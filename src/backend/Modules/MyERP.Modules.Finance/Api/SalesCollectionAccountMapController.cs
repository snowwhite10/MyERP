using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Current-company sales collection account map (WU-FI-005a2d, ADR-123). One map per company; it is never deleted, so
/// the resource has only read and create-or-update. The map version is the GL <c>FI/SalesCollection</c> and
/// <c>FI/CollectionWriteOff</c> mapping version and is independent of the SD sales invoice map.
/// </summary>
[ApiController]
[Route("api/v1/gl/sales-collection-account-map")]
[Produces("application/json")]
[Tags("FI - GL sales collection account map")]
public sealed class SalesCollectionAccountMapController(SalesCollectionAccountMapService maps) : ControllerBase
{
    /// <summary>
    /// Read the map with current MD account labels and the SD invoice map's receivable code; an unconfigured company
    /// returns configured=false, version 0. <c>warnings</c> contains FI.COLLECTION_MAP.RECEIVABLE_DIFFERS_FROM_SALES_MAP
    /// when the AR control account differs from the SD invoice map's receivable account.
    /// </summary>
    [HttpGet]
    [RequirePermission(SalesCollectionAccountMapService.ViewPermission)]
    [ProducesResponseType<SalesCollectionAccountMapRead>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<SalesCollectionAccountMapRead> Get(CancellationToken token) => maps.GetAsync(token);

    /// <summary>
    /// Create (version 0) or update (saved version) the unapplied-cash clearing and AR control accounts. Unchanged
    /// accounts return changed=false without a new version; a stale version is 409 FI.COLLECTION_MAP.VERSION_CONFLICT;
    /// a code that is missing, inactive or not postable in MD, or an AR code equal to the unapplied-cash code, is 422
    /// FI.COLLECTION_MAP.ACCOUNT_INVALID with <c>accountErrors</c> keyed by field.
    /// </summary>
    [HttpPut]
    [RequirePermission(SalesCollectionAccountMapService.EditPermission)]
    [ProducesResponseType<SalesCollectionAccountMapWriteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<SalesCollectionAccountMapWriteResult> Save(SalesCollectionAccountMapInput request,
        CancellationToken token) => maps.SaveAsync(request, token);
}
