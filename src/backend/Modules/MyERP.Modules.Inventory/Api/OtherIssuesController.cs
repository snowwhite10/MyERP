using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

public sealed record OtherIssueLineRequest(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal UnitCost);
public sealed record CreateOtherIssueRequest(DateOnly BusinessDate, string? Remark, IReadOnlyList<OtherIssueLineRequest>? Lines);
public sealed record UpdateOtherIssueRequest(DateOnly BusinessDate, string? Remark, IReadOnlyList<OtherIssueLineRequest>? Lines, int Version);
public sealed record SubmitOtherIssueRequest(int Version);

public sealed record OtherIssueLineResponse(long Id, int LineNo, long ItemId, long WarehouseId, long? LocationId,
    decimal Quantity, decimal UnitCost, decimal Amount);
public sealed record OtherIssueApprovalResponse(long InstanceId, ApprovalStatus Status, int? CurrentStepNo, int StepCount);
public sealed record OtherIssueResponse(long Id, string DocumentNo, DateOnly BusinessDate, InventoryDocumentStatus Status,
    string? Remark, decimal TotalAmount, DateTime? PostedAt, DateTime CreatedAt, int Version,
    IReadOnlyList<OtherIssueLineResponse> Lines, OtherIssueApprovalResponse? Approval);

/// <summary>
/// 其他出庫（REQ_F4115.3.1–6、ADR-057）。核准／駁回由 SYS 審批 API 執行，本控制器不提供直接核准端點；
/// 最終核准在 SYS 交易內過帳 <c>OTHER_ISSUE</c> 異動帳。
/// </summary>
[ApiController]
[Route("api/v1/inv/other-issues")]
[Produces("application/json")]
[Tags("INV - Other Issues")]
public sealed class OtherIssuesController(OtherIssueService issues) : ControllerBase
{
    /// <summary>依單號／備註關鍵字、業務日期範圍與狀態查詢目前公司的其他出庫；排序 <c>businessDate</c>（預設新到舊）或 <c>documentNo</c>。</summary>
    [HttpGet]
    [RequirePermission(InventoryPermissions.OtherIssueView)]
    [ProducesResponseType<PagedResult<OtherIssueSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<OtherIssueSummary>> List([FromQuery] OtherIssueQuery request, CancellationToken token) =>
        issues.ListAsync(request, token);

    /// <summary>表頭、依行號排序的明細與最近一次 SYS 審批狀態。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(InventoryPermissions.OtherIssueView)]
    [ProducesResponseType<OtherIssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OtherIssueResponse> Get(long id, CancellationToken token) => Map(await issues.GetAsync(id, token));

    /// <summary>建立草稿並在同一交易取 <c>INV.OTHER_ISSUE</c> 單號；未設定單號規則時回 422。草稿不影響庫存。</summary>
    [HttpPost]
    [RequirePermission(InventoryPermissions.OtherIssueCreate)]
    [ProducesResponseType<OtherIssueResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OtherIssueResponse>> Create(CreateOtherIssueRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issue = await issues.CreateAsync(Input(request.BusinessDate, request.Remark, request.Lines), token);
        return StatusCode(StatusCodes.Status201Created, Map(new OtherIssueDetail(issue, null)));
    }

    /// <summary>按版本整組替換草稿或已駁回單據的日期、備註與明細；已駁回單據回到草稿。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(InventoryPermissions.OtherIssueEdit)]
    [ProducesResponseType<OtherIssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<OtherIssueResponse> Update(long id, UpdateOtherIssueRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issue = await issues.UpdateAsync(id, Input(request.BusinessDate, request.Remark, request.Lines), request.Version, token);
        return Map(new OtherIssueDetail(issue, null));
    }

    /// <summary>按版本軟刪除草稿；送審後、已駁回或已過帳回 422 <c>INV.OTHER_ISSUE.NOT_DRAFT</c>。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(InventoryPermissions.OtherIssueDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await issues.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>按版本送 SYS 審批；沒有適用審批步驟時立即核准並在同一交易過帳（狀態 <c>Posted</c>）。</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(InventoryPermissions.OtherIssueSubmit)]
    [ProducesResponseType<OtherIssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<OtherIssueResponse> Submit(long id, SubmitOtherIssueRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await issues.SubmitAsync(id, request.Version, token));
    }

    private static OtherIssueInput Input(DateOnly date, string? remark, IReadOnlyList<OtherIssueLineRequest>? lines) =>
        new(date, remark, lines?.Select(x => x is null ? null! :
            new OtherIssueLineInput(x.LineNo, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, x.UnitCost)).ToList());

    private static OtherIssueResponse Map(OtherIssueDetail detail)
    {
        var r = detail.Issue;
        return new OtherIssueResponse(r.Id, r.DocumentNo, r.BusinessDate, r.Status, r.Remark, r.TotalAmount, r.PostedAt, r.CreatedAt,
            r.Version, [.. r.Lines.OrderBy(x => x.LineNo).Select(x => new OtherIssueLineResponse(x.Id, x.LineNo, x.ItemId, x.WarehouseId,
                x.LocationId, x.Quantity, x.UnitCost, x.Amount))],
            detail.Approval is { } a ? new OtherIssueApprovalResponse(a.InstanceId, a.Status, a.CurrentStepNo, a.StepCount) : null);
    }
}
