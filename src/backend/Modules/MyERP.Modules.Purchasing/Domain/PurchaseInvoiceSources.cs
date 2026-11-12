using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

public sealed class PurchaseOrderSource : BusinessEntity, ISoftDeletable
{
    public long SupplierId { get; set; }
    public required string Currency { get; set; }
    public required string Status { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class PurchaseOrderSourceLine : BusinessEntity
{
    public long PurchaseOrderId { get; set; }
    public int LineNo { get; set; }
    public long MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; }
}

public sealed class PurchaseReceiptSource : BusinessEntity, ISoftDeletable
{
    public long PurchaseOrderId { get; set; }
    public long SupplierId { get; set; }
    public required string Currency { get; set; }
    public required string Status { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class PurchaseReceiptSourceLine : BusinessEntity
{
    public long PurchaseReceiptId { get; set; }
    public long PurchaseOrderLineId { get; set; }
    public int LineNo { get; set; }
    public long MaterialId { get; set; }
    public decimal PostedReceived { get; set; }
    public decimal PostedReturned { get; set; }
    public decimal PendingReturnReserved { get; set; }
    public decimal PostedInvoiced { get; set; }
    public decimal MatchedReserved { get; set; }
}
