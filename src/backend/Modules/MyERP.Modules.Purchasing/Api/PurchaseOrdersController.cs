using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>REQ_F4114.1.2：人工採購單草稿入口。</summary>
[ApiController]
[Route("api/v1/pur/purchase-orders")]
[Produces("application/json")]
[Tags("PUR - Purchase Orders")]
public sealed class PurchaseOrdersController(PurchaseOrderDraftService drafts,
    PurchaseOrderQueryService queries, PurchaseOrderSubmissionService submissions) : ControllerBase
{
    /// <summary>讀取目前公司與 VIEW 資料範圍內的已保存詳情，包括決標來源 ID。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(PurchasingPermissions.OrderView)]
    [ProducesResponseType<PurchaseOrderReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseOrderReadDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

    /// <summary>只有 EDIT 權限者可讀其動作範圍內的保存資料；不要求 VIEW 權限。</summary>
    [HttpGet("{id:long}/edit")]
    [RequirePermission(PurchasingPermissions.OrderEdit)]
    [ProducesResponseType<PurchaseOrderReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseOrderReadDetail> GetForEdit(long id, CancellationToken token) =>
        queries.GetForEditAsync(id, token);

    /// <summary>在目前公司建立人工草稿；SYS 發號、MD 引用及保存行整單提交。</summary>
    [HttpPost]
    [RequirePermission(PurchasingPermissions.OrderCreate)]
    [ProducesResponseType<PurchaseOrderDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PurchaseOrderDetail>> Create([FromBody] PurchaseOrderInput input,
        CancellationToken token)
    {
        var detail = await drafts.CreateAsync(input, token);
        return StatusCode(StatusCodes.Status201Created, detail);
    }

    /// <summary>
    /// 以 <paramref name="version"/> 樂觀鎖整組修訂 EDIT 範圍內的 Draft；版本不符 409、非 Draft 或修改決標行 422，
    /// 範圍外 404。請購已轉量不變，單號保持不變。
    /// </summary>
    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.OrderEdit)]
    [ProducesResponseType<PurchaseOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseOrderDetail> Update(long id, [FromQuery] int version,
        [FromBody] PurchaseOrderInput input, CancellationToken token) =>
        drafts.UpdateAsync(id, version, input, token);

    /// <summary>
    /// 以 body 的 <c>version</c> 送審 SUBMIT 範圍內的 Draft：同一交易按已保存行聚合並消耗請購餘量、建立 SYS 審批實例；
    /// 零步驟流程立即 Approved。版本不符 409；非 Draft、空單、來源無效（<c>lineNos</c>）或餘量不足（<c>shortages</c>）
    /// 422，皆零變更。範圍外或他公司 404。
    /// </summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(PurchasingPermissions.OrderSubmit)]
    [ProducesResponseType<PurchaseOrderSubmissionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseOrderSubmissionResult> Submit(long id, [FromBody] PurchaseOrderSubmitInput input,
        CancellationToken token) => submissions.SubmitAsync(id, input.Version, token);
}

/// <summary>SUBMIT 請求：目前保存版本。</summary>
public sealed record PurchaseOrderSubmitInput(int Version);
