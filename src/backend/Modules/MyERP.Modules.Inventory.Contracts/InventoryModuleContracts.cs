namespace MyERP.Modules.Inventory.Contracts;

/// <summary>INV 對外契約入口。</summary>
public static class InventoryModuleContracts
{
    public const string ModuleCode = "INV";
}

public enum StockDirection
{
    In,
    Out,
}

/// <summary>過帳明細；數量為物料基本單位、正數且最多六位小數，方向另以 <see cref="StockDirection"/> 表示。</summary>
public sealed record InventoryPostingLine(
    int LineNo, StockDirection Direction, long ItemId, long WarehouseId, long? LocationId, decimal Quantity, decimal? UnitCost = null);

/// <summary>一張已核准來源單據的全部庫存異動；同一來源只會過帳一次，重送相同內容回既有結果（ADR-053）。</summary>
public sealed record InventoryPostingRequest(
    string SourceType, long SourceId, DateOnly BusinessDate, IReadOnlyList<InventoryPostingLine> Lines);

public sealed record InventoryPostingResult(bool Replayed, IReadOnlyList<long> MovementIds);

/// <summary>
/// 庫存唯一寫入入口：在一個交易內驗證引用、序列化出庫、檢查負庫存並追加異動帳。
/// 呼叫端已有交易時加入該交易，鎖持有至呼叫端提交。
/// </summary>
public interface IInventoryPostingService
{
    Task<InventoryPostingResult> PostAsync(InventoryPostingRequest request, CancellationToken token = default);
}

public sealed record StockBalance(long ItemId, long WarehouseId, long? LocationId, decimal Quantity);

/// <summary>結存由異動帳即時推導，不另存可被直接修改的彙總數量。</summary>
public interface IStockBalanceQuery
{
    /// <summary>指定物料、倉庫與儲位（null 表示無儲位）的現存量；無異動時為 0。</summary>
    Task<decimal> GetOnHandAsync(long itemId, long warehouseId, long? locationId, CancellationToken token = default);

    /// <summary>目前公司的非零結存，可依物料或倉庫篩選，依物料、倉庫、儲位排序。</summary>
    Task<IReadOnlyList<StockBalance>> ListAsync(long? itemId = null, long? warehouseId = null, CancellationToken token = default);
}
