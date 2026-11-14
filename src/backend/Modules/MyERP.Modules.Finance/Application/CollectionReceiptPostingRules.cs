using MyERP.Modules.Finance.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Pure checks of the approval caller's command and the saved FI receipt (WU-FI-005a2b, ADR-121; BR-FI-031/033).
/// No I/O. Money, currency and fingerprint primitives are shared with the SD invoice rules.
/// </summary>
public static class CollectionReceiptPostingRules
{
    /// <summary>Same limit as <c>ar_collection_receipt.bank_reference nvarchar(50)</c>.</summary>
    public const int BankReferenceMaxLength = 50;

    public static bool IsWellFormed(CollectionReceiptPostingCommand? command) =>
        command is { CompanyId: > 0, ReceiptId: > 0, CustomerId: > 0, SourceVersion: > 0 }
        && SalesInvoicePostingRules.IsCanonicalFingerprint(command.Fingerprint);

    /// <summary>
    /// Null when the saved receipt proves the command and is postable; otherwise the HTTP status and code. Order:
    /// identity (404) → state (422) → caller claim (409) → saved content (422). A Posted receipt FI has no committed row
    /// for is not replayable.
    /// </summary>
    public static (int Status, string Code)? CheckSnapshot(CollectionReceiptPostingCommand command,
        FinanceCollectionReceiptSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (snapshot is null || snapshot.CompanyId != command.CompanyId || snapshot.ReceiptId != command.ReceiptId)
            return (404, CollectionPostingErrors.SourceNotFound);
        if (snapshot.State != FinanceCollectionReceiptState.Approved)
            return (422, CollectionPostingErrors.SourceNotPostable);
        if (snapshot.CustomerId != command.CustomerId || snapshot.SourceVersion != command.SourceVersion
            || !string.Equals(snapshot.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return (409, CollectionPostingErrors.SourceMismatch);
        return IsUsable(snapshot) ? null : (422, CollectionPostingErrors.InvalidSource);
    }

    /// <summary>
    /// BR-FI-031/033: a business date; positive decimal(19,4) amount; ISO uppercase currency equal to the base currency
    /// (no foreign-currency receipt in this slice); a positive fund account ID; a canonical bank reference.
    /// </summary>
    private static bool IsUsable(FinanceCollectionReceiptSnapshot s) =>
        s.BusinessDate != default
        && SalesInvoicePostingRules.IsMoney(s.Amount) && s.Amount > 0m
        && SalesInvoicePostingRules.IsCurrency(s.BaseCurrency)
        && string.Equals(s.Currency, s.BaseCurrency, StringComparison.Ordinal)
        && s.BankAccountId > 0
        && IsCanonicalBankReference(s.BankReference);

    /// <summary>Non-blank, at most 50 characters, no leading/trailing white space and no control characters.</summary>
    public static bool IsCanonicalBankReference(string? reference) =>
        !string.IsNullOrWhiteSpace(reference) && reference.Length <= BankReferenceMaxLength
        && reference.Trim().Length == reference.Length && !reference.Any(char.IsControl);

    /// <summary>
    /// Key of the bank-reference applock: SQL compares references case-insensitively (default collation of the unique
    /// index), so the lock folds case the same way to serialize two receipts that differ only in case.
    /// </summary>
    public static string BankReferenceKey(string reference) => reference.ToUpperInvariant();
}
