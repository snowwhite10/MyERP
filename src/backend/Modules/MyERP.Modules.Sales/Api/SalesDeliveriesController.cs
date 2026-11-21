using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>
/// Sales deliveries (outbound) against Approved orders. Each action uses its own SYS permission;
/// the data scope is checked on the source order. Submission and withdrawal reserve and release order balance; posting arrives with 004b.
/// </summary>
[ApiController]
[Route("api/v1/sal/deliveries")]
[Produces("application/json")]
[Tags("SD - Deliveries")]
public sealed class SalesDeliveriesController(SalesDeliveryQueryService queries,
    SalesDeliveryCreateService create, SalesDeliveryDraftCommandService commands,
    SalesDeliverySubmissionService submission, SalesDeliveryTrackingOptionsService trackingOptions,
    SalesDeliveryFormOptionsService formOptions) : ControllerBase
{
    /// <summary>Approved orders the caller may create a delivery from (ADR-155); no amounts.</summary>
    /// <remarks>Needs SAL.DELIVERY.CREATE (not SAL.ORDER.VIEW) and applies its SYS data scope. Keyword matches order
    /// number, customer code or customer name. Sort: one of documentNo, businessDate (default -businessDate); ID breaks
    /// ties. Orders whose lines are fully delivered are still listed; the balance endpoint shows what is left.</remarks>
    [HttpGet("order-options")]
    [RequirePermission("SAL.DELIVERY.CREATE")]
    [ProducesResponseType<PagedResult<SalesDeliveryOrderOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesDeliveryOrderOption>> OrderOptions([FromQuery] PagedRequest request,
        CancellationToken token) => formOptions.OrdersAsync(request, token);

    /// <summary>One source order with each line's advisory delivered, pending and remaining quantity (ADR-155).</summary>
    /// <remarks>Needs SAL.DELIVERY.CREATE or EDIT (not SAL.ORDER.VIEW); the order must be in one of those data scopes,
    /// otherwise 404 SAL.ORDER.NOT_FOUND. Any status is returned so the page can say the order is no longer Approved.
    /// No prices or amounts. The balance is advisory: submission rechecks it under order-line locks.</remarks>
    [HttpGet("order-options/{orderId:long}")]
    [RequireAnyPermission("SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT")]
    [ProducesResponseType<SalesDeliverySourceOrder>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesDeliverySourceOrder> OrderOption(long orderId, CancellationToken token) =>
        formOptions.OrderAsync(orderId, token);

    /// <summary>Active warehouses of the current company for delivery lines (ADR-155); code and ID order.</summary>
    /// <remarks>Needs SAL.DELIVERY.CREATE or EDIT (not MD.WAREHOUSE.VIEW). Keyword matches code or name.</remarks>
    [HttpGet("warehouse-options")]
    [RequireAnyPermission("SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT")]
    [ProducesResponseType<PagedResult<MyERP.Modules.MasterData.Contracts.InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<MyERP.Modules.MasterData.Contracts.InventorySelectionOption>> WarehouseOptions(
        [FromQuery] PagedRequest request, CancellationToken token) => formOptions.WarehousesAsync(request, token);

    /// <summary>Active storage locations of one active warehouse (ADR-155); 404 MD.WAREHOUSE.NOT_FOUND otherwise.</summary>
    [HttpGet("warehouse-options/{warehouseId:long}/locations")]
    [RequireAnyPermission("SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT")]
    [ProducesResponseType<PagedResult<MyERP.Modules.MasterData.Contracts.InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<MyERP.Modules.MasterData.Contracts.InventorySelectionOption>> LocationOptions(long warehouseId,
        [FromQuery] PagedRequest request, CancellationToken token) => formOptions.LocationsAsync(warehouseId, request, token);

    /// <summary>List saved deliveries whose source order is in the current company and VIEW data scope.</summary>
    /// <remarks>Filters: keyword (delivery or order number), from/to business date, status, orderId, customerId.
    /// Sort: one of documentNo, businessDate (default -businessDate); ID breaks ties.</remarks>
    [HttpGet]
    [RequirePermission("SAL.DELIVERY.VIEW")]
    [ProducesResponseType<PagedResult<SalesDeliverySummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesDeliverySummary>> List([FromQuery] SalesDeliveryQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>Get a saved delivery with its lines, source order and order-line references.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission("SAL.DELIVERY.VIEW")]
    [ProducesResponseType<SalesDeliveryDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesDeliveryDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

    /// <summary>Lots or serials of an order line's item with positive stock at one warehouse/location (ADR-152).</summary>
    /// <remarks>For the delivery create/edit pages: needs SAL.DELIVERY.CREATE or EDIT (not INV.TRACKING.VIEW) and the
    /// source order must be in that permission's data scope, otherwise 404 SAL.ORDER.NOT_FOUND. No locationId means
    /// stock without a location. Lots are ordered by earliest expiry (none last), serials by number; at most 100 rows,
    /// <c>truncated</c> asks for a keyword. On-hand stock only: other pending deliveries are not subtracted and INV
    /// rechecks stock on posting. <c>trackingMode</c> None returns no rows; null means the item was deleted.</remarks>
    [HttpGet("tracking-options")]
    [RequireAnyPermission("SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT")]
    [ProducesResponseType<SalesDeliveryTrackingOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesDeliveryTrackingOptions> TrackingOptions([FromQuery] SalesDeliveryTrackingOptionsQuery request,
        CancellationToken token) => trackingOptions.GetAsync(request, token);

    /// <summary>Create a Draft from one Approved order; the server assigns the SYS number and item identity.</summary>
    /// <remarks>Line errors (ORDER_LINE_INVALID, TRACKING_MODE_MISMATCH, SERIAL_DUPLICATE) carry the offending <c>lineNo</c>.
    /// A Draft reserves no order balance and changes no stock.</remarks>
    [HttpPost]
    [RequirePermission("SAL.DELIVERY.CREATE")]
    [ProducesResponseType<SalesDeliveryDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SalesDeliveryDetail>> Create(SalesDeliveryDraftInput request,
        CancellationToken token)
    {
        var saved = await create.CreateAsync(request, token);
        return Created($"/api/v1/sal/deliveries/{saved.Id}", await queries.DescribeAsync(saved, token));
    }

    /// <summary>Replace every line of a current-version Draft or Rejected delivery; it returns to Draft.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission("SAL.DELIVERY.EDIT")]
    [ProducesResponseType<SalesDeliveryDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesDeliveryDetail> Replace(long id, SalesDeliveryReplaceRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var saved = await commands.ReplaceAsync(id, request.Version, request.Draft, token);
        return await queries.DescribeAsync(saved, token);
    }

    /// <summary>Soft-delete a current-version Draft delivery.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission("SAL.DELIVERY.DELETE")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await commands.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>Submit a current-version Draft to SYS approval; the Pending delivery reserves order balance.</summary>
    /// <remarks>Rechecks the full order-line balance under ordered source locks (ORDER_BALANCE_EXCEEDED), MD
    /// references and complete lot/serial selections (TRACKING_REQUIRED with lineNo). A flow with no applicable step
    /// posts the saved lines and their lot/serial selections through INV in the same transaction and returns the
    /// delivery as Posted; an INV refusal (for example 422 INV.STOCK.INSUFFICIENT) or an existing INV posting for the
    /// delivery (409 SAL.DELIVERY.POSTING_CONFLICT) saves nothing and the delivery stays Draft.</remarks>
    [HttpPost("{id:long}/submit")]
    [RequirePermission("SAL.DELIVERY.SUBMIT")]
    [ProducesResponseType<SalesDeliveryDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesDeliveryDetail> Submit(long id, SalesDeliveryVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await submission.SubmitAsync(id, request.Version, token);
        return await queries.DescribeAsync(result.Delivery, token);
    }

    /// <summary>Withdraw a current-version Pending delivery back to Draft and cancel its SYS round.</summary>
    [HttpPost("{id:long}/withdraw")]
    [RequirePermission("SAL.DELIVERY.SUBMIT")]
    [ProducesResponseType<SalesDeliveryDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesDeliveryDetail> Withdraw(long id, SalesDeliveryVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await submission.WithdrawAsync(id, request.Version, token);
        return await queries.DescribeAsync(result.Delivery, token);
    }
}

public sealed record SalesDeliveryReplaceRequest(int Version, SalesDeliveryDraftInput Draft);

public sealed record SalesDeliveryVersionRequest(int Version);
