using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Validated draft data assembled against order and receipt identities read by the caller.</summary>
public sealed record PurchaseInvoiceDraftSnapshot(
    PurchaseInvoiceIdentity Identity,
    DateOnly BusinessDate,
    PurchaseInvoiceNumberValue Number,
    IReadOnlyList<PurchaseInvoiceDraftLine> Lines,
    PurchaseInvoiceDraftTotals Totals,
    PurchaseInvoiceAmountDecision AmountEvidence);

/// <summary>Saved order line price joined to one locked receipt source.</summary>
public sealed record PurchaseInvoiceOrderLinePrice(long ReceiptLineId, long CompanyId,
    long PurchaseOrderId, long OrderLineId, long MaterialId,
    decimal UnitPrice, decimal TaxRatePercent);

/// <summary>BR-PUR-036h: a pure draft save boundary; the caller supplies locked authority rows.</summary>
public static class PurchaseInvoiceDraftAssembly
{
    public static PurchaseInvoiceDraftSnapshot Create(long companyId, PurchaseInvoiceDraftInput? input,
        PurchaseInvoiceOrderIdentity order, IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        IReadOnlyList<PurchaseInvoiceOrderLinePrice> prices)
    {
        var validated = PurchaseInvoiceDraftInput.Validate(input);
        if (companyId <= 0)
            throw InvalidSource();

        var identity = new PurchaseInvoiceIdentity(companyId, validated.SupplierId,
            validated.PurchaseOrderId, validated.Currency);
        var requested = validated.Lines.Select(line =>
            new PurchaseInvoiceReceiptRequest(line.ReceiptLineId, line.Quantity)).ToArray();
        PurchaseInvoiceSourceIdentity.Check(requested, identity, order, receipts);

        if (prices is null || prices.Count != receipts.Count)
            throw InvalidAmountSource();
        var receiptById = receipts.ToDictionary(x => x.ReceiptLineId);
        var priceById = new Dictionary<long, PurchaseInvoiceOrderLinePrice>();
        foreach (var price in prices)
        {
            if (price is null || !receiptById.TryGetValue(price.ReceiptLineId, out var receipt) ||
                !priceById.TryAdd(price.ReceiptLineId, price) ||
                price.CompanyId != companyId || price.PurchaseOrderId != validated.PurchaseOrderId ||
                receipt.OrderLineId <= 0 || price.OrderLineId != receipt.OrderLineId ||
                price.MaterialId != receipt.MaterialId)
                throw InvalidAmountSource();
        }

        var amountLines = validated.Lines.Select(line =>
        {
            var price = priceById[line.ReceiptLineId];
            return new PurchaseInvoiceAmountLine(line.LineNumber, line.Quantity,
                price.UnitPrice, price.TaxRatePercent, line.UnitPrice,
                line.TaxRatePercent, line.Net, line.Tax, line.Gross);
        }).ToArray();
        var evidence = PurchaseInvoiceAmountMatch.Check(amountLines);

        return new(identity, validated.BusinessDate, validated.Number,
            validated.Lines, validated.Totals, evidence);
    }

    private static BusinessRuleException InvalidSource() =>
        new("PUR.INVOICE.INVALID_SOURCE_IDENTITY", "Purchase order or receipt source identity is inconsistent.");

    private static BusinessRuleException InvalidAmountSource() =>
        new("PUR.INVOICE.INVALID_AMOUNT_SOURCE", "Saved order price source is inconsistent.");
}
