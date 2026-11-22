using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Sales.Api;

/// <summary>Read-only customer credit check (WU-SD-006b3, BR-SD-035, ADR-170).</summary>
[ApiController]
[Route("api/v1/sal/credit-checks")]
[Produces("application/json")]
[Tags("SD - Credit")]
public sealed class SalesCreditChecksController(SalesCreditCheckService checks) : ControllerBase
{
    /// <summary>
    /// Current credit position of a customer in company base currency, optionally with one saved order as the
    /// candidate. Advisory only: it saves nothing and the order submit / SYS final approval recheck it.
    /// </summary>
    /// <remarks>Without <c>orderId</c> it needs SAL.CREDIT.VIEW; with <c>orderId</c> SAL.CREDIT.VIEW or
    /// SAL.ORDER.CREATE/EDIT/SUBMIT whose data scope covers that order. The evaluation date is the server's today.
    /// <c>headroom</c> = <c>limit</c> − <c>postedOpenAr</c> − <c>otherCommitments</c> (Approved orders) −
    /// <c>candidate</c>; <c>status</c> OverLimit carries <c>reasonCode</c> SAL.CREDIT.LIMIT_EXCEEDED and
    /// <c>excess</c>. No FI document detail is returned. 400 for a non-positive or malformed ID; 403 without any of
    /// the permissions; 404 MD.CUSTOMER.NOT_FOUND / SAL.ORDER.NOT_FOUND for another company, out-of-scope or
    /// another customer's order; 409 FI.CREDIT.LOCK_TIMEOUT while credit is being updated (retry); 422
    /// SAL.CREDIT.LIMIT_NOT_SET / UNAVAILABLE / INVALID_SNAPSHOT / INVALID_AMOUNT / CUSTOMER_INACTIVE or
    /// SAL.ORDER.BASE_CURRENCY_CHANGED when credit cannot be confirmed.</remarks>
    [HttpGet("{customerId:long}")]
    [RequireAnyPermission(SalesCreditCheckService.ViewPermission, "SAL.ORDER.CREATE", "SAL.ORDER.EDIT",
        "SAL.ORDER.SUBMIT")]
    [ProducesResponseType<SalesCreditCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<SalesCreditCheckResponse> Get(long customerId, [FromQuery] long? orderId,
        CancellationToken token) => SalesCreditCheckResponse.From(await checks.CheckAsync(customerId, orderId, token));
}

/// <summary>
/// Credit check result. <c>requiresRecheck</c> is always true: the figures are a preview and the order command
/// rechecks under the FI credit lock.
/// </summary>
public sealed record SalesCreditCheckResponse(long CustomerId, long? OrderId, string Status, string? ReasonCode,
    DateOnly EvaluationDate, string? BaseCurrency, decimal? Limit, decimal? PostedOpenAr, decimal? OtherCommitments,
    decimal Candidate, decimal? Headroom, decimal? Excess, int? LimitVersion, bool RequiresRecheck)
{
    public static SalesCreditCheckResponse From(SalesCreditCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var a = result.Assessment;
        return new(result.CustomerId, result.OrderId, a.Status.ToString(), a.ErrorCode, a.EvaluationDate,
            a.BaseCurrency, a.Limit, a.PostedOpenAr, a.OtherCommitments, a.Candidate, a.Headroom, a.Excess,
            a.LimitVersion, true);
    }
}
