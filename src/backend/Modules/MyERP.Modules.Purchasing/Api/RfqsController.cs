using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>REQ_F4114.4.1：受權詢價列表與已保存詳情。</summary>
[ApiController]
[Route("api/v1/pur/rfqs")]
[Produces("application/json")]
[Tags("PUR - RFQs")]
public sealed class RfqsController(RfqQueryService queries, RfqDraftService drafts) : ControllerBase
{
    /// <summary>單號、業務日期、狀態及請購來源篩選；公司及資料範圍在 SQL 分頁前套用。</summary>
    [HttpGet]
    [RequirePermission(PurchasingPermissions.RfqView)]
    [ProducesResponseType<PagedResult<RfqSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<RfqSummary>> List([FromQuery] RfqQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>保存的行、邀請、版本與發送快照；他公司或資料範圍外回 404。</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(PurchasingPermissions.RfqView)]
    [ProducesResponseType<RfqDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RfqDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

    /// <summary>建立詢價草稿；公司、歸屬及單號由伺服器決定，整單同交易保存。</summary>
    [HttpPost]
    [RequirePermission(PurchasingPermissions.RfqCreate)]
    [ProducesResponseType<RfqDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RfqDetail>> Create([FromBody] RfqInput input, CancellationToken token)
    {
        var result = await drafts.CreateAsync(input, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>按版本整組替換草稿行及受邀供應商。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.RfqEdit)]
    [ProducesResponseType<RfqDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<RfqDetail> Update(long id, [FromQuery] int version, [FromBody] RfqInput input,
        CancellationToken token) => drafts.UpdateAsync(id, version, input, token);

    /// <summary>按版本軟刪未發送草稿，已發單號不會重用。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(PurchasingPermissions.RfqDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await drafts.DeleteAsync(id, version, token);
        return NoContent();
    }
}
