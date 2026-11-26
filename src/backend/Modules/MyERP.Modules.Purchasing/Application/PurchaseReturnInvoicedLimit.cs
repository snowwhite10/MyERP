using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Committed quantities of one Posted receipt line plus this return's own pending reservation.</summary>
public sealed record PurchaseReturnSourceQuantities(decimal PostedReceived, decimal PostedReturned,
    decimal PendingReturnReserved, decimal PostedInvoiced, decimal MatchedReserved,
    decimal CurrentReturnReserved);

public sealed record PurchaseReturnReceiptRequest(long ReceiptLineId, decimal Quantity);
public sealed record PurchaseReturnReceiptSource(long ReceiptLineId, PurchaseReturnSourceQuantities Quantities);

/// <summary>
/// A receipt line whose request exceeds the uninvoiced returnable quantity; <c>InvoicedOrMatched</c> is the Posted
/// invoice plus Matched/Disputed reservation, i.e. the reason for refusal.
/// </summary>
public sealed record PurchaseReturnShortage(long ReceiptLineId, decimal Requested, decimal Available,
    decimal InvoicedOrMatched);

public sealed record PurchaseReturnLimitDecision(bool CanReturn, IReadOnlyList<PurchaseReturnShortage> Shortages);

/// <summary>
/// BR-PUR-041 / BR-PUR-044 (WU-PUR-008b5, ADR-209): PUR's public "returnable uninvoiced" rule for 007c.
/// <c>available = posted_received - posted_returned - other pending returns - posted_invoiced - matched_reserved</c>.
/// Quantities already invoiced (Posted) or reserved by a Matched/Disputed invoice can only leave through an FI
/// credit note, never through a purchase return. Pure: the caller reads sources under the receipt-line lock.
/// </summary>
public static class PurchaseReturnInvoicedLimit
{
    public const string ExceedsUninvoiced = "PUR.RETURN.INVOICED_QUANTITY_EXCEEDED";
    public const string InvalidSourceCode = "PUR.RETURN.INVALID_SOURCE_QUANTITY";
    public const string InvalidRequestCode = "PUR.RETURN.INVALID_REQUEST_QUANTITY";
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public static decimal Calculate(PurchaseReturnSourceQuantities source)
    {
        ArgumentNullException.ThrowIfNull(source);
        decimal[] quantities = [source.PostedReceived, source.PostedReturned, source.PendingReturnReserved,
            source.PostedInvoiced, source.MatchedReserved, source.CurrentReturnReserved];
        if (quantities.Any(value => !ValidQuantity(value)) || source.PostedReceived == 0 ||
            source.CurrentReturnReserved > source.PendingReturnReserved ||
            // Same invariant as ck_pur_receipt_line_quantities: every consumer must be backed by the receipt.
            source.PostedReturned + source.PendingReturnReserved + source.PostedInvoiced + source.MatchedReserved >
            source.PostedReceived)
            throw InvalidSource();

        // Exclude only this return's own reservation so re-checking a submitted return does not count it twice.
        var available = source.PostedReceived - source.PostedReturned -
            (source.PendingReturnReserved - source.CurrentReturnReserved) - source.PostedInvoiced -
            source.MatchedReserved;
        return available;
    }

    /// <summary>Whole-return decision: duplicate lines aggregate per receipt line; any shortage refuses all.</summary>
    public static PurchaseReturnLimitDecision Check(IReadOnlyList<PurchaseReturnReceiptRequest> lines,
        IReadOnlyList<PurchaseReturnReceiptSource> sources)
    {
        var requested = Aggregate(lines);
        ArgumentNullException.ThrowIfNull(sources);
        var byId = new Dictionary<long, PurchaseReturnSourceQuantities>();
        foreach (var source in sources)
        {
            if (source is null || source.ReceiptLineId <= 0 || source.Quantities is null ||
                !byId.TryAdd(source.ReceiptLineId, source.Quantities))
                throw InvalidSource();
        }
        if (byId.Count != requested.Count) throw InvalidSource();

        var shortages = new List<PurchaseReturnShortage>();
        foreach (var line in requested)
        {
            if (!byId.TryGetValue(line.ReceiptLineId, out var quantities)) throw InvalidSource();
            var available = Calculate(quantities);
            if (line.Quantity > available)
                shortages.Add(new(line.ReceiptLineId, line.Quantity, available,
                    quantities.PostedInvoiced + quantities.MatchedReserved));
        }
        return new(shortages.Count == 0, shortages.ToArray());
    }

    /// <summary>Throws 422 <see cref="ExceedsUninvoiced"/> naming every short line, its available and invoiced quantity.</summary>
    public static void EnsureWithinLimit(IReadOnlyList<PurchaseReturnReceiptRequest> lines,
        IReadOnlyList<PurchaseReturnReceiptSource> sources)
    {
        var decision = Check(lines, sources);
        if (decision.CanReturn) return;
        var detail = string.Join("; ", decision.Shortages.Select(x =>
            $"receipt line {x.ReceiptLineId}: requested {x.Requested}, returnable {x.Available}, " +
            $"invoiced or matched {x.InvoicedOrMatched}"));
        throw new BusinessRuleException(ExceedsUninvoiced,
            $"Return exceeds the uninvoiced quantity; use an FI credit note for invoiced quantity ({detail}).");
    }

    public static IReadOnlyList<PurchaseReturnReceiptRequest> Aggregate(
        IReadOnlyList<PurchaseReturnReceiptRequest> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) throw InvalidRequest();
        var totals = new Dictionary<long, decimal>();
        foreach (var line in lines)
        {
            if (line is null || line.ReceiptLineId <= 0 || !ValidQuantity(line.Quantity) || line.Quantity == 0)
                throw InvalidRequest();
            totals.TryGetValue(line.ReceiptLineId, out var previous);
            if (previous >= QuantityLimit - line.Quantity) throw InvalidRequest();
            totals[line.ReceiptLineId] = previous + line.Quantity;
        }
        return totals.OrderBy(pair => pair.Key)
            .Select(pair => new PurchaseReturnReceiptRequest(pair.Key, pair.Value)).ToArray();
    }

    private static bool ValidQuantity(decimal value) =>
        value >= 0 && value < QuantityLimit && decimal.Round(value, 6) == value;

    private static BusinessRuleException InvalidSource() =>
        new(InvalidSourceCode, "Receipt quantity snapshot is inconsistent.");

    private static BusinessRuleException InvalidRequest() =>
        new(InvalidRequestCode, "Return quantity must be positive with at most six decimals.");
}
