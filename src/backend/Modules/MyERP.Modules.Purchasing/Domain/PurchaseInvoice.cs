using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

public enum PurchaseInvoiceStatus { Draft, Matched, Disputed, Rejected, Posted }

/// <summary>Invoice header; draft persistence is introduced before the transactional writer.</summary>
public sealed class PurchaseInvoice : BusinessEntity, ISoftDeletable
{
    public long SupplierId { get; set; }
    public long PurchaseOrderId { get; set; }
    public required string Currency { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string SupplierInvoiceNumber { get; set; }
    public required string NumberKey { get; set; }
    public PurchaseInvoiceStatus Status { get; set; } = PurchaseInvoiceStatus.Draft;
    public decimal TotalNet { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalGross { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<PurchaseInvoiceLine> Lines { get; } = [];
}

public sealed class PurchaseInvoiceLine : BusinessEntity
{
    public long InvoiceId { get; set; }
    public int LineNo { get; set; }
    public long ReceiptLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal Net { get; set; }
    public decimal Tax { get; set; }
    public decimal Gross { get; set; }
}
