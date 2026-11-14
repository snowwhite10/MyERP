using System.Data.Common;
using System.Text.Json.Serialization;

namespace MyERP.Modules.Finance.Contracts;

/// <summary>PUR purchase invoice state as reported to FI through <see cref="IFinancePurchaseInvoiceSource"/> (BR-FI-015).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinancePurchaseSourceState>))]
public enum FinancePurchaseSourceState
{
    Draft,
    Disputed,
    Rejected,
    Deleted,

    /// <summary>Three-way matched with every dispute accepted, saved in the PUR posting transaction; FI may post it.</summary>
    Matched,

    /// <summary>Already Posted by PUR. FI accepts it only as a replay of its own committed source (checked before the read).</summary>
    Posted
}

/// <summary>One saved PUR invoice line's amount components in the original and the company base currency (BR-FI-017).</summary>
public sealed record FinancePurchaseInvoiceLine(
    int LineNo, decimal NetAmount, decimal TaxAmount, decimal GrossAmount,
    decimal BaseNetAmount, decimal BaseTaxAmount, decimal BaseGrossAmount);

/// <summary>
/// Saved PUR invoice evidence read by FI inside the caller's transaction (BR-FI-015/017). Header totals and lines are the
/// values PUR saved (accepted price/tax differences included); FI checks they reconcile but never recalculates them.
/// </summary>
public sealed record FinancePurchaseInvoiceSnapshot(
    long CompanyId, long InvoiceId, int SourceVersion, string Fingerprint, long SupplierId,
    FinancePurchaseSourceState State, DateOnly BusinessDate, string Currency, string BaseCurrency, decimal ExchangeRate,
    decimal NetAmount, decimal TaxAmount, decimal GrossAmount,
    decimal BaseNetAmount, decimal BaseTaxAmount, decimal BaseGrossAmount,
    IReadOnlyList<FinancePurchaseInvoiceLine> Lines);

/// <summary>
/// FI-owned PUR purchase invoice read port (ADR-116/117). PUR implements it (WU-PUR-008a) over its own tables; FI never
/// reads <c>pur_</c> tables. It must read through the supplied transaction and return null for a missing,
/// foreign-company or out-of-scope invoice.
/// </summary>
public interface IFinancePurchaseInvoiceSource
{
    Task<FinancePurchaseInvoiceSnapshot?> ReadInvoiceAsync(long companyId, long invoiceId, DbTransaction transaction,
        CancellationToken cancellationToken = default);
}

/// <summary>The PUR caller's claim about the invoice it is posting; FI verifies every field against the saved source.</summary>
public sealed record PurchaseInvoicePostingCommand(
    long CompanyId, long InvoiceId, long SupplierId, int SourceVersion, string Fingerprint);

public enum PurchasePostingOutcomeKind { Posted, Unchanged, Rejected, Fault }

/// <summary>Committed-or-pending FI result PUR compares with its saved invoice before it marks Posted (BR-FI-019).</summary>
public sealed record PurchaseInvoicePostingReceipt(
    long CompanyId, long InvoiceId, long SupplierId, int SourceVersion, string Fingerprint,
    string Currency, decimal GrossAmount, string BaseCurrency, decimal BaseGrossAmount,
    long PurchaseSourceId, long PayableId, long JournalId);

/// <summary>
/// Posted: written in the caller's transaction (durable only when the caller commits), <c>Changed=true</c>.
/// Unchanged: identical replay of a committed source, nothing written. Rejected: nothing written, the caller's
/// transaction stays open. Fault: the caller's whole transaction has already been rolled back.
/// <c>InvalidLine</c> is the 1-based journal line whose account is unusable; <c>InvalidSourceLine</c> is the PUR line
/// number whose amounts do not reconcile.
/// </summary>
public sealed record PurchaseInvoicePostingResult(
    PurchasePostingOutcomeKind Kind, bool Changed, int HttpStatus, string? ErrorCode = null,
    PurchaseInvoicePostingReceipt? Receipt = null, int? InvalidLine = null, int? InvalidSourceLine = null);

/// <summary>
/// WU-FI-003a2b: posts one PUR purchase invoice to FI AP and GL inside the caller's open SQL transaction on the shared
/// scoped DbContext (BR-FI-015～020). Order: FI source-key lock → FI replay check → PUR source read → account map →
/// MD accounts → GL joined posting → <c>ap_purchase_source</c> + <c>ap_posted_payable</c>. Calling without an open
/// transaction is a programming error (<see cref="InvalidOperationException"/>).
/// </summary>
public interface IPurchaseInvoicePostingService
{
    Task<PurchaseInvoicePostingResult> PostInCurrentTransactionAsync(PurchaseInvoicePostingCommand? command,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable FI purchase posting error codes (front-end i18n keys).</summary>
public static class PurchasePostingErrors
{
    /// <summary>400: command shape is invalid (non-positive IDs/version, blank or oversized fingerprint).</summary>
    public const string InvalidInput = "FI.PURCHASE_POSTING.INVALID_INPUT";
    /// <summary>404: the PUR source is missing, belongs to another company or is out of scope.</summary>
    public const string SourceNotFound = "FI.PURCHASE_POSTING.SOURCE_NOT_FOUND";
    /// <summary>422: the PUR source is Draft, Disputed, Rejected, Deleted, or Posted without an FI source.</summary>
    public const string SourceNotPostable = "FI.PURCHASE_POSTING.SOURCE_NOT_POSTABLE";
    /// <summary>409: the command's supplier, version or fingerprint differs from the saved PUR source.</summary>
    public const string SourceMismatch = "FI.PURCHASE_POSTING.SOURCE_MISMATCH";
    /// <summary>409: FI already posted this source with another supplier, version or fingerprint.</summary>
    public const string SourceConflict = "FI.PURCHASE_POSTING.SOURCE_CONFLICT";
    /// <summary>422: saved amounts, lines, currency or rate do not reconcile (net + tax = gross, lines sum to header).</summary>
    public const string InvalidAmounts = "FI.PURCHASE_POSTING.INVALID_AMOUNTS";
    /// <summary>422: no usable FI account mapping for PUR purchase invoices.</summary>
    public const string MappingMissing = "FI.PURCHASE_POSTING.MAPPING_MISSING";
    /// <summary>422: a mapped account is missing, inactive or not postable in MD (with journal line number).</summary>
    public const string AccountInvalid = "FI.PURCHASE_POSTING.ACCOUNT_INVALID";
    /// <summary>409: another transaction holds the FI source key of this invoice; retry later.</summary>
    public const string LockTimeout = "FI.PURCHASE_POSTING.LOCK_TIMEOUT";
    /// <summary>500: the PUR source contract is not available in this host.</summary>
    public const string SourceContractUnavailable = "FI.PURCHASE_POSTING.SOURCE_CONTRACT_UNAVAILABLE";
    /// <summary>500: GL already holds the source key without an FI purchase source; transaction rolled back.</summary>
    public const string InconsistentState = "FI.PURCHASE_POSTING.INCONSISTENT_STATE";
    /// <summary>500: writing the FI purchase source or payable failed; transaction rolled back.</summary>
    public const string WriteFailed = "FI.PURCHASE_POSTING.WRITE_FAILED";
}
