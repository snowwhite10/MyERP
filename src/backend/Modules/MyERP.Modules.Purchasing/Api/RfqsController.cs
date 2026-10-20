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
public sealed class RfqsController(RfqQueryService queries) : ControllerBase
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
}
