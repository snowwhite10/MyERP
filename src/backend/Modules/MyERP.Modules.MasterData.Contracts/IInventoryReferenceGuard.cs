namespace MyERP.Modules.MasterData.Contracts;

/// <summary>
/// 在驗證物料、倉庫及可選儲位後，持有公司級共享鎖直到 action 保存完成。
/// 呼叫端若有自己的交易，須在交易提交後才釋放鎖；MD 刪除使用相同資源的排他鎖。
/// </summary>
public interface IInventoryReferenceGuard
{
    Task<T> RunAsync<T>(long itemId, long warehouseId, long? locationId,
        Func<Task<T>> action, CancellationToken token = default);

    Task<T> RunWithCodesAsync<T>(long itemId, long warehouseId, long? locationId,
        Func<InventoryReferenceCodes, Task<T>> action, CancellationToken token = default);
}

/// <summary>鎖內讀到的代碼快照與物料追蹤方式；追蹤方式在共享鎖期間不會被 MD 修改（ADR-060）。</summary>
public sealed record InventoryReferenceCodes(string ItemCode, string WarehouseCode, string? LocationCode,
    ItemTrackingMode TrackingMode = ItemTrackingMode.None);
