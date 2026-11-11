using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Committed quantities for one Posted receipt line, read under the caller's source lock.</summary>
public sealed record PurchaseInvoiceSourceQuantities(decimal PostedReceived,
    decimal PostedReturned, decimal PendingReturnReserved, decimal PostedInvoiced,
    decimal MatchedReserved, decimal CurrentInvoiceReserved);

public sealed record PurchaseInvoiceReceiptRequest(long ReceiptLineId, decimal Quantity);

/// <summary>Pure BR-PUR-037 quantity rule; callers aggregate duplicate invoice lines before checking.</summary>
public static class PurchaseInvoiceAvailability
{
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public static IReadOnlyList<PurchaseInvoiceReceiptRequest> AggregateRequested(
        IReadOnlyList<PurchaseInvoiceReceiptRequest> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) throw InvalidRequest();
        var totals = new Dictionary<long, decimal>();
        foreach (var line in lines)
        {
            if (line is null || line.ReceiptLineId <= 0 || !ValidQuantity(line.Quantity) ||
                line.Quantity == 0)
                throw InvalidRequest();
            totals.TryGetValue(line.ReceiptLineId, out var previous);
            if (previous >= QuantityLimit - line.Quantity) throw InvalidRequest();
            totals[line.ReceiptLineId] = previous + line.Quantity;
        }
        return totals.OrderBy(pair => pair.Key)
            .Select(pair => new PurchaseInvoiceReceiptRequest(pair.Key, pair.Value)).ToArray();
    }

    public static decimal Calculate(PurchaseInvoiceSourceQuantities source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var quantities = new[] { source.PostedReceived, source.PostedReturned,
            source.PendingReturnReserved, source.PostedInvoiced, source.MatchedReserved,
            source.CurrentInvoiceReserved };
        if (quantities.Any(value => !ValidQuantity(value)) ||
            source.CurrentInvoiceReserved > source.MatchedReserved)
            throw InvalidSource();

        // Exclude only this invoice's reservation. Other matched invoices still consume the source.
        var available = source.PostedReceived - source.PostedReturned -
            source.PendingReturnReserved - source.PostedInvoiced -
            (source.MatchedReserved - source.CurrentInvoiceReserved);
        if (!ValidQuantity(available)) throw InvalidSource();
        return available;
    }

    public static bool CanMatch(PurchaseInvoiceSourceQuantities source, decimal requested)
    {
        if (!ValidQuantity(requested) || requested == 0)
            throw InvalidRequest();
        return requested <= Calculate(source);
    }

    private static bool ValidQuantity(decimal value) =>
        value >= 0 && value < QuantityLimit && decimal.Round(value, 6) == value;

    private static BusinessRuleException InvalidSource() =>
        new("PUR.INVOICE.INVALID_SOURCE_QUANTITY", "Receipt quantity snapshot is inconsistent.");

    private static BusinessRuleException InvalidRequest() =>
        new("PUR.INVOICE.INVALID_REQUEST_QUANTITY",
            "Invoice source quantity must be positive with at most six decimals.");
}
