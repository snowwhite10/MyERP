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
    PurchaseOrderQueryService queries) : ControllerBase
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
}
