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
public sealed class RfqsController(RfqQueryService queries, RfqDraftService drafts,
    QuotationQueryService quotations, QuotationMutationService quotationMutations,
    QuotationComparisonService comparisons) : ControllerBase
{
    /// <summary>逐行按本位幣含稅成本排序；缺率、撤回及過期保留明確不可比原因。</summary>
    [HttpGet("{id:long}/comparison")]
    [RequirePermission(PurchasingPermissions.RfqCompare)]
    [ProducesResponseType<QuotationComparison>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<QuotationComparison> Compare(long id, CancellationToken token) =>
        comparisons.GetAsync(id, token);

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

    /// <summary>受權報價列表；按詢價歸屬過濾，包含撤回報價及目前修訂摘要。</summary>
    [HttpGet("{id:long}/quotations")]
    [RequirePermission(PurchasingPermissions.QuotationView)]
    [ProducesResponseType<PagedResult<QuotationSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<QuotationSummary>> ListQuotations(long id,
        [FromQuery] QuotationQuery request, CancellationToken token) =>
        quotations.ListAsync(id, request, token);

    /// <summary>單一報價的全部不可變修訂與逐行價格；先檢查詢價可見範圍。</summary>
    [HttpGet("{id:long}/quotations/{quotationId:long}")]
    [RequirePermission(PurchasingPermissions.QuotationView)]
    [ProducesResponseType<QuotationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<QuotationDetail> GetQuotation(long id, long quotationId,
        CancellationToken token) => quotations.GetAsync(id, quotationId, token);

    /// <summary>REQ_F4114.5.2：受邀供應商逐行首版報價，驗目前有效的 MD 幣別與稅率。</summary>
    [HttpPost("{id:long}/quotations")]
    [RequirePermission(PurchasingPermissions.QuotationQuote)]
    [ProducesResponseType<QuotationDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<QuotationDetail>> CreateQuotation(long id,
        [FromBody] QuotationInput input, CancellationToken token)
    {
        var result = await quotationMutations.CreateAsync(id, input, token);
        return CreatedAtAction(nameof(GetQuotation), new { id, quotationId = result.Id }, result);
    }

    /// <summary>REQ_F4114.5.3：以目前版本建立新修訂，保留全部舊版。</summary>
    [HttpPost("{id:long}/quotations/{quotationId:long}/revisions")]
    [RequirePermission(PurchasingPermissions.QuotationQuote)]
    [ProducesResponseType<QuotationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<QuotationDetail> ReviseQuotation(long id, long quotationId,
        [FromQuery] int version, [FromBody] QuotationInput input, CancellationToken token) =>
        quotationMutations.ReviseAsync(id, quotationId, version, input, token);

    /// <summary>REQ_F4114.5.4：撤回未決標的有效報價；歷史修訂保持可查。</summary>
    [HttpPost("{id:long}/quotations/{quotationId:long}/withdraw")]
    [RequirePermission(PurchasingPermissions.QuotationQuote)]
    [ProducesResponseType<QuotationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<QuotationDetail> WithdrawQuotation(long id, long quotationId,
        [FromQuery] int version, CancellationToken token) =>
        quotationMutations.WithdrawAsync(id, quotationId, version, token);

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

    /// <summary>重驗物料、請購來源及供應商，原子保存發送時間和供應商識別快照。</summary>
    [HttpPost("{id:long}/send")]
    [RequirePermission(PurchasingPermissions.RfqSend)]
    [ProducesResponseType<RfqDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<RfqDetail> Send(long id, [FromQuery] int version, CancellationToken token) =>
        drafts.SendAsync(id, version, token);

    /// <summary>只關閉已發送詢價，保留邀請快照及歷史。</summary>
    [HttpPost("{id:long}/close")]
    [RequirePermission(PurchasingPermissions.RfqClose)]
    [ProducesResponseType<RfqDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<RfqDetail> Close(long id, [FromQuery] int version, CancellationToken token) =>
        drafts.CloseAsync(id, version, token);
}
