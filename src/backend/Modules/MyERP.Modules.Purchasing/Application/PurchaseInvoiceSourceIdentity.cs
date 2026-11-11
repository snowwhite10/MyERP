using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Saved invoice identity; supplied by the transaction service, never by the client.</summary>
public sealed record PurchaseInvoiceIdentity(long CompanyId, long SupplierId,
    long PurchaseOrderId, string Currency);

/// <summary>Locked purchase order header.</summary>
public sealed record PurchaseInvoiceOrderIdentity(long CompanyId, long SupplierId,
    long PurchaseOrderId, string Currency, string Status, bool IsDeleted);

/// <summary>Locked receipt row joined to its authoritative purchase order line.</summary>
public sealed record PurchaseInvoiceReceiptIdentity(long ReceiptLineId, long CompanyId,
    long SupplierId, long PurchaseOrderId, long OrderLinePurchaseOrderId,
    long MaterialId, long OrderMaterialId, string Currency, string Status, bool IsDeleted)
{
    public long OrderLineId { get; init; }
}

/// <summary>Pure source identity guard for BR-PUR-036/038/039.</summary>
public static class PurchaseInvoiceSourceIdentity
{
    public static void Check(IReadOnlyList<PurchaseInvoiceReceiptRequest> lines,
        PurchaseInvoiceIdentity invoice, PurchaseInvoiceOrderIdentity order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts)
    {
        var requested = PurchaseInvoiceAvailability.AggregateRequested(lines);
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.CompanyId <= 0 || invoice.SupplierId <= 0 ||
            invoice.PurchaseOrderId <= 0 || string.IsNullOrWhiteSpace(invoice.Currency) ||
            order is null || order.IsDeleted || order.Status != "Approved" ||
            order.CompanyId != invoice.CompanyId || order.SupplierId != invoice.SupplierId ||
            order.PurchaseOrderId != invoice.PurchaseOrderId ||
            !string.Equals(order.Currency, invoice.Currency, StringComparison.Ordinal))
            throw InvalidSource();

        ArgumentNullException.ThrowIfNull(receipts);
        if (receipts.Count != requested.Count) throw InvalidSource();
        var requestedIds = requested.Select(line => line.ReceiptLineId).ToHashSet();
        var seen = new HashSet<long>();
        foreach (var receipt in receipts)
        {
            if (receipt is null || receipt.ReceiptLineId <= 0 ||
                !requestedIds.Contains(receipt.ReceiptLineId) || !seen.Add(receipt.ReceiptLineId) ||
                receipt.IsDeleted || receipt.Status != "Posted" ||
                receipt.CompanyId != invoice.CompanyId ||
                receipt.SupplierId != invoice.SupplierId ||
                receipt.PurchaseOrderId != invoice.PurchaseOrderId ||
                receipt.OrderLinePurchaseOrderId != invoice.PurchaseOrderId ||
                receipt.MaterialId <= 0 || receipt.MaterialId != receipt.OrderMaterialId ||
                !string.Equals(receipt.Currency, invoice.Currency, StringComparison.Ordinal))
                throw InvalidSource();
        }
    }

    private static BusinessRuleException InvalidSource() =>
        new("PUR.INVOICE.INVALID_SOURCE_IDENTITY",
            "Purchase order or receipt source identity is inconsistent.");
}
