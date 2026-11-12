using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Finance.Api;

/// <summary>Current-company accounting periods. Any granted data scope can read this company-wide GL resource.</summary>
[ApiController]
[Route("api/v1/gl/periods")]
[Produces("application/json")]
[Tags("FI - GL periods")]
public sealed class GlPeriodsController(GlReadService reads) : ControllerBase
{
    /// <summary>List Open and Closed accounting periods with inclusive date-overlap filters.</summary>
    [HttpGet]
    [RequirePermission(GlReadService.PeriodViewPermission)]
    [ProducesResponseType<PagedResult<GlPeriodRead>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<GlPeriodRead>> List([FromQuery] GlPeriodQuery query, CancellationToken token) =>
        reads.ListPeriodsAsync(query, token);

    /// <summary>Read one period; another company's id is hidden as 404.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(GlReadService.PeriodViewPermission)]
    [ProducesResponseType<GlPeriodRead>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<GlPeriodRead> Get(long id, CancellationToken token) => reads.GetPeriodAsync(id, token);
}

/// <summary>Immutable Posted GL journals. There is no public journal write route.</summary>
[ApiController]
[Route("api/v1/gl/journals")]
[Produces("application/json")]
[Tags("FI - GL journals")]
public sealed class GlJournalsController(GlReadService reads) : ControllerBase
{
    /// <summary>List committed Posted journals; filter by source, period, date or saved account code.</summary>
    [HttpGet]
    [RequirePermission(GlReadService.JournalViewPermission)]
    [ProducesResponseType<PagedResult<GlJournalRead>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<GlJournalRead>> List([FromQuery] GlJournalQuery query, CancellationToken token) =>
        reads.ListJournalsAsync(query, token);

    /// <summary>Read an immutable header, saved source and ordered account/amount snapshots; hidden ids are 404.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(GlReadService.JournalViewPermission)]
    [ProducesResponseType<GlJournalDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<GlJournalDetail> Get(long id, CancellationToken token) => reads.GetJournalAsync(id, token);
}
