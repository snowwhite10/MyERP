using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Caller's posting claim for one saved other payment (WU-FI-008a4, BR-FI-060/061): the document ID, the version the
/// caller read and <see cref="OtherPaymentFingerprint"/> of that read. Account, amount, journal and actor are never
/// taken from the caller.
/// </summary>
public sealed record OtherPaymentPostingCommand(long CompanyId, long PaymentId, int SourceVersion, string? Fingerprint);

public enum OtherPaymentPostingOutcomeKind { Posted, Unchanged, Rejected, Fault }

/// <summary>Committed (or, before the caller commits, pending) Posted source of an other payment.</summary>
public sealed record OtherPaymentPostingReceipt(long CompanyId, long PaymentId, int SourceVersion, string Fingerprint,
    long SourceRowId, long JournalId, DateOnly BusinessDate, long ExpenseCategoryId, long ExpenseAccountId,
    int MappingVersion, long BankAccountId, string BankReference, string BaseCurrency, decimal Amount);

/// <summary>
/// Posted/<c>changed=true</c>: rows written in the caller's transaction. Unchanged: identical committed claim, nothing
/// written. Rejected: nothing written and the caller's transaction is still open. Fault: the caller's whole
/// transaction has been rolled back.
/// </summary>
public sealed record OtherPaymentPostingResult(OtherPaymentPostingOutcomeKind Kind, bool Changed, int HttpStatus,
    string? ErrorCode = null, int? InvalidLine = null, OtherPaymentPostingReceipt? Receipt = null);

public static class OtherPaymentPostingErrors
{
    /// <summary>400: non-positive IDs/version or a non-canonical fingerprint.</summary>
    public const string InvalidInput = "FI.OTHER_PAYMENT_POSTING.INVALID_INPUT";
    /// <summary>404: missing document, another company, or a command for another company.</summary>
    public const string SourceNotFound = "FI.OTHER_PAYMENT_POSTING.SOURCE_NOT_FOUND";
    /// <summary>422: the saved document is Rejected, deleted, or Posted without an FI source row.</summary>
    public const string SourceNotPostable = "FI.OTHER_PAYMENT_POSTING.SOURCE_NOT_POSTABLE";
    /// <summary>409: the command's version or fingerprint differs from the saved document (stale read).</summary>
    public const string SourceMismatch = "FI.OTHER_PAYMENT_POSTING.SOURCE_MISMATCH";
    /// <summary>409: FI already posted this document with another version or fingerprint.</summary>
    public const string SourceConflict = "FI.OTHER_PAYMENT_POSTING.SOURCE_CONFLICT";
    /// <summary>422: the saved currency is not the current base currency or the saved content is unusable.</summary>
    public const string InvalidSource = "FI.OTHER_PAYMENT_POSTING.INVALID_SOURCE";
    /// <summary>409: another Posted other payment already uses the withdrawal reference of the fund account.</summary>
    public const string BankReferenceDuplicate = "FI.OTHER_PAYMENT_POSTING.BANK_REFERENCE_DUPLICATE";
    /// <summary>422: the MD category is missing, inactive or not an Expense category.</summary>
    public const string CategoryInvalid = "FI.OTHER_PAYMENT_POSTING.CATEGORY_INVALID";
    /// <summary>422: the MD fund account is missing, inactive, in another currency or has no ledger account.</summary>
    public const string FundAccountInvalid = "FI.OTHER_PAYMENT_POSTING.FUND_ACCOUNT_INVALID";
    /// <summary>422: no FI category → expense account mapping for the category.</summary>
    public const string MappingMissing = "FI.OTHER_PAYMENT_POSTING.MAPPING_MISSING";
    /// <summary>
    /// 422 with the journal line: the mapped account is missing, inactive, not postable or not an Expense/Cost account
    /// (AP control, unapplied-payment clearing and prepaid assets are excluded), or the bank ledger is unusable.
    /// </summary>
    public const string AccountInvalid = "FI.OTHER_PAYMENT_POSTING.ACCOUNT_INVALID";
    /// <summary>409: another transaction holds the bank-reference or source key; retry later.</summary>
    public const string LockTimeout = "FI.OTHER_PAYMENT_POSTING.LOCK_TIMEOUT";
    /// <summary>500: GL already holds the source key without an FI source row; transaction rolled back.</summary>
    public const string InconsistentState = "FI.OTHER_PAYMENT_POSTING.INCONSISTENT_STATE";
    /// <summary>500: writing the FI source or the Posted status failed; transaction rolled back.</summary>
    public const string WriteFailed = "FI.OTHER_PAYMENT_POSTING.WRITE_FAILED";
}

/// <summary>
/// WU-FI-008a4 (ADR-211, BR-FI-060/061/062): posts one saved other payment inside the caller's open SQL transaction —
/// Dr mapped expense, Cr fund account ledger, same base amount — writes <c>ap_other_payment_source</c> and marks the
/// document Posted. 008b's SYS final approval is the production caller.
/// </summary>
public interface IOtherPaymentPostingService
{
    Task<OtherPaymentPostingResult> PostInCurrentTransactionAsync(OtherPaymentPostingCommand? command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Versioned canonical hash of the saved other payment fields the posting writes (BR-FI-062): company, document ID,
/// expense category, business date, currency, amount (4 decimals), fund account and withdrawal reference. Status,
/// number, payee, remark and audit fields are excluded, so a status transition does not change it. Length-prefixed.
/// Result: <c>FI-OTHER-PAYMENT-v1:</c> + 64 lowercase hex characters.
/// </summary>
public static class OtherPaymentFingerprint
{
    public const string Prefix = "FI-OTHER-PAYMENT-v1:";

    public static string Create(OtherPaymentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var canonical = new StringBuilder(Prefix);
        Add(canonical, document.CompanyId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.Id.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.ExpenseCategoryId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(canonical, document.Currency);
        Add(canonical, document.Amount.ToString("0.0000", CultureInfo.InvariantCulture));
        Add(canonical, document.BankAccountId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.BankReference);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Add(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
}

/// <summary>Pure checks of the other payment posting (no I/O).</summary>
public static class OtherPaymentPostingRules
{
    public static bool IsWellFormed(OtherPaymentPostingCommand? command) =>
        command is { CompanyId: > 0, PaymentId: > 0, SourceVersion: > 0 }
        && SalesInvoicePostingRules.IsCanonicalFingerprint(command.Fingerprint);

    /// <summary>Draft, Pending (SYS final approval) or Approved may post; Rejected and Posted may not (BR-FI-060/064).</summary>
    public static bool IsPostable(OtherPaymentDocument document) =>
        !document.IsDeleted && document.Status is OtherPaymentDocumentStatus.Draft
            or OtherPaymentDocumentStatus.Pending or OtherPaymentDocumentStatus.Approved;

    /// <summary>
    /// BR-FI-060/063: only an active, postable Expense or Cost account is a current expense. Liabilities (AP control,
    /// unapplied-payment clearing) and assets (prepaid expense) need separate contracts and are rejected.
    /// </summary>
    public static bool IsExpenseAccount(AccountInfo? account) =>
        account is { IsPostable: true, AccountType: AccountType.Expense or AccountType.Cost };

    /// <summary>Saved content usable for posting: positive money, canonical reference, base currency (BR-FI-059).</summary>
    public static bool IsUsable(OtherPaymentDocument document, string? baseCurrency) =>
        document.BusinessDate != default
        && SalesInvoicePostingRules.IsMoney(document.Amount) && document.Amount > 0m
        && SalesInvoicePostingRules.IsCurrency(baseCurrency)
        && string.Equals(document.Currency, baseCurrency, StringComparison.Ordinal)
        && document.BankAccountId > 0 && document.ExpenseCategoryId > 0
        && CollectionReceiptPostingRules.IsCanonicalBankReference(document.BankReference);
}
