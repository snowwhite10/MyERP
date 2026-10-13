using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

public sealed record TransferLineRequest(int LineNo, long ItemId, long SourceWarehouseId, long? SourceLocationId,
    long DestinationWarehouseId, long? DestinationLocationId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantity,
    IReadOnlyList<InventoryTrackingRequest>? Tracking = null);
public sealed record CreateTransferRequest(DateOnly BusinessDate, string? Remark, IReadOnlyList<TransferLineRequest>? Lines);
public sealed record UpdateTransferRequest(DateOnly BusinessDate, string? Remark, IReadOnlyList<TransferLineRequest>? Lines, int Version);
public sealed record SubmitTransferRequest(int Version);
public sealed record TransferLineResponse(long Id, int LineNo, long ItemId, long SourceWarehouseId, long? SourceLocationId,
    long DestinationWarehouseId, long? DestinationLocationId, decimal Quantity,
    IReadOnlyList<InventoryTrackingResponse> Tracking);
public sealed record TransferApprovalResponse(long InstanceId, ApprovalStatus Status, int? CurrentStepNo, int StepCount);
public sealed record TransferResponse(long Id, string DocumentNo, DateOnly BusinessDate, InventoryDocumentStatus Status,
    string? Remark, DateTime? PostedAt, DateTime CreatedAt, int Version,
    IReadOnlyList<TransferLineResponse> Lines, TransferApprovalResponse? Approval);

/// <summary>REQ_F4115.6.1–6：公司內庫存調撥；核准由 SYS 審批 API 完成。</summary>
[ApiController]
[Route("api/v1/inv/transfers")]
[Produces("application/json")]
[Tags("INV - Transfers")]
public sealed class TransfersController(TransferService transfers) : ControllerBase
{
    /// <summary>依單號、日期及狀態查詢目前公司的調撥單，支援分頁及排序。</summary>
    [HttpGet]
    [RequirePermission(InventoryPermissions.TransferView)]
    [ProducesResponseType<PagedResult<TransferSummary>>(StatusCodes.Status200OK)]
    public Task<PagedResult<TransferSummary>> List([FromQuery] TransferQuery request, CancellationToken token) =>
        transfers.ListAsync(request, token);

    /// <summary>取得單據明細與最近一次 SYS 審批狀態。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(InventoryPermissions.TransferView)]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<TransferResponse> Get(long id, CancellationToken token) => Map(await transfers.GetAsync(id, token));

    /// <summary>建立調撥草稿；取號與單據保存同一交易，尚不移動庫存。</summary>
    [HttpPost]
    [RequirePermission(InventoryPermissions.TransferCreate)]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TransferResponse>> Create(CreateTransferRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var transfer = await transfers.CreateAsync(Input(request.BusinessDate, request.Remark, request.Lines), token);
        return StatusCode(StatusCodes.Status201Created, Map(new TransferDetail(transfer, null)));
    }

    /// <summary>按版本整組替換草稿或已駁回單據；駁回修訂後返回草稿。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(InventoryPermissions.TransferEdit)]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<TransferResponse> Update(long id, UpdateTransferRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(new TransferDetail(await transfers.UpdateAsync(id,
            Input(request.BusinessDate, request.Remark, request.Lines), request.Version, token), null));
    }

    /// <summary>按版本軟刪除草稿；單號與稽核記錄保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(InventoryPermissions.TransferDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await transfers.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>按版本送 SYS 審批；立即核准時同交易產生一出一入庫存異動。</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(InventoryPermissions.TransferSubmit)]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<TransferResponse> Submit(long id, SubmitTransferRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await transfers.SubmitAsync(id, request.Version, token));
    }

    private static TransferInput Input(DateOnly date, string? remark, IReadOnlyList<TransferLineRequest>? lines) =>
        new(date, remark, lines?.Select(x => x is null ? null! :
            new TransferLineInput(x.LineNo, x.ItemId, x.SourceWarehouseId, x.SourceLocationId,
                x.DestinationWarehouseId, x.DestinationLocationId, x.Quantity,
                InventoryTrackingRequest.ToLines(x.Tracking))).ToList());

    private static TransferResponse Map(TransferDetail detail)
    {
        var t = detail.Transfer;
        return new TransferResponse(t.Id, t.DocumentNo, t.BusinessDate, t.Status, t.Remark, t.PostedAt,
            t.CreatedAt, t.Version, [.. t.Lines.OrderBy(x => x.LineNo).Select(x =>
                new TransferLineResponse(x.Id, x.LineNo, x.ItemId, x.SourceWarehouseId, x.SourceLocationId,
                    x.DestinationWarehouseId, x.DestinationLocationId, x.Quantity,
                    [.. x.Tracking.OrderBy(y => y.SeqNo).Select(y =>
                        new InventoryTrackingResponse(y.LotNo, y.SerialNo, y.Quantity, null, null))]))],
            detail.Approval is { } a ? new TransferApprovalResponse(a.InstanceId, a.Status, a.CurrentStepNo, a.StepCount) : null);
    }
}
