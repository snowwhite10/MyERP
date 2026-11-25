using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Finance.Api;

/// <summary>Committed sales receivable and credit source trace, scoped to the current company.</summary>
[ApiController]
[Route("api/v1/ar/sales-sources")]
[Produces("application/json")]
[Tags("FI - Sales receivable sources")]
public sealed class SalesSourcesController(SalesSourceReadService reads) : ControllerBase
{
    /// <summary>List committed sources, optionally filtered by customer, type and inclusive business dates.</summary>
    [HttpGet]
    [RequirePermission(SalesSourceReadService.ViewPermission)]
    [ProducesResponseType<PagedResult<SalesSourceSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesSourceSummary>> List([FromQuery] SalesSourceQuery query, CancellationToken token) =>
        reads.ListAsync(query, token);

    /// <summary>Read saved source amounts, journal ID, current invoice balance and related credit lines.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(SalesSourceReadService.ViewPermission)]
    [ProducesResponseType<SalesSourceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesSourceDetail> Get(long id, CancellationToken token) => reads.GetAsync(id, token);

    /// <summary>Resolve a committed invoice or return by its stable SD source key after a timeout or replay.</summary>
    [HttpGet("by-key/{sourceType}/{sourceId:long}")]
    [RequirePermission(SalesSourceReadService.ViewPermission)]
    [ProducesResponseType<SalesSourceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesSourceDetail> GetByKey(SalesSourceType sourceType, long sourceId, CancellationToken token) =>
        reads.GetByKeyAsync(sourceType, sourceId, token);
}
