using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>Choices of the quotation and sales-order forms (ADR-167); no MD view permission needed.</summary>
[ApiController]
[Route("api/v1/sal/document-options")]
[Produces("application/json")]
[Tags("SD - Document form options")]
[RequireAnyPermission("SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT", "SAL.ORDER.CREATE", "SAL.ORDER.EDIT")]
public sealed class SalesDocumentOptionsController(SalesDocumentOptionsService options) : ControllerBase
{
    /// <summary>Active, approved customers a quotation or order can be saved for.</summary>
    /// <remarks>Needs SAL.QUOTATION.CREATE/EDIT or SAL.ORDER.CREATE/EDIT. Keyword matches customer code or name;
    /// only <c>sort=code</c> (default) is accepted; code then ID order. The save revalidates the customer.</remarks>
    [HttpGet("customers")]
    [ProducesResponseType<PagedResult<SalesSelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesSelectionOption>> Customers([FromQuery] PagedRequest request,
        CancellationToken token) => options.CustomersAsync(request, token);

    /// <summary>Active items a quotation or order line can be saved with.</summary>
    /// <remarks>Needs SAL.QUOTATION.CREATE/EDIT or SAL.ORDER.CREATE/EDIT. Keyword matches item code or name;
    /// only <c>sort=code</c> (default) is accepted; code then ID order.</remarks>
    [HttpGet("items")]
    [ProducesResponseType<PagedResult<SalesSelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesSelectionOption>> Items([FromQuery] PagedRequest request, CancellationToken token) =>
        options.ItemsAsync(request, token);

    /// <summary>Output tax rates (tax type Output or Both, active tax code) effective on <c>businessDate</c>.</summary>
    /// <remarks>Needs SAL.QUOTATION.CREATE/EDIT or SAL.ORDER.CREATE/EDIT. <c>businessDate</c> (yyyy-MM-dd) is
    /// required (400 otherwise). Keyword matches tax code or name; tax-code order.</remarks>
    [HttpGet("output-tax-rates")]
    [ProducesResponseType<PagedResult<SalesOutputTaxOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesOutputTaxOption>> OutputTaxRates([FromQuery] SalesOutputTaxOptionQuery request,
        CancellationToken token) => options.OutputTaxRatesAsync(request, token);
}
