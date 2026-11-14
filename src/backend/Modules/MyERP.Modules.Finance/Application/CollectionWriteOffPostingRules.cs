using MyERP.Modules.Finance.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>The posted FI receipt as re-read under its key (<c>ar_collection_receipt</c>).</summary>
public sealed record CollectionWriteOffReceiptBalance(long CustomerId, string BaseCurrency, DateOnly BusinessDate,
    decimal UnappliedAmount);

/// <summary>A posted FI receivable as re-read under its key (<c>ar_posted_receivable</c>).</summary>
public sealed record CollectionWriteOffReceivable(long Id, long CustomerId, string SourceType, string BaseCurrency,
    decimal OpenAmount, DateOnly PostingDate);

/// <summary>
/// Pure checks of the approval caller's command, the saved FI write-off document and the re-read FI balances
/// (WU-FI-005a2c, ADR-122; BR-FI-035/037/040). No I/O.
/// </summary>
public static class CollectionWriteOffPostingRules
{
    /// <summary>FI receivable source type a settlement may allocate to (credit notes cannot be settled by cash).</summary>
    public const string InvoiceReceivableType = "Invoice";

    /// <summary>REQ_F4116.5.2: one settlement allocates to 1–1000 distinct receivables.</summary>
    public const int MaxLines = 1000;

    public static bool IsWellFormed(CollectionWriteOffPostingCommand? command) =>
        command is { CompanyId: > 0, SettlementId: > 0, CustomerId: > 0, SourceVersion: > 0 }
        && SalesInvoicePostingRules.IsCanonicalFingerprint(command.Fingerprint);

    /// <summary>
    /// Null when the saved settlement proves the command and is postable; otherwise the HTTP status and code. Order:
    /// identity (404) → state (422) → caller claim (409) → saved content (422).
    /// </summary>
    public static (int Status, string Code)? CheckSnapshot(CollectionWriteOffPostingCommand command,
        FinanceCollectionWriteOffSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (snapshot is null || snapshot.CompanyId != command.CompanyId || snapshot.SettlementId != command.SettlementId)
            return (404, CollectionWriteOffPostingErrors.SourceNotFound);
        if (snapshot.State != FinanceCollectionWriteOffState.Approved)
            return (422, CollectionWriteOffPostingErrors.SourceNotPostable);
        if (snapshot.CustomerId != command.CustomerId || snapshot.SourceVersion != command.SourceVersion
            || !string.Equals(snapshot.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return (409, CollectionWriteOffPostingErrors.SourceMismatch);
        return IsUsable(snapshot) ? null : (422, CollectionWriteOffPostingErrors.InvalidSource);
    }

    /// <summary>
    /// BR-FI-031/035: a business date; ISO uppercase base currency; a positive receipt ID; a positive decimal(19,4) total;
    /// 1–1000 lines; positive, unique receivable IDs; positive decimal(19,4) amounts whose sum equals the total.
    /// </summary>
    private static bool IsUsable(FinanceCollectionWriteOffSnapshot s)
    {
        if (s.BusinessDate == default || !SalesInvoicePostingRules.IsCurrency(s.BaseCurrency)
            || !string.Equals(s.Currency, s.BaseCurrency, StringComparison.Ordinal) || s.ReceiptId <= 0
            || !SalesInvoicePostingRules.IsMoney(s.TotalAmount) || s.TotalAmount <= 0m
            || s.Lines is not { Count: > 0 and <= MaxLines })
            return false;
        var ids = new HashSet<long>();
        var sum = 0m;
        foreach (var line in s.Lines)
        {
            if (line is null || line.ReceivableId <= 0 || !ids.Add(line.ReceivableId)
                || !SalesInvoicePostingRules.IsMoney(line.Amount) || line.Amount <= 0m)
                return false;
            sum += line.Amount;
            if (sum > s.TotalAmount)
                return false;
        }
        return sum == s.TotalAmount;
    }

    /// <summary>
    /// The posted receipt must exist (422 not posted), belong to the settlement customer and currency and not be dated
    /// after the settlement (422 mismatch), and its current unapplied amount must cover the total (422 insufficient).
    /// </summary>
    public static (int Status, string Code)? CheckReceipt(FinanceCollectionWriteOffSnapshot snapshot,
        CollectionWriteOffReceiptBalance? receipt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (receipt is null)
            return (422, CollectionWriteOffPostingErrors.ReceiptNotPosted);
        if (receipt.CustomerId != snapshot.CustomerId
            || !string.Equals(receipt.BaseCurrency, snapshot.BaseCurrency, StringComparison.Ordinal)
            || receipt.BusinessDate > snapshot.BusinessDate)
            return (422, CollectionWriteOffPostingErrors.ReceiptMismatch);
        return snapshot.TotalAmount > receipt.UnappliedAmount
            ? (422, CollectionWriteOffPostingErrors.ReceiptInsufficient)
            : null;
    }

    /// <summary>
    /// Every unusable line with its reason, in document order (BR-FI-040: no partial success, no silent skip). A line is
    /// unusable when its receivable is missing, not an invoice receivable of the settlement's customer and currency,
    /// posted after the settlement date, or open for less than the line amount (credited or already settled).
    /// </summary>
    public static IReadOnlyList<CollectionWriteOffLineError> CheckLines(FinanceCollectionWriteOffSnapshot snapshot,
        IReadOnlyDictionary<long, CollectionWriteOffReceivable> receivables)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(receivables);
        var errors = new List<CollectionWriteOffLineError>();
        for (var index = 0; index < snapshot.Lines.Count; index++)
        {
            var line = snapshot.Lines[index];
            var reason = !receivables.TryGetValue(line.ReceivableId, out var receivable)
                ? CollectionWriteOffLineReasons.ReceivableNotFound
                : receivable.CustomerId != snapshot.CustomerId
                  || !string.Equals(receivable.SourceType, InvoiceReceivableType, StringComparison.Ordinal)
                  || !string.Equals(receivable.BaseCurrency, snapshot.BaseCurrency, StringComparison.Ordinal)
                    ? CollectionWriteOffLineReasons.ReceivableMismatch
                    : receivable.PostingDate > snapshot.BusinessDate
                        ? CollectionWriteOffLineReasons.ReceivableAfterSettlement
                        : line.Amount > receivable.OpenAmount
                            ? CollectionWriteOffLineReasons.ExceedsOpen
                            : null;
            if (reason is not null)
                errors.Add(new(index + 1, line.ReceivableId, reason));
        }
        return errors;
    }
}
