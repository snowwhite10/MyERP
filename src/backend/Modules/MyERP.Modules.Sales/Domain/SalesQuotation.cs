using MyERP.Modules.Sales.Application;
using MyERP.Shared.Domain;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Domain;

/// <summary>Saved quotation snapshot; commands and approval are implemented separately.</summary>
public sealed class SalesQuotation : BusinessEntity, ISoftDeletable, IDataScopedEntity
{
    public long? BranchId { get; set; }
    public long? DepartmentId { get; set; }
    public long OwnerUserId { get; set; }
    public required string DocumentNo { get; set; }
    public long CustomerId { get; set; }
    public required string CustomerCode { get; set; }
    public required string CustomerName { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public required string CurrencyCode { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; }
    public SalesQuotationStatus Status { get; set; } = SalesQuotationStatus.Draft;
    public decimal OriginalNet { get; set; }
    public decimal OriginalTax { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal BaseNet { get; set; }
    public decimal BaseTax { get; set; }
    public decimal BaseGross { get; set; }
    public string? Remark { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<SalesQuotationLine> Lines { get; } = [];
}

public sealed class SalesQuotationLine : BusinessEntity
{
    public long QuotationId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public required string ItemCode { get; set; }
    public required string ItemName { get; set; }
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
