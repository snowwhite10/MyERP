using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Saved invoice line and its authoritative approved order price and tax snapshot.</summary>
public sealed record PurchaseInvoiceAmountLine(int LineNo, decimal Quantity,
    decimal OrderUnitPrice, decimal OrderTaxRatePercent,
    decimal InvoiceUnitPrice, decimal InvoiceTaxRatePercent,
    decimal InvoiceNet, decimal InvoiceTax, decimal InvoiceGross);

public sealed record PurchaseInvoiceAmountDifference(string Kind, decimal Baseline,
    decimal Invoice, decimal Difference);
public sealed record PurchaseInvoiceAmountLineMatch(int LineNo,
    decimal BaselineNet, decimal BaselineTax, decimal BaselineGross,
    IReadOnlyList<PurchaseInvoiceAmountDifference> Differences);
public sealed record PurchaseInvoiceAmountDecision(bool CanMatch,
    IReadOnlyList<PurchaseInvoiceAmountLineMatch> Lines,
    decimal BaselineNet, decimal BaselineTax, decimal BaselineGross,
    decimal InvoiceNet, decimal InvoiceTax, decimal InvoiceGross);

/// <summary>BR-PUR-038: compare saved order basis with four-place original-currency invoice amounts.</summary>
public static class PurchaseInvoiceAmountMatch
{
    private const decimal AmountLimit = 1_000_000_000_000_000m;
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public static PurchaseInvoiceAmountDecision Check(IReadOnlyList<PurchaseInvoiceAmountLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count is < 1 or > 1000) throw InvalidLine();
        var seen = new HashSet<int>();
        var results = new List<PurchaseInvoiceAmountLineMatch>(lines.Count);
        decimal baseNet = 0, baseTax = 0, baseGross = 0;
        decimal invoiceNet = 0, invoiceTax = 0, invoiceGross = 0;
        try
        {
            foreach (var line in lines.OrderBy(x => x?.LineNo))
            {
                if (line is null || line.LineNo <= 0 || !seen.Add(line.LineNo) ||
                    line.Quantity <= 0 || line.Quantity >= QuantityLimit ||
                    decimal.Round(line.Quantity, 6) != line.Quantity ||
                    !ValidPrice(line.OrderUnitPrice) || !ValidPrice(line.InvoiceUnitPrice) ||
                    !ValidTaxRate(line.OrderTaxRatePercent) ||
                    !ValidTaxRate(line.InvoiceTaxRatePercent) ||
                    !ValidAmount(line.InvoiceNet) || !ValidAmount(line.InvoiceTax) ||
                    !ValidAmount(line.InvoiceGross))
                    throw InvalidLine();

                var net = Round4(checked(line.Quantity * line.OrderUnitPrice));
                var tax = Round4(checked(net * line.OrderTaxRatePercent / 100m));
                var gross = checked(net + tax);
                if (!ValidAmount(net) || !ValidAmount(tax) || !ValidAmount(gross))
                    throw InvalidLine();

                var differences = new List<PurchaseInvoiceAmountDifference>();
                Add(differences, "UNIT_PRICE", line.OrderUnitPrice, line.InvoiceUnitPrice);
                Add(differences, "TAX_RATE", line.OrderTaxRatePercent, line.InvoiceTaxRatePercent);
                Add(differences, "NET", net, line.InvoiceNet);
                Add(differences, "TAX", tax, line.InvoiceTax);
                Add(differences, "GROSS", gross, line.InvoiceGross);
                results.Add(new(line.LineNo, net, tax, gross, differences.ToArray()));

                baseNet = checked(baseNet + net);
                baseTax = checked(baseTax + tax);
                baseGross = checked(baseGross + gross);
                invoiceNet = checked(invoiceNet + line.InvoiceNet);
                invoiceTax = checked(invoiceTax + line.InvoiceTax);
                invoiceGross = checked(invoiceGross + line.InvoiceGross);
                if (!ValidAmount(baseNet) || !ValidAmount(baseTax) || !ValidAmount(baseGross) ||
                    !ValidAmount(invoiceNet) || !ValidAmount(invoiceTax) || !ValidAmount(invoiceGross))
                    throw InvalidLine();
            }
        }
        catch (OverflowException)
        {
            throw InvalidLine();
        }
        return new(results.All(x => x.Differences.Count == 0), results.ToArray(),
            baseNet, baseTax, baseGross, invoiceNet, invoiceTax, invoiceGross);
    }

    private static decimal Round4(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static bool ValidPrice(decimal value) =>
        value >= 0 && value < QuantityLimit && decimal.Round(value, 6) == value;

    private static bool ValidTaxRate(decimal value) =>
        value >= 0 && value <= 100 && decimal.Round(value, 4) == value;

    private static bool ValidAmount(decimal value) =>
        value >= 0 && value < AmountLimit && decimal.Round(value, 4) == value;

    private static void Add(List<PurchaseInvoiceAmountDifference> differences,
        string kind, decimal baseline, decimal invoice)
    {
        if (baseline != invoice)
            differences.Add(new(kind, baseline, invoice, invoice - baseline));
    }

    private static BusinessRuleException InvalidLine() =>
        new("PUR.INVOICE.INVALID_AMOUNT_SOURCE", "Invoice or saved order amount is invalid.");
}
