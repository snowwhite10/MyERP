using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

public sealed record OpeningStockLineRequest(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal UnitCost);
public sealed record OpeningStockRequest(string RequestKey, DateOnly OpeningDate, string? Remark, IReadOnlyList<OpeningStockLineRequest>? Lines);

public sealed record OpeningStockLineResponse(long Id, int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, decimal UnitCost, decimal Amount);
public sealed record OpeningStockResponse(long Id, string RequestKey, DateOnly OpeningDate, string? Remark, decimal TotalAmount,
    DateTime PostedAt, DateTime CreatedAt, bool Replayed, IReadOnlyList<OpeningStockLineResponse> Lines);

/// <summary>
/// 物料期初庫存（REQ_F4115.23.1–4、ADR-059，對應 BlueWhale 4.4.1.8.14）。提交即以 <c>OPENING_STOCK</c> 入庫過帳，
/// 不經 SYS 審批，亦不提供修改或刪除；錯誤以其他入庫／出庫調整。
/// </summary>
[ApiController]
[Route("api/v1/inv/opening-stocks")]
[Produces("application/json")]
[Tags("INV - Opening Stocks")]
public sealed class OpeningStocksController(OpeningStockService openings) : ControllerBase
{
    /// <summary>依啟用日期範圍、物料、倉庫與關鍵字（備註、請求鍵）查詢目前公司的期初批次；啟用日期新到舊。</summary>
    [HttpGet]
    [RequirePermission(InventoryPermissions.OpeningStockView)]
    [ProducesResponseType<PagedResult<OpeningStockSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<OpeningStockSummary>> List([FromQuery] OpeningStockQuery request, CancellationToken token) =>
        openings.ListAsync(request, token);

    /// <summary>批次表頭與依行號排序的明細。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(InventoryPermissions.OpeningStockView)]
    [ProducesResponseType<OpeningStockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OpeningStockResponse> Get(long id, CancellationToken token) => Map(await openings.GetAsync(id, token), false);

    /// <summary>唯讀預覽：格式錯誤回 400；MD 引用、批次內重複、已有期初、晚於既有異動等逐行錯誤碼放在回應內，不寫資料。</summary>
    [HttpPost("preview")]
    [RequirePermission(InventoryPermissions.OpeningStockCreate)]
    [ProducesResponseType<OpeningStockPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<OpeningStockPreview> Preview(OpeningStockRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return openings.PreviewAsync(Input(request), token);
    }

    /// <summary>
    /// 提交並入庫過帳，回 201。同一 <c>requestKey</c> 相同內容重送回 200 與原批次（<c>replayed = true</c>）；
    /// 內容不同回 409 <c>INV.OPENING_STOCK.REQUEST_CONFLICT</c>；任何一行有錯回 422 <c>INV.OPENING_STOCK.HAS_ERRORS</c>（以預覽查看逐行錯誤）。
    /// </summary>
    [HttpPost]
    [RequirePermission(InventoryPermissions.OpeningStockCreate)]
    [ProducesResponseType<OpeningStockResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<OpeningStockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OpeningStockResponse>> Submit(OpeningStockRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await openings.SubmitAsync(Input(request), token);
        return StatusCode(result.Replayed ? StatusCodes.Status200OK : StatusCodes.Status201Created, Map(result.OpeningStock, result.Replayed));
    }

    private static OpeningStockInput Input(OpeningStockRequest request) =>
        new(request.RequestKey, request.OpeningDate, request.Remark, request.Lines?.Select(x => x is null ? null! :
            new OpeningStockLineInput(x.LineNo, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, x.UnitCost)).ToList());

    private static OpeningStockResponse Map(OpeningStock x, bool replayed) =>
        new(x.Id, x.RequestKey, x.OpeningDate, x.Remark, x.TotalAmount, x.PostedAt, x.CreatedAt, replayed,
            [.. x.Lines.OrderBy(l => l.LineNo).Select(l => new OpeningStockLineResponse(l.Id, l.LineNo, l.ItemId, l.WarehouseId,
                l.LocationId, l.Quantity, l.UnitCost, l.Amount))]);
}
