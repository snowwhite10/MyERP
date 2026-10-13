using MyERP.Shared.Domain;

namespace MyERP.Modules.Inventory.Domain;

/// <summary>
/// 批號（ADR-060 第 4 點）：公司 + 物料 + 批號唯一（依資料庫定序不分大小寫），首次入庫時建立；
/// 不存數量，結存由 <see cref="StockMovementTracking"/> 配合異動方向即時加總。
/// </summary>
public sealed class StockLot : BusinessEntity
{
    public const int MaxLength = 50;

    public long ItemId { get; init; }
    public required string LotNo { get; init; }
    public DateOnly? ManufactureDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
}

/// <summary>序號（ADR-060 第 5 點）：公司 + 物料 + 序號唯一；每個序號一個基本單位，不存可改寫的在庫狀態。</summary>
public sealed class StockSerial : BusinessEntity
{
    public const int MaxLength = 100;

    public long ItemId { get; init; }
    public required string SerialNo { get; init; }
}

/// <summary>
/// 異動帳的批序號明細（ADR-060 第 6 點）：批號或序號擇一，數量合計等於所屬異動數量；
/// 與異動帳一樣只追加不改寫（資料庫觸發器拒絕 UPDATE／DELETE）。
/// </summary>
public sealed class StockMovementTracking : BusinessEntity
{
    public long MovementId { get; init; }
    public long? LotId { get; init; }
    public long? SerialId { get; init; }
    public decimal Quantity { get; init; }
    public StockMovement? Movement { get; init; }
    public StockLot? Lot { get; init; }
    public StockSerial? Serial { get; init; }
}
