using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Customer credit limits in the current company's base currency (REQ_F4116.7.1, WU-FI-001b2). Every action checks
/// its own AR.CREDIT permission; writes take the FI company/customer credit lock (ADR-074). Company, base currency,
/// status and actor are server-derived; such client fields are ignored.
/// </summary>
[ApiController]
[Route("api/v1/ar/credit-limits")]
[Produces("application/json")]
[Tags("FI - Credit limits")]
public sealed class CreditLimitsController(CreditLimitQueryService queries, CreditLimitCommandService commands)
    : ControllerBase
{
    /// <summary>List current-company credit limits, including deactivated history, with stable pagination.</summary>
    /// <remarks>Filters: customerId, status (Active/Deactivated), from/to (periods sharing an inclusive date).
    /// Sort: one of effectiveFrom (default descending), effectiveTo, customerId, amount.</remarks>
    [HttpGet]
    [RequirePermission(CreditLimitQueryService.ViewPermission)]
    [ProducesResponseType<PagedResult<CreditLimitSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<CreditLimitSummary>> List([FromQuery] CreditLimitQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>Read one credit limit; another company's id is 404 FI.CREDIT.LIMIT_NOT_FOUND.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(CreditLimitQueryService.ViewPermission)]
    [ProducesResponseType<CreditLimitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CreditLimitDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

    /// <summary>Create an Active limit for an approved customer; 409 FI.CREDIT.LIMIT_OVERLAP if another Active
    /// period of the customer shares a date, 422 when the customer or company base currency is unavailable.</summary>
    [HttpPost]
    [RequirePermission(CreditLimitCommandService.CreatePermission)]
    [ProducesResponseType<CreditLimitDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreditLimitDetail>> Create(CreditLimitInput request, CancellationToken token)
    {
        var saved = await commands.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
    }

    /// <summary>Replace the period and amount of a current-version Active limit. The customer cannot change;
    /// stale version is 409 COMMON.CONCURRENCY.CONFLICT, a deactivated limit 422 FI.CREDIT.LIMIT_NOT_ACTIVE.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(CreditLimitCommandService.EditPermission)]
    [ProducesResponseType<CreditLimitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CreditLimitDetail> Update(long id, CreditLimitUpdateRequest request, CancellationToken token) =>
        commands.UpdateAsync(id, request.Version,
            new CreditLimitInput(request.CustomerId, request.EffectiveFrom, request.EffectiveTo, request.Amount), token);

    /// <summary>Retire a current-version Active limit; the row stays as history and is never reactivated.</summary>
    [HttpPost("{id:long}/deactivate")]
    [RequirePermission(CreditLimitCommandService.DeactivatePermission)]
    [ProducesResponseType<CreditLimitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CreditLimitDetail> Deactivate(long id, CreditLimitVersionRequest request, CancellationToken token) =>
        commands.DeactivateAsync(id, request.Version, token);
}

/// <summary>Replacement values plus the version last read by the client.</summary>
public sealed record CreditLimitUpdateRequest(int Version, long CustomerId, DateOnly EffectiveFrom,
    DateOnly EffectiveTo, decimal Amount);

public sealed record CreditLimitVersionRequest(int Version);
