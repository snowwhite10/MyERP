using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Current-company SD invoice account map (WU-FI-002a4b, ADR-110). One map per company; it is never deleted, so the
/// resource has only read and create-or-update. The map version is the GL <c>SD/Invoice</c> mapping version.
/// </summary>
[ApiController]
[Route("api/v1/gl/sales-invoice-account-map")]
[Produces("application/json")]
[Tags("FI - GL sales invoice account map")]
public sealed class SalesInvoiceAccountMapController(SalesInvoiceAccountMapService maps) : ControllerBase
{
    /// <summary>Read the map with current MD account labels; an unconfigured company returns configured=false, version 0.</summary>
    [HttpGet]
    [RequirePermission(SalesInvoiceAccountMapService.ViewPermission)]
    [ProducesResponseType<SalesInvoiceAccountMapRead>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<SalesInvoiceAccountMapRead> Get(CancellationToken token) => maps.GetAsync(token);

    /// <summary>
    /// Create (version 0) or update (saved version) the three accounts. Unchanged accounts return changed=false without a
    /// new version; a stale version is 409 FI.SALES_MAP.VERSION_CONFLICT; a code that is missing, inactive or not postable
    /// in MD is 422 FI.SALES_MAP.ACCOUNT_INVALID with <c>accountErrors</c> keyed by field.
    /// </summary>
    [HttpPut]
    [RequirePermission(SalesInvoiceAccountMapService.EditPermission)]
    [ProducesResponseType<SalesInvoiceAccountMapWriteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<SalesInvoiceAccountMapWriteResult> Save(SalesInvoiceAccountMapInput request, CancellationToken token) =>
        maps.SaveAsync(request, token);
}
