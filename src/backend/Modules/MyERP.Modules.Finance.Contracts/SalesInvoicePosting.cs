using System.Data.Common;
using System.Text.Json.Serialization;

namespace MyERP.Modules.Finance.Contracts;

/// <summary>SD source state as reported to FI through <see cref="IFinanceSalesInvoiceSource"/> (BR-FI-007).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceSalesSourceState>))]
public enum FinanceSalesSourceState
{
    Draft,
    Pending,
    Rejected,
    Deleted,

    /// <summary>Saved inside the SD issuing transaction, not yet marked Posted; FI may post it.</summary>
    Issuing,

    /// <summary>Already Posted by SD; FI accepts it only as a replay of the same saved source.</summary>
    Posted
}

/// <summary>
/// Saved SD invoice evidence read by FI inside the caller's transaction (BR-FI-007/009). Amounts are the totals SD
/// saved independently for the original currency and the company base currency; FI never recalculates them.
/// </summary>
public sealed record FinanceSalesInvoiceSnapshot(
    long CompanyId, long InvoiceId, int SourceVersion, string Fingerprint, long CustomerId,
    FinanceSalesSourceState State, DateOnly BusinessDate, string Currency, string BaseCurrency, decimal ExchangeRate,
    decimal NetAmount, decimal TaxAmount, decimal GrossAmount,
    decimal BaseNetAmount, decimal BaseTaxAmount, decimal BaseGrossAmount);

/// <summary>
/// FI-owned SD invoice read port (ADR-107/108). SD implements it (WU-SD-007a) over its own tables; FI never reads
/// <c>sal_</c> tables. It must read through the supplied transaction and return null for a missing, foreign-company
/// or out-of-scope invoice.
/// </summary>
public interface IFinanceSalesInvoiceSource
{
    Task<FinanceSalesInvoiceSnapshot?> ReadInvoiceAsync(long companyId, long invoiceId, DbTransaction transaction,
        CancellationToken cancellationToken = default);
}

/// <summary>The SD caller's claim about the invoice it is issuing; FI verifies every field against the saved source.</summary>
public sealed record SalesInvoicePostingCommand(
    long CompanyId, long InvoiceId, long CustomerId, int SourceVersion, string Fingerprint);

public enum SalesPostingOutcomeKind { Posted, Unchanged, Rejected, Fault }

/// <summary>Committed-or-pending FI result SD compares before it commits (BR-FI-010).</summary>
public sealed record SalesInvoicePostingReceipt(
    long CompanyId, long InvoiceId, long CustomerId, int SourceVersion, string Fingerprint,
    string Currency, decimal GrossAmount, string BaseCurrency, decimal BaseGrossAmount,
    long SalesSourceId, long ReceivableId, long JournalId);

/// <summary>
/// Posted: written in the caller's transaction (durable only when the caller commits), <c>Changed=true</c>.
/// Unchanged: identical replay of a committed source, nothing written. Rejected: nothing written, the caller's
/// transaction stays open. Fault: the caller's whole transaction has already been rolled back.
/// </summary>
public sealed record SalesInvoicePostingResult(
    SalesPostingOutcomeKind Kind, bool Changed, int HttpStatus, string? ErrorCode = null,
    SalesInvoicePostingReceipt? Receipt = null, int? InvalidLine = null);

/// <summary>
/// WU-FI-002a2b: posts one SD invoice to FI AR and GL inside the caller's open SQL transaction on the shared scoped
/// DbContext (BR-FI-007～010、013～014). Order: FI credit key lock → FI replay check → SD source read → GL joined
/// posting → <c>ar_sales_source</c> + <c>ar_posted_receivable</c>. Calling without an open transaction is a programming
/// error (<see cref="InvalidOperationException"/>).
/// </summary>
public interface ISalesInvoicePostingService
{
    Task<SalesInvoicePostingResult> PostInCurrentTransactionAsync(SalesInvoicePostingCommand? command,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable FI sales posting error codes (front-end i18n keys).</summary>
public static class SalesPostingErrors
{
    /// <summary>400: command shape is invalid (non-positive IDs/version, blank or oversized fingerprint).</summary>
    public const string InvalidInput = "FI.SALES_POSTING.INVALID_INPUT";
    /// <summary>404: the SD source is missing, belongs to another company or is out of scope.</summary>
    public const string SourceNotFound = "FI.SALES_POSTING.SOURCE_NOT_FOUND";
    /// <summary>422: the SD source is Draft, Pending, Rejected or Deleted.</summary>
    public const string SourceNotPostable = "FI.SALES_POSTING.SOURCE_NOT_POSTABLE";
    /// <summary>409: the command's customer, version or fingerprint differs from the saved SD source.</summary>
    public const string SourceMismatch = "FI.SALES_POSTING.SOURCE_MISMATCH";
    /// <summary>409: FI already posted this source with another customer, version or fingerprint.</summary>
    public const string SourceConflict = "FI.SALES_POSTING.SOURCE_CONFLICT";
    /// <summary>422: saved amounts, currency or rate do not reconcile (net + tax = gross, decimal(19,4), ISO currency).</summary>
    public const string InvalidAmounts = "FI.SALES_POSTING.INVALID_AMOUNTS";
    /// <summary>422: no usable FI account mapping for SD invoices.</summary>
    public const string MappingMissing = "FI.SALES_POSTING.MAPPING_MISSING";
    /// <summary>422: a mapped account is missing, inactive or not postable in MD (with journal line number).</summary>
    public const string AccountInvalid = "FI.SALES_POSTING.ACCOUNT_INVALID";
    /// <summary>500: the SD source contract is not available in this host.</summary>
    public const string SourceContractUnavailable = "FI.SALES_POSTING.SOURCE_CONTRACT_UNAVAILABLE";
    /// <summary>500: GL already holds the source key without an FI sales source; transaction rolled back.</summary>
    public const string InconsistentState = "FI.SALES_POSTING.INCONSISTENT_STATE";
    /// <summary>500: writing the FI sales source or receivable failed; transaction rolled back.</summary>
    public const string WriteFailed = "FI.SALES_POSTING.WRITE_FAILED";
}
