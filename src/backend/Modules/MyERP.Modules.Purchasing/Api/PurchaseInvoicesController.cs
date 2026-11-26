using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>Purchase invoice drafts backed by locked PUR order and receipt sources.</summary>
[ApiController]
[Route("api/v1/pur/invoices")]
[Produces("application/json")]
[Tags("PUR - Invoices")]
public sealed class PurchaseInvoicesController(PurchaseInvoiceDraftMutationService drafts,
    PurchaseInvoiceMatchService matches, PurchaseInvoiceResolveService resolutions) : ControllerBase
{
    /// <summary>Create one Draft invoice from an Approved order and Posted receipt lines.</summary>
    /// <remarks>Requires PUR.INVOICE.CREATE with company data scope. The server rechecks every source and
    /// amount under the supplier transaction lock. Duplicate supplier numbers return 409; invalid sources
    /// return 422. A successful response contains the SQL IDENTITY and persisted version.</remarks>
    [HttpPost]
    [RequirePermission(PurchasingPermissions.InvoiceCreate)]
    [ProducesResponseType<PurchaseInvoiceDraftSaved>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PurchaseInvoiceDraftSaved>> Create([FromBody] PurchaseInvoiceDraftInput? input,
        CancellationToken token)
    {
        var result = await drafts.CreateAsync(input, token);
        return Created($"/api/v1/pur/invoices/{result.Id}", result);
    }

    /// <summary>Replace an active Draft using its saved version.</summary>
    /// <remarks>Requires PUR.INVOICE.EDIT with company data scope. Missing or cross-company IDs return 404;
    /// a stale version returns 409. The supplier, order and currency identity cannot change.</remarks>
    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.InvoiceEdit)]
    [ProducesResponseType<PurchaseInvoiceDraftSaved>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<PurchaseInvoiceDraftSaved> Edit(long id, [FromQuery] int version,
        [FromBody] PurchaseInvoiceDraftInput? input, CancellationToken token) =>
        drafts.EditAsync(id, version, input, token);

    /// <summary>Match a saved draft with current approved order and posted receipt quantities.</summary>
    /// <remarks>Reserves receipt quantities atomically. Returns 409 for a stale version and 422 for
    /// invalid sources, insufficient quantity, or an ineligible invoice state.</remarks>
    [HttpPost("{id:long}/match")]
    [RequirePermission(PurchasingPermissions.InvoiceMatch)]
    [ProducesResponseType<PurchaseInvoiceMatchSaved>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseInvoiceMatchSaved> Match(long id, [FromQuery] int version,
        CancellationToken token) => matches.MatchAsync(id, version, token);

    /// <summary>Accept or reject each price/tax difference of a disputed invoice.</summary>
    /// <remarks>Requires PUR.INVOICE.RESOLVE with company data scope. Every differing line needs exactly
    /// one Accept/Reject decision with a 1-500 character reason. Accepting all lines returns Matched and
    /// keeps the receipt reservation; any rejection returns Draft and releases it. Returns 409 for a stale
    /// version and 422 for an invalid decision set, invoice state, source, or quantity.</remarks>
    [HttpPost("{id:long}/resolve")]
    [RequirePermission(PurchasingPermissions.InvoiceResolve)]
    [ProducesResponseType<PurchaseInvoiceResolveSaved>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseInvoiceResolveSaved> Resolve(long id, [FromQuery] int version,
        [FromBody] PurchaseInvoiceResolveInput? input, CancellationToken token) =>
        resolutions.ResolveAsync(id, version, input, token);
}
