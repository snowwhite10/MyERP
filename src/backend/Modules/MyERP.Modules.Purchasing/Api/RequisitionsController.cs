using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>REQ_F4114.3.1：請購列表與已保存詳情。</summary>
[ApiController]
[Route("api/v1/pur/requisitions")]
[Produces("application/json")]
[Tags("PUR - Requisitions")]
public sealed class RequisitionsController(RequisitionQueryService queries) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PurchasingPermissions.RequisitionView)]
    [ProducesResponseType<PagedResult<RequisitionSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<RequisitionSummary>> List([FromQuery] RequisitionQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    [HttpGet("{id:long}")]
    [RequirePermission(PurchasingPermissions.RequisitionView)]
    [ProducesResponseType<RequisitionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RequisitionDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);
}
