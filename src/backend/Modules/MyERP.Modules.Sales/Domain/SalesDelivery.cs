using MyERP.Shared.Domain;

namespace MyERP.Modules.Sales.Domain;

public enum SalesDeliveryStatus { Draft, Pending, Rejected, Posted }

/// <summary>Saved SD delivery header. Cross-module references remain scalar IDs.</summary>
public sealed class SalesDelivery : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public long OrderId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public SalesDeliveryStatus Status { get; set; } = SalesDeliveryStatus.Draft;
    public string? Remark { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<SalesDeliveryLine> Lines { get; } = [];
}

/// <summary>Saved source order line and base-unit delivery quantity.</summary>
public sealed class SalesDeliveryLine : BusinessEntity
{
    public long DeliveryId { get; set; }
    public int LineNo { get; set; }
    public long OrderLineId { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
}
