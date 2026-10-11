using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record WarehouseResponse(long Id, string Code, string Name, string? Address, string? ContactName, string? Phone,
    bool IsActive, int Version);
public sealed record CreateWarehouseRequest(string Code, string Name, string? Address, string? ContactName, string? Phone);
public sealed record UpdateWarehouseRequest(string Name, string? Address, string? ContactName, string? Phone, bool IsActive, int Version);

/// <summary>目前公司的倉庫；庫存數量由庫存模組的異動帳維護（ADR-036）。</summary>
[ApiController]
[Route("api/v1/md/warehouses")]
[Produces("application/json")]
[Tags("MD - Warehouses")]
public sealed class WarehousesController(WarehouseService warehouses) : ControllerBase
{
    /// <summary>依代碼、名稱或地址搜尋倉庫；含停用倉庫。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.WarehouseView)]
    [ProducesResponseType<PagedResult<WarehouseResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<WarehouseResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await warehouses.ListAsync(request, token);
        return new PagedResult<WarehouseResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立倉庫；代碼在公司內永久唯一。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.WarehouseCreate)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarehouseResponse>> Create(CreateWarehouseRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created, Map(await warehouses.CreateAsync(
            request.Code, request.Name, request.Address, request.ContactName, request.Phone, token)));
    }

    /// <summary>按版本修改倉庫資料與啟用狀態；代碼不可修改。停用倉庫不可再新增儲位。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.WarehouseEdit)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<WarehouseResponse> Update(long id, UpdateWarehouseRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await warehouses.UpdateAsync(id, request.Name, request.Address, request.ContactName, request.Phone,
            request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除；仍有儲位時回 409 <c>MD.WAREHOUSE.HAS_LOCATIONS</c>，代碼仍保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.WarehouseDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await warehouses.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static WarehouseResponse Map(Warehouse entity) => new(entity.Id, entity.Code, entity.Name,
        entity.Address, entity.ContactName, entity.Phone, entity.IsActive, entity.Version);
}

public sealed record StorageLocationResponse(long Id, long WarehouseId, string Code, string Name, bool IsActive, int Version);
public sealed record CreateStorageLocationRequest(string Code, string Name);
public sealed record UpdateStorageLocationRequest(string Name, bool IsActive, int Version);

/// <summary>倉庫內的儲位；查看需要倉庫查看權限，維護需要倉庫修改權限（ADR-036）。</summary>
[ApiController]
[Route("api/v1/md/warehouses/{warehouseId:long}/locations")]
[Produces("application/json")]
[Tags("MD - Warehouses")]
public sealed class StorageLocationsController(StorageLocationService locations) : ControllerBase
{
    /// <summary>依代碼或名稱搜尋倉庫內的儲位；含停用儲位。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.WarehouseView)]
    [ProducesResponseType<PagedResult<StorageLocationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PagedResult<StorageLocationResponse>> List(long warehouseId, [FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await locations.ListAsync(warehouseId, request, token);
        return new PagedResult<StorageLocationResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>新增儲位；代碼在倉庫內永久唯一，倉庫須為啟用（否則 422 <c>MD.STORAGE_LOCATION.WAREHOUSE_INACTIVE</c>）。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.WarehouseEdit)]
    [ProducesResponseType<StorageLocationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StorageLocationResponse>> Create(long warehouseId, CreateStorageLocationRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created, Map(await locations.CreateAsync(warehouseId, request.Code, request.Name, token)));
    }

    /// <summary>按版本修改儲位名稱與啟用狀態；代碼與所屬倉庫不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.WarehouseEdit)]
    [ProducesResponseType<StorageLocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<StorageLocationResponse> Update(long warehouseId, long id, UpdateStorageLocationRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await locations.UpdateAsync(warehouseId, id, request.Name, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除儲位；代碼仍保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.WarehouseEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long warehouseId, long id, [FromQuery] int version, CancellationToken token)
    {
        await locations.DeleteAsync(warehouseId, id, version, token);
        return NoContent();
    }

    private static StorageLocationResponse Map(StorageLocation entity) =>
        new(entity.Id, entity.WarehouseId, entity.Code, entity.Name, entity.IsActive, entity.Version);
}
