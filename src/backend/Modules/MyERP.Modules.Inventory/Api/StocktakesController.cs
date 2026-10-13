using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

public sealed record StocktakeScopeRequest(IReadOnlyList<long>? WarehouseIds, IReadOnlyList<long>? CategoryIds,
    IReadOnlyList<long>? ItemIds);
public sealed record CreateStocktakeRequest(DateOnly BusinessDate, string? Remark, StocktakeScopeRequest? Scope);
public sealed record StocktakeTrackingCountRequest(string? LotNo, string? SerialNo,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? CountedQuantity);
public sealed record StocktakeCountLineRequest(long? LineId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? CountedQuantity,
    IReadOnlyList<StocktakeTrackingCountRequest>? Tracking = null, long? ItemId = null, long? WarehouseId = null,
    long? LocationId = null);
public sealed record UpdateStocktakeRequest(DateOnly BusinessDate, string? Remark, IReadOnlyList<StocktakeCountLineRequest>? Lines,
    int Version);
public sealed record StocktakeVersionRequest(int Version);
public sealed record StocktakeTrackingResponse(long Id, string? LotNo, string? SerialNo, decimal BookQuantity, decimal? CountedQuantity);
public sealed record StocktakeLineResponse(long Id, int LineNo, long ItemId, long WarehouseId, long? LocationId, string TrackingMode,
    decimal BookQuantity, decimal? CountedQuantity, bool IsAdded, bool RecountRequired,
    IReadOnlyList<StocktakeTrackingResponse> Tracking);
public sealed record StocktakeApprovalResponse(long InstanceId, ApprovalStatus Status, int? CurrentStepNo, int StepCount);
public sealed record StocktakeResponse(long Id, string DocumentNo, DateOnly BusinessDate, InventoryDocumentStatus Status,
    string? Remark, StocktakeScopeRequest Scope, DateTime SnapshotAt, DateTime? PostedAt, DateTime CreatedAt, int Version,
    IReadOnlyList<StocktakeLineResponse> Lines, StocktakeApprovalResponse? Approval);

/// <summary>REQ_F4115.7.1／7.3／7.4／7.7：盤點快照、實盤保存、重新快照與軟刪除（ADR-062）。</summary>
[ApiController]
[Route("api/v1/inv/stocktakes")]
[Produces("application/json")]
[Tags("INV - Stocktakes")]
public sealed class StocktakesController(StocktakeService stocktakes) : ControllerBase
{
    /// <summary>依單號／備註、日期及狀態查詢目前公司的盤點單，回倉庫數、行數與已盤行數，支援分頁及排序。</summary>
    [HttpGet]
    [RequirePermission(InventoryPermissions.StocktakeView)]
    [ProducesResponseType<PagedResult<StocktakeSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<StocktakeSummary>> List([FromQuery] StocktakeQuery request, CancellationToken token) =>
        stocktakes.ListAsync(request, token);

    /// <summary>取得範圍、快照時間、依行號的帳面／實盤／批序號與最近一次 SYS 審批狀態。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(InventoryPermissions.StocktakeView)]
    [ProducesResponseType<StocktakeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<StocktakeResponse> Get(long id, CancellationToken token) => Map(await stocktakes.GetAsync(id, token));

    /// <summary>依倉庫、物料類別（含下層）與物料範圍建立盤點草稿；取號與帳面快照同一交易，不寫庫存異動。</summary>
    [HttpPost]
    [RequirePermission(InventoryPermissions.StocktakeCreate)]
    [ProducesResponseType<StocktakeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StocktakeResponse>> Create(CreateStocktakeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = request.Scope is { } s ? new StocktakeScope(s.WarehouseIds, s.CategoryIds, s.ItemIds) : null;
        var detail = await stocktakes.CreateAsync(new StocktakeInput(request.BusinessDate, request.Remark, scope), token);
        return StatusCode(StatusCodes.Status201Created, Map(detail));
    }

    /// <summary>按版本整組保存實盤數量與實地新增行；駁回單保存後回草稿。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(InventoryPermissions.StocktakeEdit)]
    [ProducesResponseType<StocktakeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<StocktakeResponse> Update(long id, UpdateStocktakeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lines = request.Lines?.Select(x => x is null ? null! : new StocktakeCountLine(x.LineId, x.CountedQuantity,
            x.Tracking?.Select(t => t is null ? null! : new StocktakeTrackingCount(t.LotNo, t.SerialNo, t.CountedQuantity)).ToList(),
            x.ItemId, x.WarehouseId, x.LocationId)).ToList();
        return Map(await stocktakes.UpdateAsync(id, new StocktakeCountInput(request.BusinessDate, request.Remark, lines),
            request.Version, token));
    }

    /// <summary>按版本依原範圍重新快照：更新帳面、保留實盤並標記帳面有變的行。只限草稿。</summary>
    [HttpPost("{id:long}/refresh-snapshot")]
    [RequirePermission(InventoryPermissions.StocktakeEdit)]
    [ProducesResponseType<StocktakeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<StocktakeResponse> RefreshSnapshot(long id, StocktakeVersionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await stocktakes.RefreshSnapshotAsync(id, request.Version, token));
    }

    /// <summary>按版本軟刪除草稿並釋放範圍；單號與稽核記錄保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(InventoryPermissions.StocktakeDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await stocktakes.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static StocktakeResponse Map(StocktakeDetail detail)
    {
        var s = detail.Stocktake;
        return new StocktakeResponse(s.Id, s.DocumentNo, s.BusinessDate, s.Status, s.Remark,
            new StocktakeScopeRequest(detail.Scope.WarehouseIds, detail.Scope.CategoryIds, detail.Scope.ItemIds),
            s.SnapshotAt, s.PostedAt, s.CreatedAt, s.Version, [.. s.Lines.OrderBy(x => x.LineNo).Select(x =>
                new StocktakeLineResponse(x.Id, x.LineNo, x.ItemId, x.WarehouseId, x.LocationId, x.TrackingMode,
                    x.BookQuantity, x.CountedQuantity, x.IsAdded, x.RecountRequired,
                    [.. x.Tracking.OrderBy(y => y.SeqNo).Select(y =>
                        new StocktakeTrackingResponse(y.Id, y.LotNo, y.SerialNo, y.BookQuantity, y.CountedQuantity))]))],
            detail.Approval is { } a ? new StocktakeApprovalResponse(a.InstanceId, a.Status, a.CurrentStepNo, a.StepCount) : null);
    }
}
