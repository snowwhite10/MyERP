using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

public static class OtherPaymentWorkflowErrors
{
    /// <summary>409: the other payment is no longer Pending when SYS delivers the final decision.</summary>
    public const string ApprovalStateChanged = "FI.OTHER_PAYMENT.APPROVAL_STATE_CHANGED";
    /// <summary>409: SYS returned an approval state that does not belong to this submission.</summary>
    public const string ApprovalResultInvalid = "FI.OTHER_PAYMENT.APPROVAL_RESULT_INVALID";
    /// <summary>422: the company base currency differs from the saved document currency.</summary>
    public const string BaseCurrencyChanged = "FI.OTHER_PAYMENT.BASE_CURRENCY_CHANGED";
}

/// <summary>Other payment state after a submit or withdraw; <see cref="Posting"/> is set when it was posted.</summary>
public sealed record OtherPaymentWorkflowResult(long Id, string DocumentNo, OtherPaymentDocumentStatus Status,
    int Version, ApprovalState Approval, OtherPaymentPostingReceipt? Posting);

/// <summary>
/// Final approval of an other payment inside the caller's open transaction (WU-FI-008b2; BR-FI-060/064/065): saves
/// Approved (the saved version becomes the posting source version), posts through
/// <see cref="IOtherPaymentPostingService"/> with that version and <see cref="OtherPaymentFingerprint"/>, which marks
/// the document Posted. Any Rejected or Fault posting throws the FI posting code; the caller's transaction must then
/// roll back (Fault has already rolled it back), leaving the document and SYS round unchanged.
/// </summary>
public sealed class OtherPaymentApprovalPosting(MyErpDbContext db, IOtherPaymentPostingService posting)
{
    public async Task<OtherPaymentPostingReceipt> ApproveAndPostAsync(OtherPaymentDocument document,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Other payment approval posting requires the caller's open transaction.");
        document.Status = OtherPaymentDocumentStatus.Approved;
        await db.SaveChangesAsync(token);
        var result = await posting.PostInCurrentTransactionAsync(new OtherPaymentPostingCommand(
            document.CompanyId, document.Id, document.Version, OtherPaymentFingerprint.Create(document)), token);
        if (result.Kind is not (OtherPaymentPostingOutcomeKind.Posted or OtherPaymentPostingOutcomeKind.Unchanged)
            || result.Receipt is null)
        {
            db.ChangeTracker.Clear();
            throw ToException(result.HttpStatus, result.ErrorCode ?? OtherPaymentPostingErrors.WriteFailed);
        }
        document.Status = OtherPaymentDocumentStatus.Posted;
        return result.Receipt;
    }

    /// <summary>FI posting status → the matching ERP exception, keeping the stable posting error code (BR-FI-065).</summary>
    internal static Exception ToException(int status, string code) => status switch
    {
        404 => new NotFoundException(code, "Other payment", code),
        409 => new ConflictException(code, "The other payment could not be posted; reload and retry."),
        400 or 422 => new BusinessRuleException(code, "The other payment cannot be posted."),
        _ => new InvalidOperationException($"FI other payment posting failed ({status} {code})."),
    };
}

/// <summary>
/// SYS final decision for <c>AP.OTHER_PAYMENT</c> inside SYS's approval transaction, before its SaveChanges:
/// Approved → approve and post in the same transaction (any posting failure throws, so the whole approval rolls back);
/// Rejected → Rejected (editable or resubmittable, no ledger effect; SYS keeps the reason in its history). SYS allows
/// only one pending round per document, so a Pending document identifies the round.
/// </summary>
public sealed class OtherPaymentApprovalResultHandler(MyErpDbContext db, OtherPaymentApprovalPosting approval)
    : IApprovalResultHandler
{
    public string DocumentType => OtherPaymentDocumentService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned approval transaction is required.");

        var document = await db.Set<OtherPaymentDocument>()
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException(OtherPaymentDocumentErrors.NotFound, "Other payment", result.DocumentId);
        if (document.Status != OtherPaymentDocumentStatus.Pending)
            throw new ConflictException(OtherPaymentWorkflowErrors.ApprovalStateChanged,
                "The other payment is no longer waiting for approval.");
        if (result.Status == ApprovalStatus.Rejected)
            document.Status = OtherPaymentDocumentStatus.Rejected;
        else
            await approval.ApproveAndPostAsync(document, cancellationToken);
    }
}

/// <summary>
/// WU-FI-008b2: submit a Draft or Rejected other payment to SYS (MD re-checked; a zero-step workflow approves and posts
/// in the same transaction) and withdraw a Pending one back to Draft. Both need <c>AP.OTHER_PAYMENT.SUBMIT</c>; the row
/// is locked (UPDLOCK) before 404 → 409 (version) → 422 (status).
/// </summary>
public sealed class OtherPaymentSubmissionService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, OtherPaymentDocumentService documents, IApprovalWorkflowService workflow,
    OtherPaymentApprovalPosting approval)
{
    private const int ApprovalRemarkMaxLength = 200;

    public Task<OtherPaymentWorkflowResult> SubmitAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, submit: true, async document =>
        {
            var baseCurrency = await documents.PrepareAsync(new(document.PayeeDescription, document.ExpenseCategoryId,
                document.BankAccountId, document.BankReference, document.BusinessDate, document.Amount,
                document.Remark), token);
            if (!string.Equals(baseCurrency, document.Currency, StringComparison.Ordinal))
                throw new BusinessRuleException(OtherPaymentWorkflowErrors.BaseCurrencyChanged,
                    "The company base currency changed after the other payment was saved; edit and save it again.");
            document.Status = OtherPaymentDocumentStatus.Pending;
            var state = await workflow.SubmitAsync(new ApprovalSubmission(OtherPaymentDocumentService.DocumentType,
                document.Id, document.DocumentNo, document.Amount,
                document.Remark is { Length: > ApprovalRemarkMaxLength }
                    ? document.Remark[..ApprovalRemarkMaxLength]
                    : document.Remark), token);
            Verify(state, document.Id, ApprovalStatus.Pending, ApprovalStatus.Approved);
            OtherPaymentPostingReceipt? posted = null;
            if (state.Status == ApprovalStatus.Approved)
                posted = await approval.ApproveAndPostAsync(document, token);
            return (state, posted);
        }, token);

    public Task<OtherPaymentWorkflowResult> WithdrawAsync(long id, int version, CancellationToken token = default) =>
        RunAsync(id, version, submit: false, async document =>
        {
            var state = await workflow.CancelAsync(OtherPaymentDocumentService.DocumentType, document.Id, token);
            Verify(state, document.Id, ApprovalStatus.Cancelled);
            document.Status = OtherPaymentDocumentStatus.Draft;
            return (state, (OtherPaymentPostingReceipt?)null);
        }, token);

    private async Task<OtherPaymentWorkflowResult> RunAsync(long id, int version, bool submit,
        Func<OtherPaymentDocument, Task<(ApprovalState State, OtherPaymentPostingReceipt? Posted)>> action,
        CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(currentCompany, authorization,
            OtherPaymentDocumentService.SubmitPermission, token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.ap_other_payment_doc WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token);
            var document = await db.Set<OtherPaymentDocument>().SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(OtherPaymentDocumentErrors.NotFound, "Other payment", id);
            if (version < 1 || document.Version != version) throw new ConflictException();
            var allowed = submit
                ? document.Status is OtherPaymentDocumentStatus.Draft or OtherPaymentDocumentStatus.Rejected
                : document.Status == OtherPaymentDocumentStatus.Pending;
            if (!allowed)
                throw new BusinessRuleException(OtherPaymentDocumentErrors.InvalidStatus, submit
                    ? "Only a Draft or Rejected other payment can be submitted."
                    : "Only a Pending other payment can be withdrawn.");
            var (state, posted) = await action(document);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(document.Id, document.DocumentNo, document.Status, document.Version, state, posted);
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
        if (state is null || state.DocumentType != OtherPaymentDocumentService.DocumentType
            || state.DocumentId != documentId || state.InstanceId <= 0 || !allowed.Contains(state.Status))
            throw new ConflictException(OtherPaymentWorkflowErrors.ApprovalResultInvalid,
                "SYS returned an invalid other payment approval state.");
    }
}
