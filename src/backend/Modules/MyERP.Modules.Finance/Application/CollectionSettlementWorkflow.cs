using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

public static class CollectionSettlementWorkflowErrors
{
    /// <summary>409: the settlement is no longer Pending when SYS delivers the final decision.</summary>
    public const string ApprovalStateChanged = "FI.COLLECTION.SETTLEMENT_APPROVAL_STATE_CHANGED";
    /// <summary>409: SYS returned an approval state that does not belong to this settlement submission.</summary>
    public const string ApprovalResultInvalid = "FI.COLLECTION.SETTLEMENT_APPROVAL_RESULT_INVALID";
}

/// <summary>Settlement state after a submit or withdraw; <see cref="Posting"/> is set when the settlement was posted.</summary>
public sealed record CollectionSettlementWorkflowResult(long Id, string DocumentNo, FinanceCollectionWriteOffState Status,
    int Version, ApprovalState Approval, CollectionWriteOffPostingReceipt? Posting);

/// <summary>
/// Final approval of a settlement inside the caller's open transaction (WU-FI-005b3b, ADR-128; BR-FI-032/035/040):
/// saves Approved (so the source port reads it and the version becomes the posting source version), posts through
/// <see cref="ICollectionWriteOffPostingService"/> with the saved fingerprint, then marks Posted (saved by the caller).
/// A rejected or faulted posting throws the FI posting code (with <c>lineErrors</c> for bad lines); the caller's
/// transaction must then roll back, leaving the settlement, balances and SYS round unchanged.
/// </summary>
public sealed class CollectionSettlementApprovalPosting(MyErpDbContext db, ICollectionWriteOffPostingService posting)
{
    public async Task<CollectionWriteOffPostingReceipt> ApproveAndPostAsync(CollectionSettlementDocument document,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Settlement approval posting requires the caller's open transaction.");
        document.Status = CollectionSettlementDocumentStatus.Approved;
        await db.SaveChangesAsync(token);
        var lines = await db.Set<CollectionSettlementDocumentLine>().AsNoTracking()
            .Where(x => x.SettlementDocumentId == document.Id).OrderBy(x => x.LineNo).ToListAsync(token);
        var result = await posting.PostInCurrentTransactionAsync(new CollectionWriteOffPostingCommand(
            document.CompanyId, document.Id, document.CustomerId, document.Version,
            CollectionSettlementFingerprint.Create(document, lines)), token);
        if (result.Kind is not (CollectionPostingOutcomeKind.Posted or CollectionPostingOutcomeKind.Unchanged)
            || result.Receipt is null)
        {
            db.ChangeTracker.Clear();
            throw ToException(result);
        }
        document.Status = CollectionSettlementDocumentStatus.Posted;
        return result.Receipt;
    }

    /// <summary>FI write-off posting failure → ERP exception with the stable code; bad lines travel as <c>lineErrors</c>.</summary>
    private static Exception ToException(CollectionWriteOffPostingResult result)
    {
        var code = result.ErrorCode ?? CollectionWriteOffPostingErrors.WriteFailed;
        if (result.LineErrors is { Count: > 0 } lineErrors && result.HttpStatus is 400 or 422)
            return new BusinessRuleException(code, "One or more lines cannot be settled.")
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [CollectionSettlementDocumentService.LineErrorsExtension] = lineErrors
                }
            };
        return result.HttpStatus switch
        {
            404 => new NotFoundException(code, "Collection settlement", code),
            409 => new ConflictException(code, "The settlement could not be posted; reload and retry."),
            400 or 422 => new BusinessRuleException(code, "The settlement cannot be posted."),
            _ => new InvalidOperationException($"FI write-off posting failed ({result.HttpStatus} {code})."),
        };
    }
}

/// <summary>
/// SYS final decision for <c>AR.SETTLEMENT</c> (BR-FI-032), inside SYS's approval transaction before its SaveChanges:
/// Approved → approve and post in the same transaction (any posting failure throws, so the whole approval rolls back);
/// Rejected → Rejected (editable again, no ledger effect). SYS allows one pending round per document.
/// </summary>
public sealed class CollectionSettlementApprovalResultHandler(MyErpDbContext db, CollectionSettlementApprovalPosting approval)
    : IApprovalResultHandler
{
    public string DocumentType => CollectionSettlementDocumentService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned approval transaction is required.");

        var document = await db.Set<CollectionSettlementDocument>()
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException(CollectionSettlementDocumentErrors.NotFound, "Collection settlement", result.DocumentId);
        if (document.Status != CollectionSettlementDocumentStatus.Pending)
            throw new ConflictException(CollectionSettlementWorkflowErrors.ApprovalStateChanged,
                "The settlement is no longer waiting for approval.");
        if (result.Status == ApprovalStatus.Rejected)
            document.Status = CollectionSettlementDocumentStatus.Rejected;
        else
            await approval.ApproveAndPostAsync(document, cancellationToken);
    }
}

/// <summary>
/// WU-FI-005b3b: submit a Draft settlement to SYS (re-checked with the draft rules: posted receipt, open receivables,
/// unapplied amount, base currency; a zero-step workflow approves and posts in the same transaction) and withdraw a
/// Pending one back to Draft. Both need <c>AR.SETTLEMENT.SUBMIT</c>; the row is locked (UPDLOCK) before
/// 404 → 409 (version) → 422 (status).
/// </summary>
public sealed class CollectionSettlementSubmissionService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, CollectionSettlementDocumentService documents, IApprovalWorkflowService workflow,
    CollectionSettlementApprovalPosting approval)
{
    public const string SubmitPermission = "AR.SETTLEMENT.SUBMIT";

    public Task<CollectionSettlementWorkflowResult> SubmitAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, CollectionSettlementDocumentStatus.Draft, async (companyId, document) =>
        {
            var lines = await db.Set<CollectionSettlementDocumentLine>().AsNoTracking()
                .Where(x => x.SettlementDocumentId == document.Id).OrderBy(x => x.LineNo)
                .Select(x => new CollectionSettlementLineInput(x.ReceivableId, x.Amount)).ToListAsync(token);
            var (_, baseCurrency) = await documents.PrepareAsync(companyId, new(document.ReceiptId,
                document.BusinessDate, lines, document.Remark), token);
            if (!string.Equals(baseCurrency, document.Currency, StringComparison.Ordinal))
                throw new BusinessRuleException(CollectionReceiptWorkflowErrors.BaseCurrencyChanged,
                    "The company base currency changed after the settlement was saved; edit and save it again.");
            document.Status = CollectionSettlementDocumentStatus.Pending;
            var state = await workflow.SubmitAsync(new ApprovalSubmission(CollectionSettlementDocumentService.DocumentType,
                document.Id, document.DocumentNo, document.TotalAmount,
                document.Remark is { Length: > 200 } ? document.Remark[..200] : document.Remark), token);
            Verify(state, document.Id, ApprovalStatus.Pending, ApprovalStatus.Approved);
            CollectionWriteOffPostingReceipt? posted = null;
            if (state.Status == ApprovalStatus.Approved)
                posted = await approval.ApproveAndPostAsync(document, token);
            return (state, posted);
        }, token);

    public Task<CollectionSettlementWorkflowResult> WithdrawAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, CollectionSettlementDocumentStatus.Pending, async (_, document) =>
        {
            var state = await workflow.CancelAsync(CollectionSettlementDocumentService.DocumentType, document.Id, token);
            Verify(state, document.Id, ApprovalStatus.Cancelled);
            document.Status = CollectionSettlementDocumentStatus.Draft;
            return (state, (CollectionWriteOffPostingReceipt?)null);
        }, token);

    private async Task<CollectionSettlementWorkflowResult> RunAsync(long id, int version,
        CollectionSettlementDocumentStatus required,
        Func<long, CollectionSettlementDocument, Task<(ApprovalState State, CollectionWriteOffPostingReceipt? Posted)>> action,
        CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(currentCompany, authorization, SubmitPermission, token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.ar_collection_settlement_doc WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token);
            var document = await db.Set<CollectionSettlementDocument>().SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(CollectionSettlementDocumentErrors.NotFound, "Collection settlement", id);
            if (version < 1 || document.Version != version) throw new ConflictException();
            if (document.Status != required)
                throw new BusinessRuleException(CollectionSettlementDocumentErrors.InvalidStatus,
                    required == CollectionSettlementDocumentStatus.Draft
                        ? "Only a Draft settlement can be submitted."
                        : "Only a Pending settlement can be withdrawn.");
            var (state, posted) = await action(companyId, document);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(document.Id, document.DocumentNo, CollectionSettlementDocumentService.ToState(document.Status),
                document.Version, state, posted);
        }
        catch (Exception ex)
        {
            if (db.Database.CurrentTransaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    private static void Verify(ApprovalState state, long documentId, params ApprovalStatus[] allowed)
    {
        if (state is null || state.DocumentType != CollectionSettlementDocumentService.DocumentType
            || state.DocumentId != documentId || state.InstanceId <= 0 || !allowed.Contains(state.Status))
            throw new ConflictException(CollectionSettlementWorkflowErrors.ApprovalResultInvalid,
                "SYS returned an invalid settlement approval state.");
    }
}
