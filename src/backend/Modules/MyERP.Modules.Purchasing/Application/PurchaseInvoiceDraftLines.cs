using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceDraftLine(int LineNumber, long ReceiptLineId, decimal Quantity);

/// <summary>BR-PUR-036c: validates draft line shape before any source lookup or write.</summary>
public static class PurchaseInvoiceDraftLines
{
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public static void Validate(IReadOnlyList<PurchaseInvoiceDraftLine>? lines)
    {
        if (lines is null || lines.Count is < 1 or > 1000)
            throw InvalidLines();

        var lineNumbers = new HashSet<int>();
        foreach (var line in lines)
        {
            if (line is null || line.LineNumber <= 0 || !lineNumbers.Add(line.LineNumber) ||
                line.ReceiptLineId <= 0 || line.Quantity <= 0 ||
                line.Quantity >= QuantityLimit || decimal.Round(line.Quantity, 6) != line.Quantity)
                throw InvalidLines();
        }
    }

    private static BusinessRuleException InvalidLines() =>
        new("PUR.INVOICE.INVALID_LINES", "Invoice lines must contain 1 to 1000 distinct positive line numbers and valid receipt quantities.");
}
