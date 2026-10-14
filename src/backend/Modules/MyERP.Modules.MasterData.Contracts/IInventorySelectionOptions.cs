using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>INV 單據的 MD 唯讀選項；只傳回可用的公司主檔識別資料及物料追蹤方式。</summary>
public interface IInventorySelectionOptions
{
    Task<PagedResult<InventorySelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default);
    Task<PagedResult<InventorySelectionOption>> WarehousesAsync(PagedRequest request, CancellationToken token = default);
    Task<PagedResult<InventorySelectionOption>> LocationsAsync(long warehouseId, PagedRequest request, CancellationToken token = default);
    /// <summary>只供已授權 INV 單據詳情組裝；呼叫端必須從已保存單據取得 ID，不可接受任意用戶輸入 ID。</summary>
    Task<InventorySavedReferences> SavedAsync(IReadOnlyCollection<long> itemIds,
        IReadOnlyCollection<long> warehouseIds, IReadOnlyCollection<long> locationIds, CancellationToken token = default);
}

/// <summary>追蹤方式只在物料選項有值；倉庫及儲位為 null。</summary>
public sealed record InventorySelectionOption(long Id, string Code, string Name, string? TrackingMode = null, bool IsActive = true);

/// <summary>已保存單據引用的最小顯示資料；僅目前公司、未刪除記錄，包含停用記錄。</summary>
public sealed record InventorySavedReferences(IReadOnlyList<InventorySelectionOption> Items,
    IReadOnlyList<InventorySelectionOption> Warehouses, IReadOnlyList<InventorySelectionOption> Locations);
