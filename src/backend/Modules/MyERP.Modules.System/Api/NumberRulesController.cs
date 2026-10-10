using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Api;

/// <summary>
/// 單號規則維護（REQ_F4118.28.1–4）。只處理目前公司的規則；沒有刪除端點，不再使用的規則以停用處理。
/// 實際取號由各模組在進程內呼叫 <c>INumberGenerator</c>，不經 HTTP。
/// </summary>
[ApiController]
[Route("api/v1/sys/number-rules")]
[Produces("application/json")]
[Tags("SYS - Number Rules")]
public sealed class NumberRulesController(NumberRuleService rules) : ControllerBase
{
    /// <summary>規則列表：分頁、關鍵字（單據類型／名稱）、排序 <c>documentType</c>（預設）／<c>name</c>（單一欄位）。</summary>
    [HttpGet]
    [RequirePermission(SystemPermissions.NumberRuleView)]
    [ProducesResponseType<PagedResult<NumberRuleDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<NumberRuleDetail>> List([FromQuery] PagedRequest request, CancellationToken cancellationToken) =>
        rules.ListAsync(request, cancellationToken);

    /// <summary>單一規則。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(SystemPermissions.NumberRuleView)]
    [ProducesResponseType<NumberRuleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NumberRuleDetail> Get(long id, CancellationToken cancellationToken) => rules.GetAsync(id, cancellationToken);

    /// <summary>
    /// 預覽指定業務日期 <c>date</c>（yyyy-MM-dd，預設今天）的下一個單號，不佔用流水號。
    /// </summary>
    [HttpGet("{id:long}/preview")]
    [RequirePermission(SystemPermissions.NumberRuleView)]
    [ProducesResponseType<NumberPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<NumberPreview> Preview(long id, [FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
        rules.PreviewAsync(id, date, cancellationToken);

    /// <summary>
    /// 建立規則。單據類型 <c>{PREFIX}.{DOCUMENT}</c>（例如 <c>SAL.ORDER</c>）在公司內唯一；前綴最多 10 字；
    /// 流水號長度 3–10；重設週期必須包含在日期格式內。
    /// </summary>
    [HttpPost]
    [RequirePermission(SystemPermissions.NumberRuleCreate)]
    [ProducesResponseType<NumberRuleDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NumberRuleDetail>> Create(NumberRuleInput input, CancellationToken cancellationToken)
    {
        var created = await rules.CreateAsync(input, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// 修改名稱、前綴、格式、長度、週期或啟用狀態，須帶 <c>version</c>。已發出單號的規則不可改日期格式與重設週期、
    /// 不可縮短流水號長度（422 <c>SYS.NUMBER_RULE.IN_USE</c>）。
    /// </summary>
    [HttpPut("{id:long}")]
    [RequirePermission(SystemPermissions.NumberRuleEdit)]
    [ProducesResponseType<NumberRuleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<NumberRuleDetail> Update(long id, NumberRuleUpdate input, CancellationToken cancellationToken) =>
        rules.UpdateAsync(id, input, cancellationToken);
}
