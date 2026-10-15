using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

public enum AssemblyLineRole { Finished, Component }

public sealed class Assembly : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public InventoryDocumentStatus Status { get; set; } = InventoryDocumentStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<AssemblyLine> Lines { get; } = [];
}

public sealed class AssemblyLine : BusinessEntity
{
    public long AssemblyId { get; set; }
    public int LineNo { get; set; }
    public AssemblyLineRole Role { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? PostedUnitCost { get; set; }
    public List<AssemblyLineTracking> Tracking { get; } = [];
}

public sealed class AssemblyLineTracking : BusinessEntity
{
    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
}
