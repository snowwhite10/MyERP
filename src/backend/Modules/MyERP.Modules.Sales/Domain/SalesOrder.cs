using MyERP.Shared.Domain;

namespace MyERP.Modules.Sales.Domain;

public enum SalesOrderStatus { Draft, Pending, Rejected, Approved, Cancelled }

/// <summary>Saved order and immutable commercial totals; approval is a later work unit.</summary>
public sealed class SalesOrder : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public long CustomerId { get; set; }
    public required string CustomerCode { get; set; }
    public required string CustomerName { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string CurrencyCode { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; }
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public long? SourceQuotationId { get; set; }
    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
    public string? Remark { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<SalesOrderLine> Lines { get; } = [];
}

public sealed class SalesOrderLine : BusinessEntity
{
    public long OrderId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public required string ItemCode { get; set; }
    public required string ItemName { get; set; }
    public long? SourceQuotationLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public long TaxRateId { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
}
