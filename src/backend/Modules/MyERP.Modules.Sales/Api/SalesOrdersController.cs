using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Infrastructure.Web;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Api;

/// <summary>Saved sales orders. Each action uses its own SYS permission and data scope.</summary>
[ApiController]
[Route("api/v1/sal/orders")]
[Produces("application/json")]
[Tags("SD - Orders")]
public sealed class SalesOrdersController(SalesOrderQueryService queries,
    SalesOrderCreateService create, SalesOrderConversionService conversion,
    SalesOrderDraftCommandService commands, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>List saved orders in the current company and VIEW data scope with stable pagination.</summary>
    [HttpGet]
    [RequirePermission("SAL.ORDER.VIEW")]
    [ProducesResponseType<PagedResult<SalesOrderSummary>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SalesOrderSummary>> List([FromQuery] SalesOrderHttpQuery request,
        CancellationToken token) => queries.ListAsync(request.ToQuery(), token);

    /// <summary>
    /// Download every saved line of the filtered orders as UTF-8 CSV (with BOM) using the EXPORT
    /// permission and its data scope. Headers and status follow Accept-Language (zh-Hant / zh-Hans / en);
    /// paging is ignored. More than 10,000 lines returns 422 SAL.ORDER.EXPORT_LIMIT before any output.
    /// X-Export-Row-Count carries the written line count (0 = header only).
    /// </summary>
    [HttpGet("export")]
    [RequirePermission("SAL.ORDER.EXPORT")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task Export([FromQuery] SalesOrderHttpQuery request, CancellationToken token)
    {
        var language = ErpProblemDetailsFactory.ResolveLanguage(Request.Headers.AcceptLanguage);
        await queries.ExportAsync(request.ToQuery(), language, Response.Body, count =>
        {
            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = "attachment; filename=\"sales-orders.csv\"";
            Response.Headers["X-Export-Row-Count"] = count.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers.ContentLanguage = language;
            return Task.CompletedTask;
        }, token: token);
    }

    /// <summary>Get a saved order and its lines in the current company and VIEW action scope.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission("SAL.ORDER.VIEW")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesOrderDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

    /// <summary>Read the caller's own saved creation using CREATE permission, including without VIEW.</summary>
    [HttpGet("{id:long}/created")]
    [RequirePermission("SAL.ORDER.CREATE")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesOrderDetail> GetCreated(long id, CancellationToken token) =>
        queries.GetCreatedAsync(id, currentUser.UserId ?? 0, token);

    /// <summary>Create a manual Draft with a permanent SYS number and MD monetary snapshots atomically.</summary>
    [HttpPost]
    [RequirePermission("SAL.ORDER.CREATE")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SalesOrderDetail>> Create(SalesOrderDraftInput request,
        CancellationToken token)
    {
        var saved = await create.CreateAsync(request, token);
        return StatusCode(StatusCodes.Status201Created, SalesOrderQueryService.Map(saved));
    }

    /// <summary>Convert one approved, unexpired saved quotation into an immutable-source Draft.</summary>
    [HttpPost("from-quotation/{quotationId:long}")]
    [RequirePermission("SAL.ORDER.CREATE")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SalesOrderDetail>> ConvertQuotation(long quotationId,
        CancellationToken token)
    {
        var saved = await conversion.CreateAsync(quotationId, token);
        return StatusCode(StatusCodes.Status201Created, SalesOrderQueryService.Map(saved));
    }

    /// <summary>Replace a current-version manual Draft using the EDIT action scope.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission("SAL.ORDER.EDIT")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<SalesOrderDetail> Replace(long id, SalesOrderReplaceRequest request,
        CancellationToken token) => SalesOrderQueryService.Map(
            await commands.ReplaceAsync(id, request.Version, request.Draft, token));

    /// <summary>Soft-delete a current-version Draft using the DELETE action scope.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission("SAL.ORDER.DELETE")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version,
        CancellationToken token)
    {
        await commands.DeleteAsync(id, version, token);
        return NoContent();
    }
}

public sealed record SalesOrderReplaceRequest(int Version, SalesOrderDraftInput Draft);

/// <summary>HTTP status names are stable across enum reordering; numeric query values are rejected.</summary>
public sealed record SalesOrderHttpQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Status { get; init; }
    public long? CustomerId { get; init; }

    public SalesOrderQuery ToQuery()
    {
        SalesOrderStatus? status = null;
        if (!string.IsNullOrEmpty(Status))
        {
            if (!Enum.TryParse<SalesOrderStatus>(Status, false, out var parsed)
                || !Enum.IsDefined(parsed) || !string.Equals(Status, parsed.ToString(), StringComparison.Ordinal))
                throw new RequestValidationException("status", "Choose a named sales order status.");
            status = parsed;
        }
        return new SalesOrderQuery { Page = Page, PageSize = PageSize, Keyword = Keyword,
            Sort = Sort, From = From, To = To, Status = status, CustomerId = CustomerId };
    }
}
