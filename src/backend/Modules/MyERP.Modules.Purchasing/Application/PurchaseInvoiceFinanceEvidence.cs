using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One saved RESOLVE decision row (<c>pur_purchase_invoice_resolution</c>).</summary>
public sealed record PurchaseInvoiceSavedResolution(int InvoiceVersion, int LineNo, string Action, string Reason,
    decimal NetDifference, decimal TaxDifference, decimal GrossDifference);

/// <summary>
/// WU-PUR-008b3 (ADR-207): rebuilds the accepted-difference evidence of a saved invoice for the 008a10 fingerprint.
/// RESOLVE stores only the NET/TAX/GROSS difference sums per line, so the full per-kind differences are recomputed from
/// the current saved invoice and order prices and must agree with the stored sums.
/// </summary>
public static class PurchaseInvoiceFinanceEvidence
{
    /// <summary>
    /// No current differences → no evidence (older RESOLVE rounds are history). Otherwise the evidence is the latest
    /// RESOLVE round (highest invoice version); each of its rows carries the current differences of its line. Whether
    /// the round is complete and all-Accept is decided by <see cref="PurchaseInvoicePostPreflight"/> in the fingerprint.
    /// </summary>
    public static IReadOnlyList<PurchaseInvoiceResolvedLine> Accepted(PurchaseInvoiceAmountDecision amount,
        IReadOnlyList<PurchaseInvoiceSavedResolution> saved)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(saved);
        var disputed = amount.Lines.Where(x => x.Differences.Count > 0).ToDictionary(x => x.LineNo);
        if (disputed.Count == 0)
            return [];
        if (saved.Count == 0 || saved.Any(x => x is null))
            throw InvalidEvidence();

        var latest = saved.Max(x => x.InvoiceVersion);
        var result = new List<PurchaseInvoiceResolvedLine>();
        foreach (var row in saved.Where(x => x.InvoiceVersion == latest).OrderBy(x => x.LineNo))
        {
            if (!disputed.TryGetValue(row.LineNo, out var line)
                || row.NetDifference != Sum(line, "NET") || row.TaxDifference != Sum(line, "TAX")
                || row.GrossDifference != Sum(line, "GROSS"))
                throw InvalidEvidence();
            result.Add(new(row.LineNo, row.Action, row.Reason, line.Differences.ToArray()));
        }
        return result;
    }

    private static decimal Sum(PurchaseInvoiceAmountLineMatch line, string kind) =>
        line.Differences.Where(x => x.Kind == kind).Sum(x => x.Difference);

    private static BusinessRuleException InvalidEvidence() =>
        new("PUR.INVOICE.INVALID_POSTING_DIFFERENCE",
            "Saved difference decisions do not match current invoice and order amounts.");
}
