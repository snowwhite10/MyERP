using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

/// <summary>批號或序號擇一；序號數量為 1；生產日期／有效期限只用於批號。</summary>
public sealed record InventoryTrackingRequest(string? LotNo, string? SerialNo,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantity,
    DateOnly? ManufactureDate = null, DateOnly? ExpiryDate = null)
{
    internal InventoryTrackingLine ToLine() => new(LotNo, SerialNo, Quantity, ManufactureDate, ExpiryDate);

    internal static List<InventoryTrackingLine>? ToLines(IReadOnlyList<InventoryTrackingRequest>? tracking) =>
        tracking?.Select(t => t?.ToLine()!).ToList();
}

public sealed record InventoryTrackingResponse(string? LotNo, string? SerialNo, decimal Quantity, DateOnly? ManufactureDate, DateOnly? ExpiryDate);

public sealed record OtherReceiptLineRequest(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal UnitCost,
    IReadOnlyList<InventoryTrackingRequest>? Tracking = null);
public sealed record CreateOtherReceiptRequest(DateOnly BusinessDate, string? Remark, IReadOnlyList<OtherReceiptLineRequest>? Lines);
public sealed record UpdateOtherReceiptRequest(DateOnly BusinessDate, string? Remark, IReadOnlyList<OtherReceiptLineRequest>? Lines, int Version);
public sealed record SubmitOtherReceiptRequest(int Version);

public sealed record OtherReceiptLineResponse(long Id, int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, decimal UnitCost, decimal Amount, IReadOnlyList<InventoryTrackingResponse> Tracking);
public sealed record OtherReceiptApprovalResponse(long InstanceId, ApprovalStatus Status, int? CurrentStepNo, int StepCount);
public sealed record OtherReceiptResponse(long Id, string DocumentNo, DateOnly BusinessDate, InventoryDocumentStatus Status,
    string? Remark, decimal TotalAmount, DateTime? PostedAt, DateTime CreatedAt, int Version,
    IReadOnlyList<OtherReceiptLineResponse> Lines, OtherReceiptApprovalResponse? Approval);

/// <summary>
/// 其他入庫（REQ_F4115.2.1–6、ADR-054）。核准／駁回由 SYS 審批 API 執行，本控制器不提供直接核准端點；
/// 最終核准在 SYS 交易內過帳 <c>OTHER_RECEIPT</c> 異動帳。
/// </summary>
[ApiController]
[Route("api/v1/inv/other-receipts")]
[Produces("application/json")]
[Tags("INV - Other Receipts")]
public sealed class OtherReceiptsController(OtherReceiptService receipts) : ControllerBase
{
    /// <summary>依單號／備註關鍵字、業務日期範圍與狀態查詢目前公司的其他入庫；排序 <c>businessDate</c>（預設新到舊）或 <c>documentNo</c>。</summary>
    [HttpGet]
    [RequirePermission(InventoryPermissions.OtherReceiptView)]
    [ProducesResponseType<PagedResult<OtherReceiptSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<OtherReceiptSummary>> List([FromQuery] OtherReceiptQuery request, CancellationToken token) =>
        receipts.ListAsync(request, token);

    /// <summary>表頭、依行號排序的明細與最近一次 SYS 審批狀態。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(InventoryPermissions.OtherReceiptView)]
    [ProducesResponseType<OtherReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OtherReceiptResponse> Get(long id, CancellationToken token) => Map(await receipts.GetAsync(id, token));

    /// <summary>建立草稿並在同一交易取 <c>INV.OTHER_RECEIPT</c> 單號；未設定單號規則時回 422。草稿不影響庫存。</summary>
    [HttpPost]
    [RequirePermission(InventoryPermissions.OtherReceiptCreate)]
    [ProducesResponseType<OtherReceiptResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OtherReceiptResponse>> Create(CreateOtherReceiptRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var receipt = await receipts.CreateAsync(Input(request.BusinessDate, request.Remark, request.Lines), token);
        return StatusCode(StatusCodes.Status201Created, Map(new OtherReceiptDetail(receipt, null)));
    }

    /// <summary>按版本整組替換草稿或已駁回單據的日期、備註與明細；已駁回單據回到草稿。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(InventoryPermissions.OtherReceiptEdit)]
    [ProducesResponseType<OtherReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<OtherReceiptResponse> Update(long id, UpdateOtherReceiptRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var receipt = await receipts.UpdateAsync(id, Input(request.BusinessDate, request.Remark, request.Lines), request.Version, token);
        return Map(new OtherReceiptDetail(receipt, null));
    }

    /// <summary>按版本軟刪除草稿；送審後、已駁回或已過帳回 422 <c>INV.OTHER_RECEIPT.NOT_DRAFT</c>。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(InventoryPermissions.OtherReceiptDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await receipts.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>按版本送 SYS 審批；沒有適用審批步驟時立即核准並在同一交易過帳（狀態 <c>Posted</c>）。</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(InventoryPermissions.OtherReceiptSubmit)]
    [ProducesResponseType<OtherReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<OtherReceiptResponse> Submit(long id, SubmitOtherReceiptRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await receipts.SubmitAsync(id, request.Version, token));
    }

    private static OtherReceiptInput Input(DateOnly date, string? remark, IReadOnlyList<OtherReceiptLineRequest>? lines) =>
        new(date, remark, lines?.Select(x => x is null ? null! :
            new OtherReceiptLineInput(x.LineNo, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, x.UnitCost,
                InventoryTrackingRequest.ToLines(x.Tracking))).ToList());

    private static OtherReceiptResponse Map(OtherReceiptDetail detail)
    {
        var r = detail.Receipt;
        return new OtherReceiptResponse(r.Id, r.DocumentNo, r.BusinessDate, r.Status, r.Remark, r.TotalAmount, r.PostedAt, r.CreatedAt,
            r.Version, [.. r.Lines.OrderBy(x => x.LineNo).Select(x => new OtherReceiptLineResponse(x.Id, x.LineNo, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, x.UnitCost, x.Amount, [.. x.Tracking.OrderBy(t => t.SeqNo).Select(t =>
                    new InventoryTrackingResponse(t.LotNo, t.SerialNo, t.Quantity, t.ManufactureDate, t.ExpiryDate))]))],
            detail.Approval is { } a ? new OtherReceiptApprovalResponse(a.InstanceId, a.Status, a.CurrentStepNo, a.StepCount) : null);
    }
}
