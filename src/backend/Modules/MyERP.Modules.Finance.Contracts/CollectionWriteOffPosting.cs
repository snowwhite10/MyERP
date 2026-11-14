namespace MyERP.Modules.Finance.Contracts;

/// <summary>The approval caller's claim about the FI collection write-off (settlement) it is posting; FI verifies every field.</summary>
public sealed record CollectionWriteOffPostingCommand(
    long CompanyId, long SettlementId, long CustomerId, int SourceVersion, string Fingerprint);

/// <summary>One committed allocation: FI receivable ID, settled base amount and the receivable's open amount when read.</summary>
public sealed record CollectionWriteOffAllocationReceipt(long ReceivableId, decimal Amount, decimal ReceivableOpenAmount);

/// <summary>
/// Committed-or-pending FI result of one write-off posting (BR-FI-035/037). <c>ReceiptId</c> is the FI receipt document ID,
/// <c>CollectionReceiptId</c> its <c>ar_collection_receipt</c> row and <c>ReceiptUnappliedAmount</c> that row's value when
/// read; <c>CollectionSettlementId</c> is the <c>ar_collection_settlement</c> row. Allocations are in receivable ID order.
/// </summary>
public sealed record CollectionWriteOffPostingReceipt(
    long CompanyId, long SettlementId, long CustomerId, int SourceVersion, string Fingerprint, string BaseCurrency,
    decimal TotalAmount, long ReceiptId, long CollectionReceiptId, decimal ReceiptUnappliedAmount,
    long CollectionSettlementId, long JournalId, IReadOnlyList<CollectionWriteOffAllocationReceipt> Allocations);

/// <summary>Why one settlement line cannot be posted (BR-FI-040: the whole settlement fails, every bad line is named).</summary>
public sealed record CollectionWriteOffLineError(int Line, long ReceivableId, string Reason);

/// <summary>
/// Posted: written in the caller's transaction (durable only when the caller commits), <c>Changed=true</c>.
/// Unchanged: identical replay of a committed settlement, nothing written. Rejected: nothing written, the caller's
/// transaction stays open. Fault: the caller's whole transaction has already been rolled back.
/// <c>LineErrors</c> accompany <see cref="CollectionWriteOffPostingErrors.AllocationInvalid"/> (1-based document lines);
/// <c>InvalidLine</c> is the 1-based journal line whose account is unusable (1 unapplied cash, 2 AR control).
/// </summary>
public sealed record CollectionWriteOffPostingResult(
    CollectionPostingOutcomeKind Kind, bool Changed, int HttpStatus, string? ErrorCode = null,
    CollectionWriteOffPostingReceipt? Receipt = null, IReadOnlyList<CollectionWriteOffLineError>? LineErrors = null,
    int? InvalidLine = null);

/// <summary>
/// WU-FI-005a2c (ADR-122): posts one approved FI collection write-off (receipt-to-invoice settlement) to GL and the FI
/// ledgers inside the caller's open SQL transaction on the shared scoped DbContext (BR-FI-035/036/037/038/040).
/// Order: credit key lock → FI replay check → saved settlement read → receipt key → receivable keys ascending →
/// settlement source key → posted receipt and receivables re-read and checked → account map → GL joined posting
/// (Dr unapplied cash, Cr AR control) → <c>ar_collection_settlement</c>/<c>ar_collection_allocation</c>, receipt
/// unapplied and receivable open amounts reduced. No partial success. Calling without an open transaction is a
/// programming error (<see cref="InvalidOperationException"/>).
/// </summary>
public interface ICollectionWriteOffPostingService
{
    Task<CollectionWriteOffPostingResult> PostInCurrentTransactionAsync(CollectionWriteOffPostingCommand? command,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable FI collection write-off posting error codes (front-end i18n keys).</summary>
public static class CollectionWriteOffPostingErrors
{
    /// <summary>400: command shape is invalid (non-positive IDs/version, blank or non-canonical fingerprint).</summary>
    public const string InvalidInput = "FI.COLLECTION_WRITEOFF.INVALID_INPUT";
    /// <summary>404: the settlement is missing, belongs to another company or is out of scope.</summary>
    public const string SourceNotFound = "FI.COLLECTION_WRITEOFF.SOURCE_NOT_FOUND";
    /// <summary>422: the settlement is not Approved.</summary>
    public const string SourceNotPostable = "FI.COLLECTION_WRITEOFF.SOURCE_NOT_POSTABLE";
    /// <summary>409: the command's customer, version or fingerprint differs from the saved settlement.</summary>
    public const string SourceMismatch = "FI.COLLECTION_WRITEOFF.SOURCE_MISMATCH";
    /// <summary>409: FI already posted this settlement with another customer, version or fingerprint.</summary>
    public const string SourceConflict = "FI.COLLECTION_WRITEOFF.SOURCE_CONFLICT";
    /// <summary>422: saved date, currency, receipt reference, total or lines are unusable (no lines, duplicates, sum ≠ total).</summary>
    public const string InvalidSource = "FI.COLLECTION_WRITEOFF.INVALID_SOURCE";
    /// <summary>422: the referenced receipt has no posted FI row (unsaved, Draft, Rejected or deleted receipt).</summary>
    public const string ReceiptNotPosted = "FI.COLLECTION_WRITEOFF.RECEIPT_NOT_POSTED";
    /// <summary>422: the posted receipt has another customer or currency, or is dated after the settlement.</summary>
    public const string ReceiptMismatch = "FI.COLLECTION_WRITEOFF.RECEIPT_MISMATCH";
    /// <summary>422: the settlement total exceeds the receipt's current unapplied amount.</summary>
    public const string ReceiptInsufficient = "FI.COLLECTION_WRITEOFF.RECEIPT_INSUFFICIENT";
    /// <summary>422: one or more lines are unusable; see <c>LineErrors</c>.</summary>
    public const string AllocationInvalid = "FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID";
    /// <summary>422: no usable FI collection account mapping.</summary>
    public const string MappingMissing = "FI.COLLECTION_WRITEOFF.MAPPING_MISSING";
    /// <summary>422: a journal account is missing, inactive or not postable (with line).</summary>
    public const string AccountInvalid = "FI.COLLECTION_WRITEOFF.ACCOUNT_INVALID";
    /// <summary>409: another transaction holds the receipt, a receivable or the settlement key; retry later.</summary>
    public const string LockTimeout = "FI.COLLECTION_WRITEOFF.LOCK_TIMEOUT";
    /// <summary>500: the FI settlement document source is not available in this host.</summary>
    public const string SourceContractUnavailable = "FI.COLLECTION_WRITEOFF.SOURCE_CONTRACT_UNAVAILABLE";
    /// <summary>500: GL already holds the source key without an FI settlement row; transaction rolled back.</summary>
    public const string InconsistentState = "FI.COLLECTION_WRITEOFF.INCONSISTENT_STATE";
    /// <summary>500: writing the FI rows failed; transaction rolled back.</summary>
    public const string WriteFailed = "FI.COLLECTION_WRITEOFF.WRITE_FAILED";
}

/// <summary>Line reasons inside <see cref="CollectionWriteOffLineError"/> (front-end i18n keys).</summary>
public static class CollectionWriteOffLineReasons
{
    /// <summary>No posted FI receivable with this ID in the company.</summary>
    public const string ReceivableNotFound = "FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_NOT_FOUND";
    /// <summary>The receivable is not an invoice receivable of the settlement's customer and currency.</summary>
    public const string ReceivableMismatch = "FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_MISMATCH";
    /// <summary>The receivable was posted after the settlement date.</summary>
    public const string ReceivableAfterSettlement = "FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_AFTER_SETTLEMENT";
    /// <summary>The amount exceeds the receivable's current open amount (credited or already settled).</summary>
    public const string ExceedsOpen = "FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN";
}
