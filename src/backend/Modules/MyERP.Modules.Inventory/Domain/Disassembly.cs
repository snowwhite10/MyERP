using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

public enum DisassemblyLineRole { Finished, Component }

public sealed class Disassembly : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public InventoryDocumentStatus Status { get; set; } = InventoryDocumentStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<DisassemblyLine> Lines { get; } = [];
}

public sealed class DisassemblyLine : BusinessEntity
{
    public long DisassemblyId { get; set; }
    public int LineNo { get; set; }
    public DisassemblyLineRole Role { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? AllocationWeight { get; set; }
    public decimal? PostedUnitCost { get; set; }
    public List<DisassemblyLineTracking> Tracking { get; } = [];
}

public sealed class DisassemblyLineTracking : BusinessEntity
{
    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
}
