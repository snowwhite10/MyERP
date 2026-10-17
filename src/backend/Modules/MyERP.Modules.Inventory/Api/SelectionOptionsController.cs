using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

/// <summary>INV 單據建立／編輯的唯讀 MD 選項（REQ_F4115.20.1–2）。</summary>
[ApiController]
[Route("api/v1/inv/selection-options")]
[Produces("application/json")]
[Tags("INV - Selection Options")]
public sealed class SelectionOptionsController(IInventorySelectionOptions options) : ControllerBase
{
    /// <summary>只列目前公司啟用物料並返回追蹤方式；按代碼與 ID 穩定分頁，可搜尋代碼或名稱。</summary>
    [HttpGet("items")]
    [RequireAnyPermission(
        InventoryPermissions.OtherReceiptCreate, InventoryPermissions.OtherReceiptEdit,
        InventoryPermissions.OtherIssueCreate, InventoryPermissions.OtherIssueEdit,
        InventoryPermissions.OpeningStockCreate,
        InventoryPermissions.TransferCreate, InventoryPermissions.TransferEdit,
        InventoryPermissions.StocktakeCreate, InventoryPermissions.StocktakeEdit,
        InventoryPermissions.AssemblyCreate, InventoryPermissions.AssemblyEdit,
        InventoryPermissions.SafetyStockEdit)]
    [ProducesResponseType<PagedResult<InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<InventorySelectionOption>> Items([FromQuery] PagedRequest request, CancellationToken token) =>
        options.ItemsAsync(request, token);

    /// <summary>只列目前公司啟用倉庫；按代碼與 ID 穩定分頁。</summary>
    [HttpGet("warehouses")]
    [RequireAnyPermission(
        InventoryPermissions.OtherReceiptCreate, InventoryPermissions.OtherReceiptEdit,
        InventoryPermissions.OtherIssueCreate, InventoryPermissions.OtherIssueEdit,
        InventoryPermissions.OpeningStockCreate,
        InventoryPermissions.TransferCreate, InventoryPermissions.TransferEdit,
        InventoryPermissions.StocktakeCreate, InventoryPermissions.StocktakeEdit,
        InventoryPermissions.AssemblyCreate, InventoryPermissions.AssemblyEdit,
        InventoryPermissions.SafetyStockView, InventoryPermissions.SafetyStockEdit)]
    [ProducesResponseType<PagedResult<InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<InventorySelectionOption>> Warehouses([FromQuery] PagedRequest request, CancellationToken token) =>
        options.WarehousesAsync(request, token);

    /// <summary>只列指定目前公司啟用倉庫中的啟用儲位；無效倉庫回 404。</summary>
    [HttpGet("warehouses/{warehouseId:long}/locations")]
    [RequireAnyPermission(
        InventoryPermissions.OtherReceiptCreate, InventoryPermissions.OtherReceiptEdit,
        InventoryPermissions.OtherIssueCreate, InventoryPermissions.OtherIssueEdit,
        InventoryPermissions.OpeningStockCreate,
        InventoryPermissions.TransferCreate, InventoryPermissions.TransferEdit,
        InventoryPermissions.StocktakeCreate, InventoryPermissions.StocktakeEdit,
        InventoryPermissions.AssemblyCreate, InventoryPermissions.AssemblyEdit)]
    [ProducesResponseType<PagedResult<InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<InventorySelectionOption>> Locations(long warehouseId, [FromQuery] PagedRequest request, CancellationToken token) =>
        options.LocationsAsync(warehouseId, request, token);
}
