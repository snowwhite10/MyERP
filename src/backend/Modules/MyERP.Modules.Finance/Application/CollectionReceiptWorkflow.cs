using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

public static class CollectionReceiptWorkflowErrors
{
    /// <summary>409: the receipt is no longer Pending when SYS delivers the final decision.</summary>
    public const string ApprovalStateChanged = "FI.COLLECTION.RECEIPT_APPROVAL_STATE_CHANGED";
    /// <summary>409: SYS returned an approval state that does not belong to this receipt submission.</summary>
    public const string ApprovalResultInvalid = "FI.COLLECTION.RECEIPT_APPROVAL_RESULT_INVALID";
    /// <summary>422: the company base currency differs from the saved receipt currency.</summary>
    public const string BaseCurrencyChanged = "FI.COLLECTION.BASE_CURRENCY_CHANGED";
}

/// <summary>Receipt state after a submit or withdraw; <see cref="Posting"/> is set when the receipt was posted.</summary>
public sealed record CollectionReceiptWorkflowResult(long Id, string DocumentNo, FinanceCollectionReceiptState Status,
    int Version, ApprovalState Approval, CollectionReceiptPostingReceipt? Posting);

/// <summary>
/// Final approval of a receipt inside the caller's open transaction (WU-FI-005b3a, ADR-128; BR-FI-032/033): saves
/// Approved (so the source port reads it and the version becomes the posting source version), posts through
/// <see cref="ICollectionReceiptPostingService"/> with the saved fingerprint, then marks Posted (saved by the caller).
/// A rejected or faulted posting throws the FI posting code; the caller's transaction must then roll back (Fault has
/// already rolled it back), leaving the receipt and SYS round unchanged.
/// </summary>
public sealed class CollectionReceiptApprovalPosting(MyErpDbContext db, ICollectionReceiptPostingService posting)
{
    public async Task<CollectionReceiptPostingReceipt> ApproveAndPostAsync(CollectionReceiptDocument document,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Receipt approval posting requires the caller's open transaction.");
        document.Status = CollectionReceiptDocumentStatus.Approved;
        await db.SaveChangesAsync(token);
        var result = await posting.PostInCurrentTransactionAsync(new CollectionReceiptPostingCommand(
            document.CompanyId, document.Id, document.CustomerId, document.Version,
            CollectionReceiptFingerprint.Create(document)), token);
        if (result.Kind is not (CollectionPostingOutcomeKind.Posted or CollectionPostingOutcomeKind.Unchanged)
            || result.Receipt is null)
        {
            db.ChangeTracker.Clear();
            throw ToException(result.HttpStatus, result.ErrorCode ?? CollectionPostingErrors.WriteFailed);
        }
        document.Status = CollectionReceiptDocumentStatus.Posted;
        return result.Receipt;
    }

    /// <summary>FI posting status → the matching ERP exception, keeping the stable posting error code.</summary>
    internal static Exception ToException(int status, string code) => status switch
    {
        404 => new NotFoundException(code, "Collection receipt", code),
        409 => new ConflictException(code, "The receipt could not be posted; reload and retry."),
        400 or 422 => new BusinessRuleException(code, "The receipt cannot be posted."),
        _ => new InvalidOperationException($"FI receipt posting failed ({status} {code})."),
    };
}

/// <summary>
/// SYS final decision for <c>AR.COLLECTION</c> (BR-FI-032), inside SYS's approval transaction before its SaveChanges:
/// Approved → approve and post in the same transaction (any posting failure throws, so the whole approval rolls back);
/// Rejected → Rejected (editable again, no ledger effect). SYS allows only one pending round per document, so a Pending
/// receipt identifies the round.
/// </summary>
public sealed class CollectionReceiptApprovalResultHandler(MyErpDbContext db, CollectionReceiptApprovalPosting approval)
    : IApprovalResultHandler
{
    public string DocumentType => CollectionReceiptDocumentService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned approval transaction is required.");

        var document = await db.Set<CollectionReceiptDocument>()
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException(CollectionReceiptDocumentErrors.NotFound, "Collection receipt", result.DocumentId);
        if (document.Status != CollectionReceiptDocumentStatus.Pending)
            throw new ConflictException(CollectionReceiptWorkflowErrors.ApprovalStateChanged,
                "The receipt is no longer waiting for approval.");
        if (result.Status == ApprovalStatus.Rejected)
            document.Status = CollectionReceiptDocumentStatus.Rejected;
        else
            await approval.ApproveAndPostAsync(document, cancellationToken);
    }
}

/// <summary>
/// WU-FI-005b3a: submit a Draft receipt to SYS (MD re-checked; zero-step workflow approves and posts in the same
/// transaction) and withdraw a Pending one back to Draft. Both need <c>AR.COLLECTION.SUBMIT</c>; the row is locked
/// (UPDLOCK) before 404 → 409 (version) → 422 (status).
/// </summary>
public sealed class CollectionReceiptSubmissionService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, CollectionReceiptDocumentService documents, IApprovalWorkflowService workflow,
    CollectionReceiptApprovalPosting approval)
{
    public const string SubmitPermission = "AR.COLLECTION.SUBMIT";

    public Task<CollectionReceiptWorkflowResult> SubmitAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, CollectionReceiptDocumentStatus.Draft, async document =>
        {
            var baseCurrency = await documents.PrepareAsync(new(document.CustomerId, document.BusinessDate,
                document.Amount, document.BankAccountId, document.BankReference, document.Remark), token);
            if (!string.Equals(baseCurrency, document.Currency, StringComparison.Ordinal))
                throw new BusinessRuleException(CollectionReceiptWorkflowErrors.BaseCurrencyChanged,
                    "The company base currency changed after the receipt was saved; edit and save it again.");
            document.Status = CollectionReceiptDocumentStatus.Pending;
            var state = await workflow.SubmitAsync(new ApprovalSubmission(CollectionReceiptDocumentService.DocumentType,
                document.Id, document.DocumentNo, document.Amount,
                document.Remark is { Length: > 200 } ? document.Remark[..200] : document.Remark), token);
            Verify(state, document.Id, ApprovalStatus.Pending, ApprovalStatus.Approved);
            CollectionReceiptPostingReceipt? posted = null;
            if (state.Status == ApprovalStatus.Approved)
                posted = await approval.ApproveAndPostAsync(document, token);
            return (state, posted);
        }, token);

    public Task<CollectionReceiptWorkflowResult> WithdrawAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, CollectionReceiptDocumentStatus.Pending, async document =>
        {
            var state = await workflow.CancelAsync(CollectionReceiptDocumentService.DocumentType, document.Id, token);
            Verify(state, document.Id, ApprovalStatus.Cancelled);
            document.Status = CollectionReceiptDocumentStatus.Draft;
            return (state, (CollectionReceiptPostingReceipt?)null);
        }, token);

    private async Task<CollectionReceiptWorkflowResult> RunAsync(long id, int version,
        CollectionReceiptDocumentStatus required,
        Func<CollectionReceiptDocument, Task<(ApprovalState State, CollectionReceiptPostingReceipt? Posted)>> action,
        CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(currentCompany, authorization, SubmitPermission, token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.ar_collection_receipt_doc WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token);
            var document = await db.Set<CollectionReceiptDocument>().SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(CollectionReceiptDocumentErrors.NotFound, "Collection receipt", id);
            if (version < 1 || document.Version != version) throw new ConflictException();
            if (document.Status != required)
                throw new BusinessRuleException(CollectionReceiptDocumentErrors.InvalidStatus,
                    required == CollectionReceiptDocumentStatus.Draft
                        ? "Only a Draft receipt can be submitted."
                        : "Only a Pending receipt can be withdrawn.");
            var (state, posted) = await action(document);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(document.Id, document.DocumentNo, CollectionReceiptDocumentService.ToState(document.Status),
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
        if (state is null || state.DocumentType != CollectionReceiptDocumentService.DocumentType
            || state.DocumentId != documentId || state.InstanceId <= 0 || !allowed.Contains(state.Status))
            throw new ConflictException(CollectionReceiptWorkflowErrors.ApprovalResultInvalid,
                "SYS returned an invalid receipt approval state.");
    }
}
