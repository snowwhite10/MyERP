using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Finance.Api;

/// <summary>Current-company accounting periods. Any granted data scope can read this company-wide GL resource.</summary>
[ApiController]
[Route("api/v1/gl/periods")]
[Produces("application/json")]
[Tags("FI - GL periods")]
public sealed class GlPeriodsController(GlReadService reads, GlPeriodCommandService commands) : ControllerBase
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

    /// <summary>Create an Open period. Overlap is rejected under the company SQL lock.</summary>
    [HttpPost]
    [RequirePermission(GlPeriodCommandService.CreatePermission)]
    [ProducesResponseType<GlPeriodWriteResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GlPeriodWriteResult>> Create(GlPeriodDatesRequest request, CancellationToken token)
    {
        if (request.Status is not null)
            throw new RequestValidationException("status", "A new period is always Open.");
        var result = await commands.CreateAsync(request.StartDate, request.EndDate, token);
        return CreatedAtAction(nameof(Get), new { id = result.Period.Id }, result);
    }

    /// <summary>Change dates only; a status field is rejected. Use close/reopen with their distinct permissions.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(GlPeriodCommandService.EditPermission)]
    [ProducesResponseType<GlPeriodWriteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<GlPeriodWriteResult> Edit(long id, GlPeriodEditRequest request, CancellationToken token)
    {
        if (request.Status is not null)
            throw new RequestValidationException("status", "Use the close or reopen action to change period state.");
        return commands.EditAsync(id, request.Version, request.StartDate, request.EndDate, token);
    }

    /// <summary>Close a current-version period; a repeat returns changed=false.</summary>
    [HttpPost("{id:long}/close")]
    [RequirePermission(GlPeriodCommandService.ClosePermission)]
    [ProducesResponseType<GlPeriodWriteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<GlPeriodWriteResult> Close(long id, GlPeriodVersionRequest request, CancellationToken token) =>
        commands.SetOpenAsync(id, request.Version, false, token);

    /// <summary>Reopen a current-version period; a repeat returns changed=false.</summary>
    [HttpPost("{id:long}/reopen")]
    [RequirePermission(GlPeriodCommandService.ReopenPermission)]
    [ProducesResponseType<GlPeriodWriteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<GlPeriodWriteResult> Reopen(long id, GlPeriodVersionRequest request, CancellationToken token) =>
        commands.SetOpenAsync(id, request.Version, true, token);

    /// <summary>Delete only an unreferenced current-version period.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(GlPeriodCommandService.DeletePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await commands.DeleteAsync(id, version, token);
        return NoContent();
    }
}

public sealed record GlPeriodDatesRequest(DateOnly StartDate, DateOnly EndDate, string? Status = null);
public sealed record GlPeriodEditRequest(int Version, DateOnly StartDate, DateOnly EndDate, string? Status = null);
public sealed record GlPeriodVersionRequest(int Version);

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
