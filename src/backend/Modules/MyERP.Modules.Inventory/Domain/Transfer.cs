using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>公司內庫存調撥表頭；草稿不影響異動帳。</summary>
public sealed class Transfer : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public InventoryDocumentStatus Status { get; set; } = InventoryDocumentStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<TransferLine> Lines { get; } = [];
}

public sealed class TransferLine : BusinessEntity
{
    public long TransferId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public long SourceWarehouseId { get; set; }
    public long? SourceLocationId { get; set; }
    public long DestinationWarehouseId { get; set; }
    public long? DestinationLocationId { get; set; }
    public decimal Quantity { get; set; }
    public List<TransferLineTracking> Tracking { get; } = [];
}

public sealed class TransferLineTracking : BusinessEntity
{
    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal Quantity { get; set; }
}
