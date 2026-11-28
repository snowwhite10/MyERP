using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Infrastructure.Web;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>Saved purchase receipt drafts; stock is posted only after SYS final approval.</summary>
[ApiController]
[Route("api/v1/pur/purchase-receipts")]
public sealed class PurchaseReceiptsController(PurchaseReceiptDraftService drafts,
    PurchaseReceiptSubmissionService submissions, PurchaseReceiptQueryService queries,
    PurchaseReceiptLifecycleService lifecycle) : ControllerBase
{
    /// <summary>
    /// Lists saved receipts in the VIEW data scope. Filters: <c>keyword</c> (receipt or purchase order number),
    /// <c>from</c>／<c>to</c> business date, <c>purchaseOrderId</c>, <c>status</c> (Draft／PendingApproval／Rejected／Posted);
    /// sort <c>documentNo</c> or <c>businessDate</c> (default <c>-businessDate</c>, ID tiebreak).
    /// </summary>
    [HttpGet]
    [RequirePermission(PurchasingPermissions.ReceiptView)]
    [ProducesResponseType<PagedResult<PurchaseReceiptSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<PurchaseReceiptSummary>> List([FromQuery] PurchaseReceiptQuery query,
        CancellationToken token) => queries.ListAsync(query, token);

    /// <summary>Exports every saved line matching the list filters (VIEW ∩ EXPORT scope); max 10000 lines, otherwise 422 and no file.</summary>
    /// <remarks>Same filters and sort as the list; paging is ignored. UTF-8 BOM, CRLF, formula-safe cells;
    /// <c>X-Export-Row-Count</c> is the number of lines written (0 = header only). Print by re-reading <c>GET /{id}</c>.</remarks>
    [HttpGet("export")]
    [RequirePermission(PurchasingPermissions.ReceiptExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task Export([FromQuery] PurchaseReceiptQuery query, CancellationToken token)
    {
        var language = ErpProblemDetailsFactory.ResolveLanguage(Request.Headers.AcceptLanguage);
        await queries.ExportAsync(query, language, Response.Body, count =>
        {
            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = "attachment; filename=\"purchase-receipts.csv\"";
            Response.Headers["X-Export-Row-Count"] = count.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers.ContentLanguage = language;
            return Task.CompletedTask;
        }, token: token);
    }

    /// <summary>
    /// Soft-deletes a Draft receipt with <paramref name="version"/>; number, lines and lot/serial capture are kept.
    /// Stale version 409; PendingApproval／Rejected／Posted 422 <c>PUR.RECEIPT.NOT_DELETABLE</c>; out of scope 404.
    /// </summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReceiptDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await lifecycle.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>Creates a receipt draft from one approved order, with saved warehouse and base cost snapshots.</summary>
    [HttpPost]
    [RequirePermission(PurchasingPermissions.ReceiptCreate)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PurchaseReceiptReadDetail>> Create(PurchaseReceiptSaveInput input,
        CancellationToken token) => StatusCode(StatusCodes.Status201Created,
            await drafts.CreateAsync(input, token));

    /// <summary>Reads a saved inbound document in the VIEW data scope.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReceiptView)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseReceiptReadDetail> Get(long id, CancellationToken token) => drafts.GetAsync(id, token);

    /// <summary>Reads the same saved document in the EDIT data scope.</summary>
    [HttpGet("{id:long}/edit")]
    [RequirePermission(PurchasingPermissions.ReceiptEdit)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseReceiptReadDetail> GetForEdit(long id, CancellationToken token) =>
        drafts.GetForEditAsync(id, token);

    /// <summary>Replaces Draft or Rejected lines after version and locked source validation.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReceiptEdit)]
    [ProducesResponseType<PurchaseReceiptReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseReceiptReadDetail> Update(long id, [FromQuery] int version,
        PurchaseReceiptSaveInput input, CancellationToken token) => drafts.UpdateAsync(id, version, input, token);

    /// <summary>
    /// Submits a saved Draft with body <c>version</c>: one transaction reserves purchase order line capacity from saved
    /// lines and creates the SYS approval instance. Stock is not posted until final approval (WU-PUR-007b).
    /// </summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(PurchasingPermissions.ReceiptSubmit)]
    [ProducesResponseType<PurchaseReceiptSubmissionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseReceiptSubmissionResult> Submit(long id, [FromBody] PurchaseReceiptSubmitInput input,
        CancellationToken token) => submissions.SubmitAsync(id, input.Version, token);
}

/// <summary>SUBMIT request: the currently saved receipt version.</summary>
public sealed record PurchaseReceiptSubmitInput(int Version);
