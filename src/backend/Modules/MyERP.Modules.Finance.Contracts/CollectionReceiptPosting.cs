namespace MyERP.Modules.Finance.Contracts;

/// <summary>The approval caller's claim about the FI sales collection receipt it is posting; FI verifies every field.</summary>
public sealed record CollectionReceiptPostingCommand(
    long CompanyId, long ReceiptId, long CustomerId, int SourceVersion, string Fingerprint);

public enum CollectionPostingOutcomeKind { Posted, Unchanged, Rejected, Fault }

/// <summary>
/// Committed-or-pending FI result of one receipt posting (BR-FI-033/034). <c>CollectionReceiptId</c> is the
/// <c>ar_collection_receipt</c> row; <c>UnappliedAmount</c> is its value when read (equal to <c>Amount</c> on first post,
/// possibly lower on a later replay after settlements).
/// </summary>
public sealed record CollectionReceiptPostingReceipt(
    long CompanyId, long ReceiptId, long CustomerId, int SourceVersion, string Fingerprint,
    string BaseCurrency, decimal Amount, decimal UnappliedAmount, long BankAccountId, string BankReference,
    long CollectionReceiptId, long JournalId);

/// <summary>
/// Posted: written in the caller's transaction (durable only when the caller commits), <c>Changed=true</c>.
/// Unchanged: identical replay of a committed receipt, nothing written. Rejected: nothing written, the caller's
/// transaction stays open. Fault: the caller's whole transaction has already been rolled back.
/// <c>InvalidLine</c> is the 1-based journal line whose account is unusable (1 bank, 2 unapplied cash).
/// </summary>
public sealed record CollectionReceiptPostingResult(
    CollectionPostingOutcomeKind Kind, bool Changed, int HttpStatus, string? ErrorCode = null,
    CollectionReceiptPostingReceipt? Receipt = null, int? InvalidLine = null);

/// <summary>
/// WU-FI-005a2b (ADR-121): posts one approved FI sales collection receipt to GL and the FI unapplied-cash ledger inside
/// the caller's open SQL transaction on the shared scoped DbContext (BR-FI-033/034/036/038). Order: credit key lock →
/// receipt source-key lock → FI replay check → saved receipt read → bank-reference lock and duplicate check → account
/// map → MD fund account → MD accounts → GL joined posting (Dr bank, Cr unapplied cash) → <c>ar_collection_receipt</c>
/// with unapplied = amount. Open AR is not changed. Calling without an open transaction is a programming error
/// (<see cref="InvalidOperationException"/>).
/// </summary>
public interface ICollectionReceiptPostingService
{
    Task<CollectionReceiptPostingResult> PostInCurrentTransactionAsync(CollectionReceiptPostingCommand? command,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable FI collection receipt posting error codes (front-end i18n keys).</summary>
public static class CollectionPostingErrors
{
    /// <summary>400: command shape is invalid (non-positive IDs/version, blank or non-canonical fingerprint).</summary>
    public const string InvalidInput = "FI.COLLECTION_POSTING.INVALID_INPUT";
    /// <summary>404: the receipt is missing, belongs to another company or is out of scope.</summary>
    public const string SourceNotFound = "FI.COLLECTION_POSTING.SOURCE_NOT_FOUND";
    /// <summary>422: the receipt is not Approved (Draft, Pending, Rejected, Deleted, or Posted without an FI row).</summary>
    public const string SourceNotPostable = "FI.COLLECTION_POSTING.SOURCE_NOT_POSTABLE";
    /// <summary>409: the command's customer, version or fingerprint differs from the saved receipt.</summary>
    public const string SourceMismatch = "FI.COLLECTION_POSTING.SOURCE_MISMATCH";
    /// <summary>409: FI already posted this receipt with another customer, version or fingerprint.</summary>
    public const string SourceConflict = "FI.COLLECTION_POSTING.SOURCE_CONFLICT";
    /// <summary>422: saved amount, currency, date, bank or bank reference is unusable (BR-FI-031/033).</summary>
    public const string InvalidSource = "FI.COLLECTION_POSTING.INVALID_SOURCE";
    /// <summary>409: another posted receipt already uses this bank reference for the same fund account.</summary>
    public const string BankReferenceDuplicate = "FI.COLLECTION_POSTING.BANK_REFERENCE_DUPLICATE";
    /// <summary>422: the MD fund account is missing, inactive, in another currency or has no ledger account.</summary>
    public const string FundAccountInvalid = "FI.COLLECTION_POSTING.FUND_ACCOUNT_INVALID";
    /// <summary>422: no usable FI collection account mapping.</summary>
    public const string MappingMissing = "FI.COLLECTION_POSTING.MAPPING_MISSING";
    /// <summary>422: a journal account is missing, inactive, not postable, or bank equals unapplied cash (with line).</summary>
    public const string AccountInvalid = "FI.COLLECTION_POSTING.ACCOUNT_INVALID";
    /// <summary>409: another transaction holds this receipt's source key or bank reference; retry later.</summary>
    public const string LockTimeout = "FI.COLLECTION_POSTING.LOCK_TIMEOUT";
    /// <summary>500: the FI receipt document source is not available in this host.</summary>
    public const string SourceContractUnavailable = "FI.COLLECTION_POSTING.SOURCE_CONTRACT_UNAVAILABLE";
    /// <summary>500: GL already holds the source key without an FI receipt row; transaction rolled back.</summary>
    public const string InconsistentState = "FI.COLLECTION_POSTING.INCONSISTENT_STATE";
    /// <summary>500: writing the FI receipt row failed; transaction rolled back.</summary>
    public const string WriteFailed = "FI.COLLECTION_POSTING.WRITE_FAILED";
}
