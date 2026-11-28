using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

public sealed class PurchaseOrderSource : BusinessEntity, ISoftDeletable
{
    // Nullable while the older invoice-source fixtures have no commercial document number.
    public string? DocumentNo { get; set; }
    public long? RfqId { get; set; }
    public DateOnly? BusinessDate { get; set; }
    public long? OwnerId { get; set; }
    public long? BranchId { get; set; }
    public long? DepartmentId { get; set; }
    public decimal? BaseNet { get; set; }
    public decimal? BaseTax { get; set; }
    public decimal? BaseGross { get; set; }
    public string? Remark { get; set; }
    public bool RequisitionConsumptionActive { get; set; }
    /// <summary>CANCEL reason (BR-PUR-025); only a Cancelled order may carry one (ck_pur_order_cancel_reason).</summary>
    public string? CancelReason { get; set; }
    public long SupplierId { get; set; }
    public required string Currency { get; set; }
    /// <summary>Company base currency captured with the order rate (BR-PUR-043b); null only for pre-006a source rows.</summary>
    public string? BaseCurrencyCode { get; set; }
    /// <summary>1 order currency = <c>ExchangeRate</c> base currency; immutable once approved (BR-PUR-043).</summary>
    public decimal? ExchangeRate { get; set; }
    public required string Status { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class PurchaseOrderSourceLine : BusinessEntity
{
    public long? RequisitionLineId { get; set; }
    public long? TaxRateId { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? BaseNet { get; set; }
    public decimal? BaseTax { get; set; }
    public decimal? BaseGross { get; set; }
    public decimal? OriginalNet { get; set; }
    public decimal? OriginalTax { get; set; }
    public decimal? OriginalGross { get; set; }
    public long PurchaseOrderId { get; set; }
    public int LineNo { get; set; }
    public long MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal PostedReceived { get; set; }
    public decimal PendingReceiptReserved { get; set; }
}

/// <summary>Permanent, company-scoped evidence that an award was converted once.</summary>
public sealed class PurchaseOrderAwardSource : BusinessEntity
{
    public long PurchaseOrderLineId { get; set; }
    public long AwardId { get; set; }
}

public sealed class PurchaseReceiptSource : BusinessEntity, ISoftDeletable
{
    public string? DocumentNo { get; set; }
    public DateOnly? BusinessDate { get; set; }
    public long PurchaseOrderId { get; set; }
    public long SupplierId { get; set; }
    public required string Currency { get; set; }
    public required string Status { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class PurchaseReceiptSourceLine : BusinessEntity
{
    public decimal? Quantity { get; set; }
    public long? WarehouseId { get; set; }
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
