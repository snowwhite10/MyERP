using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Api;

/// <summary>
/// 審批流程定義（REQ_F4118.29.1–2）：每個單據類型一條多級流程，步驟可設角色或用戶審批人與金額門檻。
/// 沒有刪除端點，不再使用的流程以停用處理（停用後送審即自動核准）。
/// </summary>
[ApiController]
[Route("api/v1/sys/approval-flows")]
[Produces("application/json")]
[Tags("SYS - Approval Flows")]
public sealed class ApprovalFlowsController(ApprovalFlowService flows) : ControllerBase
{
    /// <summary>流程列表（含步驟）：分頁、關鍵字（單據類型／名稱）、排序 <c>documentType</c>（預設）／<c>name</c>。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.ApprovalFlowView)]
    [ProducesResponseType<PagedResult<ApprovalFlowDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<ApprovalFlowDetail>> List([FromQuery] PagedRequest request, CancellationToken cancellationToken) =>
        flows.ListAsync(request, cancellationToken);

    /// <summary>單一流程。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(SystemPermissions.ApprovalFlowView)]
    [ProducesResponseType<ApprovalFlowDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ApprovalFlowDetail> Get(long id, CancellationToken cancellationToken) => flows.GetAsync(id, cancellationToken);

    /// <summary>
    /// 建立流程。單據類型 <c>{PREFIX}.{DOCUMENT}</c> 在公司內唯一；1–10 個步驟，依清單次序執行；
    /// 角色步驟填 <c>roleId</c>（啟用角色），用戶步驟填 <c>userId</c>（本公司啟用用戶）；<c>minAmount</c> 為金額門檻。
    /// </summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.ApprovalFlowCreate)]
    [ProducesResponseType<ApprovalFlowDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApprovalFlowDetail>> Create(ApprovalFlowInput input, CancellationToken cancellationToken)
    {
        var created = await flows.CreateAsync(input, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>修改名稱、自審設定、啟用狀態並整組取代步驟，須帶 <c>version</c>。審批中的實例不受影響。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.ApprovalFlowEdit)]
    [ProducesResponseType<ApprovalFlowDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ApprovalFlowDetail> Update(long id, ApprovalFlowUpdate input, CancellationToken cancellationToken) =>
        flows.UpdateAsync(id, input, cancellationToken);
}

/// <summary>審批代理（REQ_F4118.29.3）：被代理人在期間內的審批工作可由代理人處理。</summary>
[ApiController]
[Route("api/v1/sys/approval-delegations")]
[Produces("application/json")]
[Tags("SYS - Approval Delegations")]
public sealed class ApprovalDelegationsController(ApprovalDelegationService delegations) : ControllerBase
{
    /// <summary>代理列表：<c>userId</c>（被代理人或代理人）、<c>activeOn</c>（該日有效），排序 <c>startDate</c>（預設新到舊）。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.ApprovalDelegationView)]
    [ProducesResponseType<PagedResult<ApprovalDelegationDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ApprovalDelegationDetail>> List([FromQuery] ApprovalDelegationQuery request, CancellationToken cancellationToken) =>
        delegations.ListAsync(request, cancellationToken);

    /// <summary>建立代理：兩位不同的本公司啟用用戶、起訖日期（含頭尾）；同一被代理人啟用中的期間不可重疊（409）。</summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.ApprovalDelegationCreate)]
    [ProducesResponseType<ApprovalDelegationDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApprovalDelegationDetail>> Create(ApprovalDelegationInput input, CancellationToken cancellationToken)
    {
        var created = await delegations.CreateAsync(input, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>修改期間、備註或啟用狀態，須帶 <c>version</c>。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.ApprovalDelegationEdit)]
    [ProducesResponseType<ApprovalDelegationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ApprovalDelegationDetail> Update(long id, ApprovalDelegationUpdate input, CancellationToken cancellationToken) =>
        delegations.UpdateAsync(id, input, cancellationToken);
}

/// <summary>
/// 審批實例（REQ_F4118.29.5–7）：我的待辦、核准、駁回與審批歷史。
/// 送審與撤回由各模組在進程內經 <c>IApprovalWorkflowService</c> 呼叫，不經 HTTP。
/// </summary>
[ApiController]
[Route("api/v1/sys/approvals")]
[Produces("application/json")]
[Tags("SYS - Approvals")]
public sealed class ApprovalsController(ApprovalWorkflowService approvals) : ControllerBase
{
    /// <summary>我的待辦：本人或代理可處理的目前步驟（不含自己送審的單據），排序 <c>submittedAt</c>（預設舊到新）。</summary>
    [HttpGet("tasks")]
    [RequirePermission(SystemPermissions.ApprovalProcess)]
    [ProducesResponseType<PagedResult<ApprovalTask>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ApprovalTask>> Tasks([FromQuery] ApprovalTaskQuery request, CancellationToken cancellationToken) =>
        approvals.ListTasksAsync(request, cancellationToken);

    /// <summary>審批歷史：<c>documentType</c>、<c>documentId</c>、<c>status</c> 篩選，排序 <c>submittedAt</c>（預設新到舊）。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.ApprovalView)]
    [ProducesResponseType<PagedResult<ApprovalInstanceSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ApprovalInstanceSummary>> List([FromQuery] ApprovalInstanceQuery request, CancellationToken cancellationToken) =>
        approvals.ListAsync(request, cancellationToken);

    /// <summary>單一審批實例及各步驟的處理記錄。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(SystemPermissions.ApprovalView)]
    [ProducesResponseType<ApprovalInstanceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ApprovalInstanceDetail> Get(long id, CancellationToken cancellationToken) => approvals.GetAsync(id, cancellationToken);

    /// <summary>
    /// 核准目前步驟，須帶 <c>version</c>。非審批人 403 <c>SYS.APPROVAL.NOT_APPROVER</c>；自己送審 422 <c>SYS.APPROVAL.SELF_APPROVAL</c>；
    /// 已完成 422 <c>SYS.APPROVAL.NOT_PENDING</c>；版本不符 409。
    /// </summary>
    [HttpPost("{id:long}/approve")]
    [RequirePermission(SystemPermissions.ApprovalProcess)]
    [ProducesResponseType<ApprovalInstanceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<ApprovalInstanceDetail> Approve(long id, ApprovalDecision decision, CancellationToken cancellationToken) =>
        approvals.ApproveAsync(id, decision, cancellationToken);

    /// <summary>駁回（必須填寫 <c>comment</c>），須帶 <c>version</c>；錯誤同核准。</summary>
    [HttpPost("{id:long}/reject")]
    [RequirePermission(SystemPermissions.ApprovalProcess)]
    [ProducesResponseType<ApprovalInstanceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<ApprovalInstanceDetail> Reject(long id, ApprovalDecision decision, CancellationToken cancellationToken) =>
        approvals.RejectAsync(id, decision, cancellationToken);
}
