using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Inventory.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

/// <summary>批號／序號追溯（REQ_F4115.24.5–6、ADR-060 第 9 點）：現存位置與完整異動歷史，皆由不可改寫的批序號明細推導。</summary>
[ApiController]
[Route("api/v1/inv/tracking")]
[Produces("application/json")]
[Tags("INV - Lot and Serial Tracking")]
public sealed class TrackingController(TrackingQueryService query) : ControllerBase
{
    /// <summary>依物料、批號關鍵字查詢批號及全公司結存；<c>inStockOnly</c> 只列仍有結存者。按物料、批號排序。</summary>
    [HttpGet("lots")]
    [RequirePermission(InventoryPermissions.TrackingView)]
    [ProducesResponseType<PagedResult<LotRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<LotRow>> Lots([FromQuery] TrackingPageQuery request, CancellationToken token) =>
        query.LotsAsync(request, token);

    /// <summary>批號追溯：生產日期、有效期限、各倉庫／儲位結存及異動歷史（新到舊）。</summary>
    [HttpGet("lots/{id:long}")]
    [RequirePermission(InventoryPermissions.TrackingView)]
    [ProducesResponseType<TrackingTrace<LotRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TrackingTrace<LotRow>> Lot(long id, CancellationToken token) => query.LotAsync(id, token);

    /// <summary>下載目前公司批號的完整異動歷史 CSV；超過 10,000 列先回 422。</summary>
    [HttpGet("lots/{id:long}/export")]
    [RequirePermission(InventoryPermissions.TrackingView)]
    [RequirePermission(InventoryPermissions.TrackingExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task ExportLot(long id, CancellationToken token) => await query.ExportLotAsync(id, Response.Body,
        onReady: count => Ready(count, "lot-history.csv"), token: token);

    /// <summary>依物料、序號關鍵字查詢序號及是否在庫；<c>inStockOnly</c> 只列在庫序號。按物料、序號排序。</summary>
    [HttpGet("serials")]
    [RequirePermission(InventoryPermissions.TrackingView)]
    [ProducesResponseType<PagedResult<SerialRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<SerialRow>> Serials([FromQuery] TrackingPageQuery request, CancellationToken token) =>
        query.SerialsAsync(request, token);

    /// <summary>序號追溯：目前所在倉庫／儲位（不在庫時為空）及異動歷史（新到舊）。</summary>
    [HttpGet("serials/{id:long}")]
    [RequirePermission(InventoryPermissions.TrackingView)]
    [ProducesResponseType<TrackingTrace<SerialRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TrackingTrace<SerialRow>> Serial(long id, CancellationToken token) => query.SerialAsync(id, token);

    /// <summary>下載目前公司序號的完整異動歷史 CSV；超過 10,000 列先回 422。</summary>
    [HttpGet("serials/{id:long}/export")]
    [RequirePermission(InventoryPermissions.TrackingView)]
    [RequirePermission(InventoryPermissions.TrackingExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task ExportSerial(long id, CancellationToken token) => await query.ExportSerialAsync(id, Response.Body,
        onReady: count => Ready(count, "serial-history.csv"), token: token);

    private Task Ready(long count, string filename)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename={filename}";
        Response.Headers["X-Export-Row-Count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    }
}
