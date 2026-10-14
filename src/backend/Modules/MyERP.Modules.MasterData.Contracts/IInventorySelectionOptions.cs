using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>INV 單據的 MD 唯讀選項；只傳回可用的公司主檔識別資料及物料追蹤方式。</summary>
public interface IInventorySelectionOptions
{
    Task<PagedResult<InventorySelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default);
    Task<PagedResult<InventorySelectionOption>> WarehousesAsync(PagedRequest request, CancellationToken token = default);
    Task<PagedResult<InventorySelectionOption>> LocationsAsync(long warehouseId, PagedRequest request, CancellationToken token = default);
}

/// <summary>追蹤方式只在物料選項有值；倉庫及儲位為 null。</summary>
public sealed record InventorySelectionOption(long Id, string Code, string Name, string? TrackingMode = null);
