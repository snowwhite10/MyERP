using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One saved invoice line with its receipt reference and saved price/tax basis.</summary>
public sealed record PurchaseInvoiceMatchLine(long ReceiptLineId, PurchaseInvoiceAmountLine Amount);

public sealed record PurchaseInvoiceMatchDecision(string Status,
    IReadOnlyList<PurchaseInvoiceShortage> Shortages, PurchaseInvoiceAmountDecision? Amount);

/// <summary>BR-PUR-037e: pure whole-invoice MATCH decision over transaction-owned snapshots.</summary>
public static class PurchaseInvoiceMatch
{
    public static PurchaseInvoiceMatchDecision Check(string status,
        IReadOnlyList<PurchaseInvoiceMatchLine> lines,
        PurchaseInvoiceIdentity invoice, PurchaseInvoiceOrderIdentity order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        IReadOnlyList<PurchaseInvoiceReceiptSource> quantities)
    {
        if (status is not ("Draft" or "Rejected"))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_MATCH_STATUS",
                "Only draft or rejected invoices can be matched.");
        ArgumentNullException.ThrowIfNull(lines);
        var requested = lines.Select(line => line is null
            ? new PurchaseInvoiceReceiptRequest(0, 0)
            : new PurchaseInvoiceReceiptRequest(line.ReceiptLineId, line.Amount?.Quantity ?? 0)).ToArray();

        // Client/saved line shape comes first; never let a broken source snapshot mask it.
        PurchaseInvoiceAvailability.AggregateRequested(requested);
        PurchaseInvoiceSourceIdentity.Check(requested, invoice, order, receipts);
        var availability = PurchaseInvoiceAvailability.CheckRequested(requested, quantities);
        if (!availability.CanMatch)
            return new("Shortage", availability.Shortages, null);

        var amount = PurchaseInvoiceAmountMatch.Check(lines.Select(line => line.Amount).ToArray());
        return new(amount.CanMatch ? "Matched" : "Disputed", [], amount);
    }
}
