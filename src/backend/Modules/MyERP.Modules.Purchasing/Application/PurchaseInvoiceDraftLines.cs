using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceDraftLine(int LineNumber, long ReceiptLineId, decimal Quantity,
    decimal UnitPrice, decimal TaxRatePercent, decimal Net, decimal Tax, decimal Gross);

/// <summary>BR-PUR-036c: validates draft line shape before any source lookup or write.</summary>
public static class PurchaseInvoiceDraftLines
{
    private const decimal QuantityLimit = 10_000_000_000_000m;
    private const decimal AmountLimit = 1_000_000_000_000_000m;

    public static void Validate(IReadOnlyList<PurchaseInvoiceDraftLine>? lines)
    {
        if (lines is null || lines.Count is < 1 or > 1000)
            throw InvalidLines();

        var lineNumbers = new HashSet<int>();
        decimal totalNet = 0, totalTax = 0, totalGross = 0;
        try
        {
            foreach (var line in lines)
            {
                if (line is null || line.LineNumber <= 0 || !lineNumbers.Add(line.LineNumber) ||
                    line.ReceiptLineId <= 0 || line.Quantity <= 0 ||
                    line.Quantity >= QuantityLimit || decimal.Round(line.Quantity, 6) != line.Quantity ||
                    !ValidPrice(line.UnitPrice) || !ValidTaxRate(line.TaxRatePercent) ||
                    !ValidAmount(line.Net) || !ValidAmount(line.Tax) || !ValidAmount(line.Gross))
                    throw InvalidLines();
                totalNet = checked(totalNet + line.Net);
                totalTax = checked(totalTax + line.Tax);
                totalGross = checked(totalGross + line.Gross);
                if (!ValidAmount(totalNet) || !ValidAmount(totalTax) || !ValidAmount(totalGross))
                    throw InvalidLines();
            }
        }
        catch (OverflowException)
        {
                throw InvalidLines();
        }
    }

    private static bool ValidPrice(decimal value) => value >= 0 && value < QuantityLimit &&
        decimal.Round(value, 6) == value;

    private static bool ValidTaxRate(decimal value) => value >= 0 && value <= 100 &&
        decimal.Round(value, 4) == value;

    private static bool ValidAmount(decimal value) => value >= 0 && value < AmountLimit &&
        decimal.Round(value, 4) == value;

    private static BusinessRuleException InvalidLines() =>
        new("PUR.INVOICE.INVALID_LINES", "Invoice lines must contain 1 to 1000 distinct positive line numbers and valid receipt quantities.");
}
