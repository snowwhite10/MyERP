using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Current-company PUR purchase invoice account map (WU-FI-003a2c, ADR-118). One map per company; it is never deleted,
/// so the resource has only read and create-or-update. The map version is the GL <c>PUR/PurchaseInvoice</c> mapping
/// version and is independent of the SD sales invoice map.
/// </summary>
[ApiController]
[Route("api/v1/gl/purchase-invoice-account-map")]
[Produces("application/json")]
[Tags("FI - GL purchase invoice account map")]
public sealed class PurchaseInvoiceAccountMapController(PurchaseInvoiceAccountMapService maps) : ControllerBase
{
    /// <summary>Read the map with current MD account labels; an unconfigured company returns configured=false, version 0.</summary>
    [HttpGet]
    [RequirePermission(PurchaseInvoiceAccountMapService.ViewPermission)]
    [ProducesResponseType<PurchaseInvoiceAccountMapRead>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PurchaseInvoiceAccountMapRead> Get(CancellationToken token) => maps.GetAsync(token);

    /// <summary>
    /// Create (version 0) or update (saved version) the payable, purchase/expense and input tax accounts. Unchanged
    /// accounts return changed=false without a new version; a stale version is 409 FI.PURCHASE_MAP.VERSION_CONFLICT; a
    /// code that is missing, inactive or not postable in MD is 422 FI.PURCHASE_MAP.ACCOUNT_INVALID with
    /// <c>accountErrors</c> keyed by field.
    /// </summary>
    [HttpPut]
    [RequirePermission(PurchaseInvoiceAccountMapService.EditPermission)]
    [ProducesResponseType<PurchaseInvoiceAccountMapWriteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseInvoiceAccountMapWriteResult> Save(PurchaseInvoiceAccountMapInput request,
        CancellationToken token) => maps.SaveAsync(request, token);
}
