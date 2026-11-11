using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Committed quantities for one Posted receipt line, read under the caller's source lock.</summary>
public sealed record PurchaseInvoiceSourceQuantities(decimal PostedReceived,
    decimal PostedReturned, decimal PendingReturnReserved, decimal PostedInvoiced,
    decimal MatchedReserved, decimal CurrentInvoiceReserved);

/// <summary>Pure BR-PUR-037 quantity rule; callers aggregate duplicate invoice lines before checking.</summary>
public static class PurchaseInvoiceAvailability
{
    private const decimal QuantityLimit = 10_000_000_000_000m;

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
            throw new BusinessRuleException("PUR.INVOICE.INVALID_REQUEST_QUANTITY",
                "Invoice source quantity must be positive with at most six decimals.");
        return requested <= Calculate(source);
    }

    private static bool ValidQuantity(decimal value) =>
        value >= 0 && value < QuantityLimit && decimal.Round(value, 6) == value;

    private static BusinessRuleException InvalidSource() =>
        new("PUR.INVOICE.INVALID_SOURCE_QUANTITY", "Receipt quantity snapshot is inconsistent.");
}
