using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Versioned canonical hash of the saved purchase invoice POST source snapshot.</summary>
public static class PurchaseInvoiceSnapshotFingerprint
{
    private const string Domain = "PUR-INVOICE-POST-v1:";

    public static string Create(long companyId, long invoiceId, long supplierId,
        string currency, DateOnly businessDate,
        IReadOnlyList<PurchaseInvoiceMatchLine> lines,
        IReadOnlyList<PurchaseInvoiceResolvedLine> acceptedDifferences)
    {
        if (companyId <= 0 || invoiceId <= 0 || supplierId <= 0 || currency is null ||
            currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw InvalidSnapshot();
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(acceptedDifferences);

        var requests = lines.Select(x => x is null
            ? new PurchaseInvoiceReceiptRequest(0, 0)
            : new PurchaseInvoiceReceiptRequest(x.ReceiptLineId, x.Amount?.Quantity ?? 0)).ToArray();
        PurchaseInvoiceAvailability.AggregateRequested(requests);
        var amount = PurchaseInvoiceAmountMatch.Check(lines.Select(x => x.Amount).ToArray());
        PurchaseInvoicePostPreflight.CheckAcceptedDifferences(amount, acceptedDifferences);

        var canonical = new StringBuilder(Domain);
        Add(canonical, companyId);
        Add(canonical, invoiceId);
        Add(canonical, supplierId);
        Add(canonical, currency);
        Add(canonical, businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(canonical, lines.Count);
        foreach (var line in lines.OrderBy(x => x.Amount.LineNo))
        {
            var saved = line.Amount;
            Add(canonical, saved.LineNo);
            Add(canonical, line.ReceiptLineId);
            Add(canonical, Number(saved.Quantity, 6));
            Add(canonical, Number(saved.OrderUnitPrice, 6));
            Add(canonical, Number(saved.OrderTaxRatePercent, 4));
            Add(canonical, Number(saved.InvoiceUnitPrice, 6));
            Add(canonical, Number(saved.InvoiceTaxRatePercent, 4));
            Add(canonical, Number(saved.InvoiceNet, 4));
            Add(canonical, Number(saved.InvoiceTax, 4));
            Add(canonical, Number(saved.InvoiceGross, 4));
        }
        Add(canonical, acceptedDifferences.Count);
        foreach (var decision in acceptedDifferences.OrderBy(x => x.LineNo))
        {
            Add(canonical, decision.LineNo);
            Add(canonical, decision.Action);
            Add(canonical, decision.Reason);
            Add(canonical, decision.Differences.Count);
            foreach (var difference in decision.Differences.OrderBy(x => x.Kind, StringComparer.Ordinal))
            {
                Add(canonical, difference.Kind);
                Add(canonical, Number(difference.Baseline, 6));
                Add(canonical, Number(difference.Invoice, 6));
                Add(canonical, Number(difference.Difference, 6));
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Domain + Convert.ToHexStringLower(hash);
    }

    private static string Number(decimal value, int scale) =>
        value.ToString(scale == 6 ? "0.000000" : "0.0000", CultureInfo.InvariantCulture);

    private static void Add(StringBuilder builder, long value) =>
        Add(builder, value.ToString(CultureInfo.InvariantCulture));

    // Length prefixes keep text and numeric fields unambiguous, including reasons with separators.
    private static void Add(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);

    private static BusinessRuleException InvalidSnapshot() =>
        new("PUR.INVOICE.INVALID_POSTING_SNAPSHOT", "Saved invoice identity is invalid.");
}
