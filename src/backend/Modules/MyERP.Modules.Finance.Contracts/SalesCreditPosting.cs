using System.Data.Common;

namespace MyERP.Modules.Finance.Contracts;

/// <summary>
/// SD-saved totals of one original invoice line (BR-FI-011). FI uses them as the per-line cap for cumulative credits;
/// SD owns invoice lines, FI never reads <c>sal_</c> tables.
/// </summary>
public sealed record FinanceSalesInvoiceLineCap(
    decimal Quantity, decimal NetAmount, decimal TaxAmount, decimal BaseNetAmount, decimal BaseTaxAmount);

/// <summary>
/// One billed credit allocation of a return against one original invoice line (BR-FI-011). Quantity is
/// decimal(19,6); amounts are the SD-saved original/base net, tax and gross of this allocation (decimal(19,4)).
/// The unbilled portion of a return is not listed here: SD releases it without FI (BR-FI-012).
/// </summary>
public sealed record FinanceSalesReturnLine(
    long InvoiceId, long InvoiceLineId, decimal Quantity,
    decimal NetAmount, decimal TaxAmount, decimal GrossAmount,
    decimal BaseNetAmount, decimal BaseTaxAmount, decimal BaseGrossAmount,
    FinanceSalesInvoiceLineCap InvoiceLine);

/// <summary>
/// Saved SD return evidence read by FI inside the caller's transaction (BR-FI-007/011). Header amounts are the billed
/// credit totals only and must equal the sum of <see cref="Lines"/> per component.
/// </summary>
public sealed record FinanceSalesReturnSnapshot(
    long CompanyId, long ReturnId, int SourceVersion, string Fingerprint, long CustomerId,
    FinanceSalesSourceState State, DateOnly BusinessDate, string Currency, string BaseCurrency, decimal ExchangeRate,
    decimal NetAmount, decimal TaxAmount, decimal GrossAmount,
    decimal BaseNetAmount, decimal BaseTaxAmount, decimal BaseGrossAmount,
    IReadOnlyList<FinanceSalesReturnLine> Lines);

/// <summary>
/// FI-owned SD return read port (ADR-113). SD implements it (WU-SD-008a) over its own tables. It must read through the
/// supplied transaction and return null for a missing, foreign-company or out-of-scope return.
/// </summary>
public interface IFinanceSalesReturnSource
{
    Task<FinanceSalesReturnSnapshot?> ReadReturnAsync(long companyId, long returnId, DbTransaction transaction,
        CancellationToken cancellationToken = default);
}

/// <summary>The SD caller's claim about the billed return it is posting; FI verifies it against the saved source.</summary>
public sealed record SalesCreditPostingCommand(
    long CompanyId, long ReturnId, long CustomerId, int SourceVersion, string Fingerprint);

/// <summary>One saved credit allocation as returned to SD for comparison before commit.</summary>
public sealed record SalesCreditAllocationReceipt(
    long InvoiceId, long InvoiceLineId, long ReceivableId, decimal Quantity, decimal GrossAmount, decimal BaseGrossAmount);

/// <summary>Committed-or-pending FI credit result SD compares before it commits (BR-FI-010/012).</summary>
public sealed record SalesCreditPostingReceipt(
    long CompanyId, long ReturnId, long CustomerId, int SourceVersion, string Fingerprint,
    string Currency, decimal GrossAmount, string BaseCurrency, decimal BaseGrossAmount,
    long SalesSourceId, long JournalId, IReadOnlyList<SalesCreditAllocationReceipt> Allocations);

/// <summary>Same outcome semantics as <see cref="SalesInvoicePostingResult"/>; <c>InvalidLine</c> is the 1-based return line.</summary>
public sealed record SalesCreditPostingResult(
    SalesPostingOutcomeKind Kind, bool Changed, int HttpStatus, string? ErrorCode = null,
    SalesCreditPostingReceipt? Receipt = null, int? InvalidLine = null);

/// <summary>
/// WU-FI-002a3 (ADR-113): posts one billed SD return as an FI credit source inside the caller's open SQL transaction.
/// Order: FI credit key lock → FI replay check → SD return read → original FI invoices and prior allocations →
/// cumulative/open-AR checks → joined GL credit journal → source, allocations and open-AR reductions.
/// </summary>
public interface ISalesCreditPostingService
{
    Task<SalesCreditPostingResult> PostInCurrentTransactionAsync(SalesCreditPostingCommand? command,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable FI credit posting error codes added by WU-FI-002a3 (front-end i18n keys).</summary>
public static class SalesCreditPostingErrors
{
    /// <summary>422: the same original invoice line appears twice in one return (with the 1-based line).</summary>
    public const string DuplicateLine = "FI.SALES_POSTING.DUPLICATE_LINE";
    /// <summary>422: a return line references an invoice FI has not posted (or posted as another source type).</summary>
    public const string OriginalNotPosted = "FI.SALES_POSTING.ORIGINAL_NOT_POSTED";
    /// <summary>422: the original FI invoice belongs to another customer or currency than the return.</summary>
    public const string OriginalMismatch = "FI.SALES_POSTING.ORIGINAL_MISMATCH";
    /// <summary>422: cumulative credits would exceed the saved invoice line quantity or a component.</summary>
    public const string CreditExceedsLine = "FI.SALES_POSTING.CREDIT_EXCEEDS_LINE";
    /// <summary>422: cumulative credits would exceed the FI-saved invoice net or tax totals.</summary>
    public const string CreditExceedsInvoice = "FI.SALES_POSTING.CREDIT_EXCEEDS_INVOICE";
    /// <summary>422: the credit would drive the original receivable's open amount below zero (BR-FI-012).</summary>
    public const string CreditExceedsOpen = "FI.SALES_POSTING.CREDIT_EXCEEDS_OPEN";
}
