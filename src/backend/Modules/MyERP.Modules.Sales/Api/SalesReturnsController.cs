using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>
/// Sales returns against Posted deliveries of one Approved order (WU-SD-008a6, ADR-192). Each action uses its own SYS
/// permission and that permission's data scope is checked on the source order; a hidden, foreign, deleted or unknown
/// return is 404 SAL.RETURN.NOT_FOUND. A Draft reserves nothing; SUBMIT sends it to SYS approval and the final approval
/// (or a flow with no step) receives the goods through INV and credits the billed portions through FI in one
/// transaction. There is no client route that posts to INV or FI directly.
/// </summary>
[ApiController]
[Route("api/v1/sal/returns")]
[Produces("application/json")]
[Tags("SD - Returns")]
public sealed class SalesReturnsController(SalesReturnCreateService create, SalesReturnDraftCommandService commands,
    SalesReturnSubmissionService submission, SalesReturnQueryService queries, SalesReturnSourceQueryService sources)
    : ControllerBase
{
    /// <summary>List returns whose source order is in the caller's SAL.RETURN.VIEW data scope.</summary>
    /// <remarks>
    /// Filters: <c>keyword</c> (return or order number, customer name), <c>from</c>/<c>to</c> return date
    /// (inclusive), <c>orderId</c>, <c>customerId</c>, <c>status</c> (Draft, Pending, Rejected, Posted). Sort:
    /// <c>documentNo</c> or <c>businessDate</c> with optional <c>-</c> (default <c>-businessDate</c>; the return ID
    /// breaks ties). Returns outside the scope and deleted Drafts are not counted.
    /// </remarks>
    [HttpGet]
    [RequirePermission(SalesReturnQueryService.ViewPermission)]
    [ProducesResponseType<PagedResult<SalesReturnSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesReturnSummary>> List([FromQuery] SalesReturnQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>List Approved orders that still have returnable Posted delivery lines (return form source picker).</summary>
    /// <remarks>
    /// Needs SAL.RETURN.CREATE; its SYS data scope is applied to the order before counting, as CREATE applies it.
    /// Filters: <c>keyword</c> (order number, customer code or name), <c>customerId</c>. Sort: <c>documentNo</c> or
    /// <c>businessDate</c> with optional <c>-</c> (default <c>-businessDate</c>; the order ID breaks ties).
    /// <c>openLineCount</c> counts untracked delivery lines with a returnable quantity; only Posted returns consume it.
    /// Read only; nothing is locked.
    /// </remarks>
    [HttpGet("source-orders")]
    [RequirePermission(SalesReturnCreateService.CreatePermission)]
    [ProducesResponseType<PagedResult<SalesReturnSourceOrderSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesReturnSourceOrderSummary>> ListSourceOrders(
        [FromQuery] SalesReturnSourceOrderQuery request, CancellationToken token) =>
        sources.ListOrdersAsync(request, token);

    /// <summary>Get one source order with its returnable delivery lines, unbilled balance and creditable invoice lines.</summary>
    /// <remarks>
    /// Needs SAL.RETURN.CREATE or SAL.RETURN.EDIT with the order in one of their SYS data scopes (the edit page reads
    /// its saved order); otherwise, or when the order is missing, deleted or of another company, 404
    /// SAL.RETURN.SOURCE_NOT_FOUND. An order that is not Approved is 422
    /// SAL.RETURN.ORDER_NOT_APPROVED. Each line carries <c>returnableQuantity</c>, <c>unbilledRemainingQuantity</c>, the
    /// Posted invoice lines with <c>remainingQuantity</c> &gt; 0, the delivery warehouse/location as default destination
    /// and <c>trackingUnsupported</c> for lot/serial lines. The balances are advisory: CREATE rechecks them under lock
    /// and may still answer 422 RETURN_EXCEEDED, UNBILLED_EXCEEDED or BILLED_EXCEEDED.
    /// </remarks>
    [HttpGet("source-orders/{orderId:long}")]
    [RequireAnyPermission(SalesReturnCreateService.CreatePermission, SalesReturnDraftCommandService.EditPermission)]
    [ProducesResponseType<SalesReturnSourceOrder>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<SalesReturnSourceOrder> GetSourceOrder(long orderId, CancellationToken token) =>
        sources.GetOrderAsync(orderId, token);

    /// <summary>Active warehouses of the current company as return destinations; code and ID order.</summary>
    /// <remarks>Needs SAL.RETURN.CREATE or EDIT (not MD.WAREHOUSE.VIEW). Keyword matches code or name; sort is
    /// <c>code</c> only. The return CREATE/PUT still revalidates the saved warehouse (MD.WAREHOUSE.INACTIVE etc.).</remarks>
    [HttpGet("warehouse-options")]
    [RequireAnyPermission(SalesReturnCreateService.CreatePermission, SalesReturnDraftCommandService.EditPermission)]
    [ProducesResponseType<PagedResult<InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<InventorySelectionOption>> WarehouseOptions([FromQuery] PagedRequest request,
        CancellationToken token) => sources.WarehousesAsync(request, token);

    /// <summary>Active storage locations of one active warehouse as return destinations.</summary>
    /// <remarks>Needs SAL.RETURN.CREATE or EDIT. An inactive, deleted, unknown or other-company warehouse is 404
    /// MD.WAREHOUSE.NOT_FOUND.</remarks>
    [HttpGet("warehouse-options/{warehouseId:long}/locations")]
    [RequireAnyPermission(SalesReturnCreateService.CreatePermission, SalesReturnDraftCommandService.EditPermission)]
    [ProducesResponseType<PagedResult<InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<InventorySelectionOption>> LocationOptions(long warehouseId,
        [FromQuery] PagedRequest request, CancellationToken token) =>
        sources.LocationsAsync(warehouseId, request, token);

    /// <summary>Get a return with its lines, source deliveries, billed portions, INV movements and FI credit note.</summary>
    /// <remarks>Needs SAL.RETURN.VIEW with the source order in its data scope; otherwise 404 SAL.RETURN.NOT_FOUND.</remarks>
    [HttpGet("{id:long}")]
    [RequirePermission(SalesReturnQueryService.ViewPermission)]
    [ProducesResponseType<SalesReturnDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesReturnDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

    /// <summary>Create a Draft return from Posted delivery lines of one order; the server assigns the SYS number.</summary>
    /// <remarks>
    /// Needs SAL.RETURN.CREATE with the source order in its data scope (otherwise 404 SAL.RETURN.SOURCE_NOT_FOUND).
    /// The body carries an idempotency <c>requestKey</c> (1–64 letters, digits, <c>-</c> or <c>_</c>), the return
    /// date, an optional remark and lines: delivery line, destination warehouse/location and the returned quantity
    /// split into <c>unbilledQuantity</c> and <c>billed</c> invoice-line portions. Customer, currency, prices and
    /// amounts come from the saved order and invoices. Line refusals (422 SOURCE_MIXED, DELIVERY_NOT_POSTED,
    /// INVOICE_LINE_INVALID, DATE_BEFORE_DELIVERY, DATE_BEFORE_INVOICE, TRACKING_UNSUPPORTED, RETURN_EXCEEDED,
    /// UNBILLED_EXCEEDED, BILLED_EXCEEDED) carry <c>lineNo</c>; balance refusals also carry the remaining quantity.
    /// A new return is 201 with Location; the same key with the same content returns the saved return with 200; the
    /// same key with other content, a deleted return or a hidden order is 409 SAL.RETURN.REQUEST_CONFLICT.
    /// </remarks>
    [HttpPost]
    [RequirePermission(SalesReturnCreateService.CreatePermission)]
    [ProducesResponseType<SalesReturnDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<SalesReturnDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SalesReturnDetail>> Create(SalesReturnDraftInput request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await create.CreateAsync(request, token);
        var detail = await queries.DescribeCommittedAsync(outcome.Return.Id, token);
        return outcome.Replayed ? Ok(detail) : Created($"/api/v1/sal/returns/{detail.Id}", detail);
    }

    /// <summary>Replace the date, remark and every line of a current-version Draft or Rejected return.</summary>
    /// <remarks>
    /// Needs SAL.RETURN.EDIT. The creation checks and amount snapshot run again; number, order and currency stay.
    /// <c>draft.requestKey</c> must equal the saved key (422 SAL.RETURN.REQUEST_KEY_IMMUTABLE). A Rejected return is
    /// revised back to Draft (its version advances by two); Pending and Posted are 422 SAL.RETURN.INVALID_STATUS and
    /// a stale version is 409.
    /// </remarks>
    [HttpPut("{id:long}")]
    [RequirePermission(SalesReturnDraftCommandService.EditPermission)]
    [ProducesResponseType<SalesReturnDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesReturnDetail> Replace(long id, SalesReturnReplaceRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var saved = await commands.UpdateAsync(id, request.Version, request.Draft ?? EmptyDraft, token);
        return await queries.DescribeAsync(saved, token);
    }

    /// <summary>Soft-delete a current-version Draft return.</summary>
    /// <remarks>Needs SAL.RETURN.DELETE. Pending, Rejected and Posted returns are 422 SAL.RETURN.INVALID_STATUS; a
    /// stale version is 409. The number and request key stay used.</remarks>
    [HttpDelete("{id:long}")]
    [RequirePermission(SalesReturnDraftCommandService.DeletePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await commands.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>Submit a current-version Draft return to SYS approval.</summary>
    /// <remarks>
    /// Needs SAL.RETURN.SUBMIT. Every saved line is rechecked under the FI credit, order, return, delivery-line and
    /// invoice-line locks (422 balance codes with <c>lineNo</c>; 409 SAL.RETURN.SNAPSHOT_STALE when the saved amounts
    /// no longer match). With an applicable flow the return becomes Pending. With no applicable step it is posted in
    /// the same transaction and returned as Posted: INV receives the goods (409 SAL.RETURN.INV_POSTING_CONFLICT when
    /// INV already holds a different posting), FI credits the billed portions (FI refusals keep FI's code) and 500
    /// carries errorCode SAL.RETURN.FI_POSTING_FAILED, SAL.RETURN.FI_RECEIPT_MISMATCH or FI's own code. Any refusal
    /// saves nothing and the return stays Draft. Rejected returns must be edited (PUT) before they are submitted again.
    /// </remarks>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(SalesReturnSubmissionService.Permission)]
    [ProducesResponseType<SalesReturnDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<SalesReturnDetail> Submit(long id, SalesReturnVersionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await submission.SubmitAsync(id, request.Version, token);
        return await queries.DescribeAsync(result.Return, token);
    }

    // A missing draft fails the same field validation as an empty one (400 requestKey/businessDate/lines).
    private static readonly SalesReturnDraftInput EmptyDraft = new(null, default, null, null);
}

/// <summary>PUT body: the version the client read and the replacement Draft content.</summary>
public sealed record SalesReturnReplaceRequest(int Version, SalesReturnDraftInput? Draft);

/// <summary>SUBMIT body: the version the client read.</summary>
public sealed record SalesReturnVersionRequest(int Version);
