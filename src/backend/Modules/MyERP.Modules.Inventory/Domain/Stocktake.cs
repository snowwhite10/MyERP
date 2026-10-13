using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>盤點表頭（ADR-062）：保存範圍、快照時間與快照時公司內最大異動 ID；草稿不影響異動帳。</summary>
public sealed class Stocktake : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public InventoryDocumentStatus Status { get; set; } = InventoryDocumentStatus.Draft;
    public required string ScopeJson { get; set; }
    public DateTime SnapshotAt { get; set; }
    public long? SnapshotMovementId { get; set; }
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<StocktakeLine> Lines { get; } = [];
}

/// <summary>
/// 一個「物料＋倉庫＋儲位」結存鍵的帳面與實盤；差異在查詢時計算。
/// <see cref="TrackingMode"/> 為 Contracts <c>ItemTrackingMode</c> 的名稱（None／Lot／Serial），快照時依異動帳、新增行依 MD 決定。
/// </summary>
public sealed class StocktakeLine : BusinessEntity
{
    public long StocktakeId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public string TrackingMode { get; set; } = "None";
    public decimal BookQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public bool IsAdded { get; set; }
    public bool RecountRequired { get; set; }
    public List<StocktakeLineTracking> Tracking { get; } = [];
}

public sealed class StocktakeLineTracking : BusinessEntity
{
    public long LineId { get; set; }
    public int SeqNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public decimal BookQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
}
