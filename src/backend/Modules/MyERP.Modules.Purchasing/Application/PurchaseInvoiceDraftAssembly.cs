using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Validated draft data assembled against order and receipt identities read by the caller.</summary>
public sealed record PurchaseInvoiceDraftSnapshot(
    PurchaseInvoiceIdentity Identity,
    DateOnly BusinessDate,
    PurchaseInvoiceNumberValue Number,
    IReadOnlyList<PurchaseInvoiceDraftLine> Lines,
    PurchaseInvoiceDraftTotals Totals);

/// <summary>BR-PUR-036h: a pure draft save boundary; the caller supplies locked authority rows.</summary>
public static class PurchaseInvoiceDraftAssembly
{
    public static PurchaseInvoiceDraftSnapshot Create(long companyId, PurchaseInvoiceDraftInput? input,
        PurchaseInvoiceOrderIdentity order, IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts)
    {
        var validated = PurchaseInvoiceDraftInput.Validate(input);
        if (companyId <= 0)
            throw InvalidSource();

        var identity = new PurchaseInvoiceIdentity(companyId, validated.SupplierId,
            validated.PurchaseOrderId, validated.Currency);
        var requested = validated.Lines.Select(line =>
            new PurchaseInvoiceReceiptRequest(line.ReceiptLineId, line.Quantity)).ToArray();
        PurchaseInvoiceSourceIdentity.Check(requested, identity, order, receipts);

        return new(identity, validated.BusinessDate, validated.Number,
            validated.Lines, validated.Totals);
    }

    private static BusinessRuleException InvalidSource() =>
        new("PUR.INVOICE.INVALID_SOURCE_IDENTITY", "Purchase order or receipt source identity is inconsistent.");
}
