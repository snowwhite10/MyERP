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
public sealed class RequisitionsController(RequisitionQueryService queries, RequisitionCreateService creates,
    RequisitionMutationService mutations) : ControllerBase
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

    [HttpPost]
    [RequirePermission(PurchasingPermissions.RequisitionCreate)]
    [ProducesResponseType<RequisitionDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RequisitionDetail>> Create([FromBody] RequisitionInput input, CancellationToken token)
    {
        var result = await creates.CreateAsync(input, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.RequisitionEdit)]
    [ProducesResponseType<RequisitionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<RequisitionDetail> Update(long id, [FromQuery] int version, [FromBody] RequisitionInput input,
        CancellationToken token) => mutations.UpdateAsync(id, version, input, token);

    [HttpDelete("{id:long}")]
    [RequirePermission(PurchasingPermissions.RequisitionDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await mutations.DeleteAsync(id, version, token);
        return NoContent();
    }
}
