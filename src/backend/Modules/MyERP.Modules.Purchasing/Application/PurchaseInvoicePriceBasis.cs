using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// Order price basis of an invoice's receipt lines (shared by MATCH, RESOLVE, POST and the FI source port;
/// optimization-log 008b3). Exactly one positive, distinct price per requested receipt line, or the saved sources are
/// no longer usable.
/// </summary>
public static class PurchaseInvoicePriceBasis
{
    public static IReadOnlyDictionary<long, PurchaseInvoiceOrderLinePrice> Require(
        IReadOnlyCollection<long> receiptLineIds, IReadOnlyList<PurchaseInvoiceOrderLinePrice> prices)
    {
        ArgumentNullException.ThrowIfNull(receiptLineIds);
        if (prices is null || prices.Count != receiptLineIds.Count || prices.Any(x => x is null || x.ReceiptLineId <= 0))
            throw Incomplete();
        var byReceipt = new Dictionary<long, PurchaseInvoiceOrderLinePrice>(prices.Count);
        foreach (var price in prices)
        {
            if (!byReceipt.TryAdd(price.ReceiptLineId, price))
                throw Incomplete();
        }
        if (receiptLineIds.Any(id => !byReceipt.ContainsKey(id)))
            throw Incomplete();
        return byReceipt;
    }

    /// <summary>Saved invoice lines paired with their order price basis (input of the amount match).</summary>
    public static PurchaseInvoiceMatchLine[] MatchLines(IEnumerable<PurchaseInvoiceLine> lines,
        IReadOnlyDictionary<long, PurchaseInvoiceOrderLinePrice> prices) =>
        lines.Select(line =>
        {
            if (!prices.TryGetValue(line.ReceiptLineId, out var price))
                throw Incomplete();
            return new PurchaseInvoiceMatchLine(line.ReceiptLineId, new PurchaseInvoiceAmountLine(line.LineNo,
                line.Quantity, price.UnitPrice, price.TaxRatePercent, line.UnitPrice, line.TaxRatePercent,
                line.Net, line.Tax, line.Gross));
        }).ToArray();

    private static BusinessRuleException Incomplete() =>
        new("PUR.INVOICE.INVALID_SOURCE_IDENTITY", "Saved receipt prices are incomplete or duplicated.");
}
