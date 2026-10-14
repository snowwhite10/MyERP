using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>INV 單據的 MD 唯讀選項；只傳回可用的公司主檔識別資料。</summary>
public interface IInventorySelectionOptions
{
    Task<PagedResult<InventorySelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default);
    Task<PagedResult<InventorySelectionOption>> WarehousesAsync(PagedRequest request, CancellationToken token = default);
    Task<PagedResult<InventorySelectionOption>> LocationsAsync(long warehouseId, PagedRequest request, CancellationToken token = default);
}

public sealed record InventorySelectionOption(long Id, string Code, string Name);
