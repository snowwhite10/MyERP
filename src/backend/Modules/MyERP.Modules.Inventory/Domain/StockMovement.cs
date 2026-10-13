using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

public enum StockMovementDirection
{
    In,
    Out,
}

/// <summary>
/// 庫存異動帳的一行，是現存量的唯一真相；只追加不改寫（資料庫觸發器拒絕 UPDATE／DELETE，ADR-053）。
/// 數量為基本單位正數，方向另存；物料、倉庫、儲位冗餘自 <see cref="ReferenceId"/> 指向的引用，供結存彙總索引使用。
/// </summary>
public sealed class StockMovement : BusinessEntity
{
    public required string SourceType { get; init; }
    public long SourceId { get; init; }
    public int SourceLineNo { get; init; }
    public StockMovementDirection Direction { get; init; }
    public long ReferenceId { get; init; }
    public long ItemId { get; init; }
    public long WarehouseId { get; init; }
    public long? LocationId { get; init; }
    public decimal Quantity { get; init; }
    public decimal? UnitCost { get; init; }
    public DateOnly BusinessDate { get; init; }
    public DateTime PostedAt { get; init; }
    public InventoryReference? Reference { get; init; }
}
