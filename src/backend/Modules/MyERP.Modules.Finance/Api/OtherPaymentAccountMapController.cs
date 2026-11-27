using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Current-company other payment expense account map (WU-FI-008b3b, REQ_F4116.4.2, BR-FI-060): one row per MD expense
/// category, naming the MD expense/cost account an other payment of that category debits. Rows are never deleted, so the
/// resource has only list and per-category create-or-update. The row version is the GL <c>FI/OtherPayment</c> mapping
/// version; changing it does not alter already Posted other payments.
/// </summary>
[ApiController]
[Route("api/v1/gl/other-payment-account-map")]
[Produces("application/json")]
[Tags("FI - GL other payment account map")]
public sealed class OtherPaymentAccountMapController(OtherPaymentAccountMapService maps) : ControllerBase
{
    /// <summary>
    /// List every expense category of the current company (inactive included, income excluded, by code) with its mapped
    /// account and current MD label; an unconfigured category is configured=false, version 0, expenseAccount null.
    /// <c>expenseAccount.isUsable</c> false means posting would be rejected (account inactive, not a leaf or not Expense/Cost).
    /// </summary>
    [HttpGet]
    [RequirePermission(OtherPaymentAccountMapService.ViewPermission)]
    [ProducesResponseType<OtherPaymentAccountMapList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<OtherPaymentAccountMapList> List(CancellationToken token) => maps.ListAsync(token);

    /// <summary>
    /// Create (version 0) or update (saved version) one category's expense account by MD account code. The same account
    /// returns changed=false without a new version. 400 names each invalid field; 404 FI.OTHER_PAYMENT_MAP.CATEGORY_NOT_FOUND
    /// for an unknown, deleted or other-company category; 422 FI.OTHER_PAYMENT_MAP.CATEGORY_INVALID for an income category;
    /// 422 FI.OTHER_PAYMENT_MAP.ACCOUNT_INVALID with <c>reason</c> MD.ACCOUNT.NOT_FOUND／INACTIVE／NOT_POSTABLE or
    /// FI.OTHER_PAYMENT_MAP.NOT_EXPENSE_ACCOUNT (AP control, unapplied clearing, prepaid, bank or any non Expense/Cost
    /// account); 409 FI.OTHER_PAYMENT_MAP.VERSION_CONFLICT for a stale version.
    /// </summary>
    [HttpPut("{expenseCategoryId:long}")]
    [RequirePermission(OtherPaymentAccountMapService.EditPermission)]
    [ProducesResponseType<OtherPaymentAccountMapWriteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<OtherPaymentAccountMapWriteResult> Save(long expenseCategoryId, OtherPaymentAccountMapInput request,
        CancellationToken token) => maps.SaveAsync(expenseCategoryId, request, token);
}
