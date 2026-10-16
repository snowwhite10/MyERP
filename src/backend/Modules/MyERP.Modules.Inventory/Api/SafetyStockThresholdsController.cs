using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Inventory.Api;

public sealed record SaveSafetyStockRequest(long ItemId, long WarehouseId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal MinimumQuantity);
public sealed record UpdateSafetyStockRequest(int Version,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal MinimumQuantity);
public sealed record SafetyStockThresholdResponse(long Id, long ItemId, long WarehouseId,
    decimal MinimumQuantity, int Version);
public sealed record SafetyStockDisplayRow(long Id, long ItemId, string? ItemCode, string? ItemName,
    bool ItemActive, long WarehouseId, string? WarehouseCode, string? WarehouseName,
    bool WarehouseActive, decimal MinimumQuantity, decimal OnHand, bool IsLow,
    decimal Shortage, int Version);
public sealed record SafetyStockDisplayPage(IReadOnlyList<SafetyStockDisplayRow> Items,
    int Page, int PageSize, long TotalCount, DateTime QueriedAt);

/// <summary>公司安全庫存門檻與低量提示；查詢不建立單據或庫存異動。</summary>
[ApiController]
[Route("api/v1/inv/safety-stock-thresholds")]
[Produces("application/json")]
[Tags("INV - Safety Stock")]
public sealed class SafetyStockThresholdsController(SafetyStockService service,
    IInventorySelectionOptions references) : ControllerBase
{
    [HttpGet]
    [RequirePermission(InventoryPermissions.SafetyStockView)]
    [ProducesResponseType<SafetyStockDisplayPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<SafetyStockDisplayPage> List([FromQuery] SafetyStockQuery request, CancellationToken token)
    {
        var page = await service.ListAsync(request, token);
        if (page.Items.Count == 0)
            return new SafetyStockDisplayPage([], page.Page, page.PageSize, page.TotalCount, page.QueriedAt);
        var saved = await references.SavedAsync(page.Items.Select(x => x.ItemId).Distinct().ToArray(),
            page.Items.Select(x => x.WarehouseId).Distinct().ToArray(), [], token);
        var items = saved.Items.ToDictionary(x => x.Id);
        var warehouses = saved.Warehouses.ToDictionary(x => x.Id);
        var rows = page.Items.Select(x =>
        {
            items.TryGetValue(x.ItemId, out var item);
            warehouses.TryGetValue(x.WarehouseId, out var warehouse);
            return new SafetyStockDisplayRow(x.Id, x.ItemId, item?.Code, item?.Name,
                item?.IsActive ?? false, x.WarehouseId, warehouse?.Code, warehouse?.Name,
                warehouse?.IsActive ?? false, x.MinimumQuantity, x.OnHand, x.IsLow,
                x.Shortage, x.Version);
        }).ToArray();
        return new SafetyStockDisplayPage(rows, page.Page, page.PageSize, page.TotalCount, page.QueriedAt);
    }

    [HttpPost]
    [RequirePermission(InventoryPermissions.SafetyStockEdit)]
    [ProducesResponseType<SafetyStockThresholdResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SafetyStockThresholdResponse>> Create(SaveSafetyStockRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await service.CreateWithStatusAsync(request.ItemId, request.WarehouseId, request.MinimumQuantity, token);
        return StatusCode(result.Restored ? StatusCodes.Status200OK : StatusCodes.Status201Created, Map(result.Entity));
    }

    [HttpPut("{id:long}")]
    [RequirePermission(InventoryPermissions.SafetyStockEdit)]
    [ProducesResponseType<SafetyStockThresholdResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SafetyStockThresholdResponse> Update(long id, UpdateSafetyStockRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await service.UpdateAsync(id, request.Version, request.MinimumQuantity, token));
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(InventoryPermissions.SafetyStockEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await service.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static SafetyStockThresholdResponse Map(SafetyStockThreshold entity) =>
        new(entity.Id, entity.ItemId, entity.WarehouseId, entity.MinimumQuantity, entity.Version);
}
