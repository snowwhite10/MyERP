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
    SalesOrderDraftCommandService commands, SalesOrderSubmissionService submission,
    SalesOrderCancellationService cancellation, ICurrentUser currentUser) : ControllerBase
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

    /// <summary>
    /// Create a manual Draft with a permanent SYS number and MD monetary snapshots atomically. Each line price is
    /// decided again from the price rules in the save transaction: 409 SAL.PRICING.STALE_QUOTE /
    /// DUPLICATE_ACTIVE_RULE, 403 SAL.PRICING.OVERRIDE_REQUIRED (manual price, no rule or discount above the
    /// ceiling), 400 lines[i].overrideReason; pricing errors carry lineNo (ADR-160, WU-SD-005a11b).
    /// </summary>
    [HttpPost]
    [RequirePermission("SAL.ORDER.CREATE")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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

    /// <summary>Replace a current-version manual Draft using the EDIT action scope; every line is priced again as
    /// on create, and an override needs SAL.PRICING.OVERRIDE whose scope covers this order.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission("SAL.ORDER.EDIT")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
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

    /// <summary>
    /// Submit a current-version Draft to SYS approval after the FI/AR credit check (SUBMIT action scope).
    /// Returns the saved order (Pending, or Approved when SYS needs no step) and the credit evaluation used.
    /// </summary>
    /// <remarks>The FI credit key is locked before the order, so approvals of one customer serialize. Over limit is
    /// 422 SAL.CREDIT.LIMIT_EXCEEDED with <c>excess</c> and <c>currency</c>; an unconfirmable credit position is
    /// 422 SAL.CREDIT.*; nothing is saved and no SYS round starts. There is no override before WU-SD-006b.</remarks>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(SalesOrderSubmissionService.Permission)]
    [ProducesResponseType<SalesOrderSubmitResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<SalesOrderSubmitResponse> Submit(long id, SalesOrderVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await submission.SubmitAsync(id, request.Version, token);
        return new SalesOrderSubmitResponse(SalesOrderQueryService.Map(result.Order),
            SalesOrderCreditResult.From(result.Credit));
    }

    /// <summary>
    /// Cancel a current-version Approved order with no Pending or Posted delivery (CANCEL action scope).
    /// Number, source, lines and SYS history are kept; Draft/Rejected/deleted deliveries do not block.
    /// </summary>
    [HttpPost("{id:long}/cancel")]
    [RequirePermission(SalesOrderCancellationService.Permission)]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<SalesOrderDetail> Cancel(long id, SalesOrderVersionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SalesOrderQueryService.Map(await cancellation.CancelAsync(id, request.Version, token));
    }
}

public sealed record SalesOrderReplaceRequest(int Version, SalesOrderDraftInput Draft);

public sealed record SalesOrderVersionRequest(int Version);

/// <summary>Submitted order and the credit evaluation that allowed it (not persisted before WU-SD-006b).</summary>
public sealed record SalesOrderSubmitResponse(SalesOrderDetail Order, SalesOrderCreditResult Credit);

/// <summary>
/// Credit figures in the company base currency evaluated on <see cref="EvaluationDate"/> (server date).
/// Headroom = Limit − PostedOpenAr − OtherCommitments − Candidate; Status is a stable name.
/// </summary>
public sealed record SalesOrderCreditResult(string Status, DateOnly EvaluationDate, string? BaseCurrency,
    decimal? Limit, decimal? PostedOpenAr, decimal? OtherCommitments, decimal Candidate, decimal? Headroom,
    int? LimitVersion)
{
    public static SalesOrderCreditResult From(SalesCreditAssessment credit)
    {
        ArgumentNullException.ThrowIfNull(credit);
        return new(credit.Status.ToString(), credit.EvaluationDate, credit.BaseCurrency, credit.Limit,
            credit.PostedOpenAr, credit.OtherCommitments, credit.Candidate, credit.Headroom, credit.LimitVersion);
    }
}

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
