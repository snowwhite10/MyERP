using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

/// <summary>Purchase return document; inventory movement identifiers are external snapshots.</summary>
public sealed class PurchaseReturn : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public long PurchaseOrderId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public long? OwnerId { get; set; }
    public long? BranchId { get; set; }
    public long? DepartmentId { get; set; }
    public required string Status { get; set; }
    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class PurchaseReturnLine : BusinessEntity
{
    public long PurchaseReturnId { get; set; }
    public long ReceiptLineId { get; set; }
    public long OriginalMovementId { get; set; }
    public int LineNo { get; set; }
    public long MaterialId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Filled from the immutable INV inbound movement when posting (ADR-223).</summary>
    public decimal? OriginalUnitCost { get; set; }
}

public sealed class PurchaseReturnLineTracking : BusinessEntity
{
    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
}
