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
}
