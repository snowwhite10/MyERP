using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>Dated selling-price rules (REQ_F4112.4.1). Rules are company-wide; each operation checks its own permission.</summary>
[ApiController]
[Route("api/v1/sal/price-rules")]
[Produces("application/json")]
[Tags("SD - Price rules")]
public sealed class SalesPriceRulesController(SalesPriceRuleService rules) : ControllerBase
{
    /// <summary>List current-company price rules with item/customer/currency/date/status filters and stable paging.</summary>
    [HttpGet]
    [RequirePermission("SAL.PRICING.VIEW")]
    [ProducesResponseType<PagedResult<SalesPriceRuleDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesPriceRuleDetail>> List([FromQuery] SalesPriceRuleQuery request,
        CancellationToken token) => rules.ListAsync(request, token);

    /// <summary>Read one price rule; other-company or unknown IDs are 404.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission("SAL.PRICING.VIEW")]
    [ProducesResponseType<SalesPriceRuleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesPriceRuleDetail> Get(long id, CancellationToken token) => rules.GetAsync(id, token);

    /// <summary>
    /// Create an active rule. A null customer is the company default. 409 <c>SAL.PRICING.RULE_OVERLAP</c> (with
    /// <c>conflictingRuleIds</c>) when an active rule of the same item, currency and customer covers any of the dates.
    /// </summary>
    [HttpPost]
    [RequirePermission("SAL.PRICING.CREATE")]
    [ProducesResponseType<SalesPriceRuleDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SalesPriceRuleDetail>> Create(SalesPriceRuleInput request, CancellationToken token)
    {
        var saved = await rules.CreateAsync(request, token);
        return Created($"/api/v1/sal/price-rules/{saved.Id}", SalesPriceRuleService.Map(saved));
    }

    /// <summary>
    /// Edit an active rule with the version last read (ADR-158). Item, customer and currency may change; old and new
    /// keys are both locked. 409 <c>COMMON.CONCURRENCY.CONFLICT</c> (stale version), <c>SAL.PRICING.RULE_OVERLAP</c>
    /// (with <c>conflictingRuleIds</c>) or <c>SAL.PRICING.RULE_LOCK_TIMEOUT</c>; 422 <c>SAL.PRICING.RULE_INACTIVE</c>
    /// or inactive/unapproved MD.
    /// </summary>
    [HttpPut("{id:long}")]
    [RequirePermission("SAL.PRICING.EDIT")]
    [ProducesResponseType<SalesPriceRuleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesPriceRuleDetail> Update(long id, SalesPriceRuleUpdateRequest request,
        CancellationToken token) => SalesPriceRuleService.Map(await rules.UpdateAsync(id, request, token));

    /// <summary>
    /// Retire an active rule with the version last read; it stays readable as inactive and its dates become free.
    /// 409 stale version or <c>SAL.PRICING.RULE_LOCK_TIMEOUT</c>; 422 <c>SAL.PRICING.RULE_INACTIVE</c> when already
    /// inactive.
    /// </summary>
    [HttpPost("{id:long}/deactivate")]
    [RequirePermission("SAL.PRICING.DEACTIVATE")]
    [ProducesResponseType<SalesPriceRuleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesPriceRuleDetail> Deactivate(long id, SalesPriceRuleVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SalesPriceRuleService.Map(await rules.DeactivateAsync(id, request.Version, token));
    }
}
