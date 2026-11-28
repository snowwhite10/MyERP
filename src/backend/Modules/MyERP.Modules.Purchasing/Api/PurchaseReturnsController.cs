using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Infrastructure.Web;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>
/// Purchase returns (WU-PUR-007c3～c5): LIST／CSV, drafts and SUBMIT. Drafts reference Posted receipt lines of one purchase order, keep the original
/// inventory receipt movement and never reserve quantity or post stock; SUBMIT reserves and final approval posts (007c4).
/// </summary>
[ApiController]
[Route("api/v1/pur/purchase-returns")]
public sealed class PurchaseReturnsController(PurchaseReturnDraftService drafts,
    PurchaseReturnSubmissionService submissions, PurchaseReturnQueryService queries) : ControllerBase
{
    /// <summary>
    /// Lists saved returns in the VIEW data scope. Filters: <c>keyword</c> (return or purchase order number),
    /// <c>from</c>／<c>to</c> business date, <c>purchaseOrderId</c>, <c>status</c> (Draft／PendingApproval／Rejected／Posted);
    /// sort <c>documentNo</c> or <c>businessDate</c> (default <c>-businessDate</c>, ID tiebreak). <c>returnAmount</c> is
    /// base currency: Posted lines at the original receipt cost; until then the receipt unit cost
    /// (<c>amountEstimated</c> = true).
    /// </summary>
    [HttpGet]
    [RequirePermission(PurchasingPermissions.ReturnView)]
    [ProducesResponseType<PagedResult<PurchaseReturnSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<PurchaseReturnSummary>> List([FromQuery] PurchaseReturnQuery query,
        CancellationToken token) => queries.ListAsync(query, token);

    /// <summary>Exports every saved line matching the list filters (VIEW ∩ EXPORT scope); max 10000 lines, otherwise 422
    /// <c>PUR.RETURN.EXPORT_LIMIT</c> and no file.</summary>
    /// <remarks>Same filters and sort as the list; paging is ignored. UTF-8 BOM, CRLF, formula-safe cells, three-language
    /// header／status／cost source; <c>X-Export-Row-Count</c> is the number of lines written (0 = header only).
    /// Lots/serials are not exported; print by re-reading <c>GET /{id}</c>.</remarks>
    [HttpGet("export")]
    [RequirePermission(PurchasingPermissions.ReturnExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task Export([FromQuery] PurchaseReturnQuery query, CancellationToken token)
    {
        var language = ErpProblemDetailsFactory.ResolveLanguage(Request.Headers.AcceptLanguage);
        await queries.ExportAsync(query, language, Response.Body, count =>
        {
            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = "attachment; filename=\"purchase-returns.csv\"";
            Response.Headers["X-Export-Row-Count"] = count.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers.ContentLanguage = language;
            return Task.CompletedTask;
        }, token: token);
    }

    /// <summary>
    /// Posted receipt lines of <paramref name="purchaseOrderId"/> that still have returnable quantity, with saved
    /// lots/serials (max 1000). Needs CREATE or EDIT; the quantity is advisory and rechecked when saving.
    /// </summary>
    [HttpGet("source-lines")]
    [RequireAnyPermission(PurchasingPermissions.ReturnCreate, PurchasingPermissions.ReturnEdit)]
    [ProducesResponseType<IReadOnlyList<PurchaseReturnSourceLine>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyList<PurchaseReturnSourceLine>> SourceLines([FromQuery] long purchaseOrderId,
        CancellationToken token) => drafts.SourceLinesAsync(purchaseOrderId, token);

    /// <summary>
    /// Creates a Draft. Lines over the returnable quantity, from other orders or unposted receipts, without the original
    /// inventory receipt, or with lots/serials not captured on the receipt line are 422 with <c>lineNos</c>; nothing is saved.
    /// </summary>
    [HttpPost]
    [RequirePermission(PurchasingPermissions.ReturnCreate)]
    [ProducesResponseType<PurchaseReturnReadDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PurchaseReturnReadDetail>> Create(PurchaseReturnSaveInput input,
        CancellationToken token) => StatusCode(StatusCodes.Status201Created, await drafts.CreateAsync(input, token));

    /// <summary>Reads a saved return in the VIEW data scope, with original receipt lines and current returnable quantity.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReturnView)]
    [ProducesResponseType<PurchaseReturnReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseReturnReadDetail> Get(long id, CancellationToken token) => drafts.GetAsync(id, token);

    /// <summary>Reads the same saved return in the EDIT data scope.</summary>
    [HttpGet("{id:long}/edit")]
    [RequirePermission(PurchasingPermissions.ReturnEdit)]
    [ProducesResponseType<PurchaseReturnReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PurchaseReturnReadDetail> GetForEdit(long id, CancellationToken token) =>
        drafts.GetForEditAsync(id, token);

    /// <summary>
    /// Replaces the lines of a Draft or Rejected return with <paramref name="version"/>; the result is Draft. Stale version
    /// 409; PendingApproval／Posted 422 <c>PUR.RETURN.STATUS_NOT_EDITABLE</c>; another order 422 <c>PUR.RETURN.SOURCE_LOCKED</c>.
    /// </summary>
    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReturnEdit)]
    [ProducesResponseType<PurchaseReturnReadDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseReturnReadDetail> Update(long id, [FromQuery] int version, PurchaseReturnSaveInput input,
        CancellationToken token) => drafts.UpdateAsync(id, version, input, token);

    /// <summary>Soft-deletes a Draft with <paramref name="version"/>; other statuses 422 <c>PUR.RETURN.NOT_DELETABLE</c>.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(PurchasingPermissions.ReturnDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await drafts.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>
    /// Submits a Draft with body <c>version</c>: one transaction rechecks the returnable and uninvoiced quantities,
    /// requires complete lot/serial capture, reserves the quantity on the receipt lines and creates the SYS approval.
    /// Final approval (or a zero-step flow) issues stock through INV at the original receipt cost; rejection releases.
    /// Over the returnable quantity 422 <c>PUR.RETURN.QUANTITY_EXCEEDED</c>; invoiced quantity 422
    /// <c>PUR.RETURN.INVOICED_QUANTITY_EXCEEDED</c>; incomplete capture 422 <c>PUR.RETURN.TRACKING_REQUIRED</c>.
    /// </summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(PurchasingPermissions.ReturnSubmit)]
    [ProducesResponseType<PurchaseReturnSubmissionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PurchaseReturnSubmissionResult> Submit(long id, [FromBody] PurchaseReturnSubmitInput input,
        CancellationToken token) => submissions.SubmitAsync(id, input.Version, token);
}

/// <summary>SUBMIT request: the currently saved return version.</summary>
public sealed record PurchaseReturnSubmitInput(int Version);
