using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>Dated selling-price rules (REQ_F4112.4.1). Rules are company-wide; each operation checks its own permission.</summary>
[ApiController]
[Route("api/v1/sal/price-rules")]
[Produces("application/json")]
[Tags("SD - Price rules")]
public sealed class SalesPriceRulesController(SalesPriceRuleService rules, SalesPriceQuoteService quotes,
    SalesPriceRuleLabels labels, SalesPricingOptionsService options)
    : ControllerBase
{
    /// <summary>Active items a rule can be saved for (ADR-162); no MD.ITEM.VIEW needed.</summary>
    /// <remarks>Needs SAL.PRICING.VIEW, CREATE or EDIT. Keyword matches item code or name; only <c>sort=code</c>
    /// (default) is accepted; code then ID order. The rule save still revalidates the item.</remarks>
    [HttpGet("item-options")]
    [RequireAnyPermission("SAL.PRICING.VIEW", "SAL.PRICING.CREATE", "SAL.PRICING.EDIT")]
    [ProducesResponseType<PagedResult<SalesSelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesSelectionOption>> ItemOptions([FromQuery] PagedRequest request,
        CancellationToken token) => options.ItemsAsync(request, token);

    /// <summary>Active, approved customers a customer rule can be saved for (ADR-162); no MD.CUSTOMER.VIEW needed.</summary>
    /// <remarks>Needs SAL.PRICING.VIEW, CREATE or EDIT. Keyword matches customer code or name; only <c>sort=code</c>
    /// (default) is accepted; code then ID order.</remarks>
    [HttpGet("customer-options")]
    [RequireAnyPermission("SAL.PRICING.VIEW", "SAL.PRICING.CREATE", "SAL.PRICING.EDIT")]
    [ProducesResponseType<PagedResult<SalesSelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesSelectionOption>> CustomerOptions([FromQuery] PagedRequest request,
        CancellationToken token) => options.CustomersAsync(request, token);

    /// <summary>Active currencies of the current company with decimal places, in code order (ADR-162).</summary>
    /// <remarks>Needs SAL.PRICING.VIEW, CREATE or EDIT.</remarks>
    [HttpGet("currency-options")]
    [RequireAnyPermission("SAL.PRICING.VIEW", "SAL.PRICING.CREATE", "SAL.PRICING.EDIT")]
    [ProducesResponseType<IReadOnlyList<SalesCurrencyOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyList<SalesCurrencyOption>> CurrencyOptions(CancellationToken token) =>
        options.CurrenciesAsync(token);

    /// <summary>List current-company price rules with item/customer/currency/date/status filters and stable paging. Every
    /// rule response carries the current MD item/customer code and name (ADR-161).</summary>
    [HttpGet]
    [RequirePermission("SAL.PRICING.VIEW")]
    [ProducesResponseType<PagedResult<SalesPriceRuleDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<PagedResult<SalesPriceRuleDetail>> List([FromQuery] SalesPriceRuleQuery request,
        CancellationToken token)
    {
        var page = await rules.ListAsync(request, token);
        return page with { Items = await labels.DescribeAsync(page.Items, token) };
    }

    /// <summary>
    /// Read-only dated quote (BR-SD-027, ADR-159): the single active customer rule covering <c>businessDate</c>, or the
    /// company default rule when the customer has none. Writes nothing; the result is advisory and document save
    /// resolves the rule again.
    /// </summary>
    /// <remarks>Needs SAL.PRICING.VIEW or any of SAL.QUOTATION.CREATE/EDIT, SAL.ORDER.CREATE/EDIT (form use). 400
    /// missing or malformed parameters; 404 hidden/unknown item or customer; 422 inactive item, unapproved customer or
    /// <c>SAL.PRICING.RULE_NOT_FOUND</c>; 409 <c>SAL.PRICING.DUPLICATE_ACTIVE_RULE</c> with <c>conflictingRuleIds</c>
    /// (integrity damage: no price is guessed).</remarks>
    [HttpGet("quote")]
    [RequireAnyPermission("SAL.PRICING.VIEW", "SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT", "SAL.ORDER.CREATE",
        "SAL.ORDER.EDIT")]
    [ProducesResponseType<SalesPriceQuoteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<SalesPriceQuoteResult> Quote([FromQuery] SalesPriceQuoteQuery request, CancellationToken token) =>
        quotes.QuoteAsync(request, token);

    /// <summary>Read one price rule; other-company or unknown IDs are 404.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission("SAL.PRICING.VIEW")]
    [ProducesResponseType<SalesPriceRuleDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<SalesPriceRuleDetail> Get(long id, CancellationToken token) =>
        await labels.DescribeAsync(await rules.GetAsync(id, token), token);

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
        return Created($"/api/v1/sal/price-rules/{saved.Id}", await labels.DescribeAsync(saved, token));
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
        CancellationToken token) => await labels.DescribeAsync(await rules.UpdateAsync(id, request, token), token);

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
        return await labels.DescribeAsync(await rules.DeactivateAsync(id, request.Version, token), token);
    }
}
