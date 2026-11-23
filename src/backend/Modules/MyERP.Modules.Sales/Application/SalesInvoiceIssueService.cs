using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// The FI posting contract failed in a way the caller cannot fix (FI fault, unexpected FI result or a receipt that does
/// not match the saved invoice). The whole ISSUE transaction has been rolled back; the global handler returns HTTP 500
/// keeping its error code (ADR-179).
/// </summary>
public sealed class SalesInvoicePostingException(string errorCode, string message)
    : InternalErrorException(errorCode, message);

/// <summary>
/// WU-SD-007a3a (REQ_F4112.5.1, BR-SD-040～041, FI-SALES-003, ADR-178): the ISSUE command. In one Read Committed
/// transaction it saves the Issuing invoice through <see cref="SalesInvoiceIssuing"/> (taking the FI customer credit
/// lock in the pre-lock hook), asks FI to post it through <see cref="ISalesInvoicePostingService"/>, checks the FI
/// receipt against the saved invoice and only then marks it Posted with the FI IDs and commits. Every failure leaves
/// neither invoice, number, FI source, receivable nor journal. The released SD credit commitment is derived: an
/// Approved order commits its base gross minus the base gross of its Posted invoices (<see cref="SalesOrderCreditCheck"/>),
/// so the same commit that adds FI open AR removes the commitment.
/// </summary>
public sealed class SalesInvoiceIssueService(MyErpDbContext db, SalesInvoiceIssuing issuing,
    SalesCompanyAccess company, ICustomerCreditService credit, ISalesInvoicePostingService posting, IClock clock)
{
    public const string ReceiptMismatch = "SAL.INVOICE.FI_RECEIPT_MISMATCH";
    public const string PostingFailed = "SAL.INVOICE.FI_POSTING_FAILED";

    /// <summary>
    /// Issues one invoice, or replays an identical request key (returning the committed Posted invoice without
    /// calling FI). Errors: 400/403/404/409/422 from the SD checks and from FI's Rejected results;
    /// <see cref="SalesInvoicePostingException"/> for an FI fault or an inconsistent FI receipt.
    /// </summary>
    public async Task<SalesInvoiceIssueOutcome> IssueAsync(SalesInvoiceIssueInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Invoice ISSUE owns its transaction; do not call it inside another.");
        var companyId = company.RequireCompanyId();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var committed = false;
        try
        {
            var dbTransaction = transaction.GetDbTransaction();
            var saved = await issuing.SaveIssuingAsync(input,
                (customerId, t) => credit.AcquireCreditLockAsync(companyId, customerId, dbTransaction, t), token);
            if (saved.Replayed)
                return saved; // Nothing was written; the finally block rolls the read-only transaction back.

            var invoice = saved.Invoice;
            var command = new SalesInvoicePostingCommand(companyId, invoice.Id, invoice.CustomerId,
                SalesInvoice.SourceRevision, invoice.RequestFingerprint);
            var result = await posting.PostInCurrentTransactionAsync(command, token);
            if (result.Kind != SalesPostingOutcomeKind.Posted)
                throw Failure(result);
            var receipt = result.Receipt;
            if (receipt is null || !Matches(receipt, command, invoice))
                throw new SalesInvoicePostingException(ReceiptMismatch,
                    "The FI posting receipt does not match the saved invoice.");

            invoice.Status = SalesInvoiceStatus.Posted;
            invoice.PostedAt = clock.LocalNow;
            invoice.FiSalesSourceId = receipt.SalesSourceId;
            invoice.FiReceivableId = receipt.ReceivableId;
            invoice.FiJournalId = receipt.JournalId;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            committed = true;
            return saved;
        }
        finally
        {
            if (!committed)
            {
                // FI Fault has already rolled back; rolling back an ended transaction is a no-op here.
                if (db.Database.CurrentTransaction is not null)
                    await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>BR-FI-010 / BR-SD-041: the receipt must prove source identity, currency and gross amount.</summary>
    public static bool Matches(SalesInvoicePostingReceipt receipt, SalesInvoicePostingCommand command,
        SalesInvoice invoice) =>
        receipt.CompanyId == command.CompanyId && receipt.InvoiceId == command.InvoiceId
        && receipt.CustomerId == command.CustomerId && receipt.SourceVersion == command.SourceVersion
        && string.Equals(receipt.Fingerprint, command.Fingerprint, StringComparison.Ordinal)
        && string.Equals(receipt.Currency, invoice.CurrencyCode, StringComparison.Ordinal)
        && receipt.GrossAmount == invoice.OriginalGross
        && string.Equals(receipt.BaseCurrency, invoice.BaseCurrencyCode, StringComparison.Ordinal)
        && receipt.BaseGrossAmount == invoice.BaseGross
        && receipt is { SalesSourceId: > 0, ReceivableId: > 0, JournalId: > 0 };

    /// <summary>
    /// Maps a non-Posted FI result. Rejected 404/409/422 keep FI's code (the user can act on it); anything else —
    /// a Fault, Unchanged for a freshly saved invoice, or a Rejected status that means SD built a bad command — is an
    /// internal failure.
    /// </summary>
    public static Exception Failure(SalesInvoicePostingResult result) => result switch
    {
        { Kind: SalesPostingOutcomeKind.Rejected, HttpStatus: 404, ErrorCode: { } code } =>
            new NotFoundException(code, "Sales invoice FI source", "invoice"),
        { Kind: SalesPostingOutcomeKind.Rejected, HttpStatus: 409, ErrorCode: { } code } =>
            new ConflictException(code, "FI refused the invoice posting because of a conflict."),
        { Kind: SalesPostingOutcomeKind.Rejected, HttpStatus: 422, ErrorCode: { } code } =>
            new BusinessRuleException(code, "FI refused the invoice posting.")
            {
                Extensions = result.InvalidLine is { } line
                    ? new Dictionary<string, object?> { ["journalLineNo"] = line }
                    : null
            },
        _ => new SalesInvoicePostingException(result.ErrorCode ?? PostingFailed,
            $"FI invoice posting failed ({result.Kind}, {result.HttpStatus}).")
    };
}
