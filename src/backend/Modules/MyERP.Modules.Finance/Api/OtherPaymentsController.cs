using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Other payments (REQ_F4116.4.1–4.5, WU-FI-008b3a; BR-FI-059–065): a non-AP expense paid from a company fund account.
/// The document has no supplier, invoice or payable target and never creates an AP payable, allocation or unapplied
/// amount. It is saved as Draft in the company base currency without GL effect; submit enters the SYS
/// <c>AP.OTHER_PAYMENT</c> workflow and the final SYS approval posts it in the same transaction (debit the mapped
/// expense account, credit the fund account's ledger). Approve and reject are SYS routes
/// (<c>/api/v1/sys/approvals/{id}/approve|reject</c>); there is no public posting route. Company, currency, number,
/// status, actor, journal and expense account are server-derived.
/// </summary>
[ApiController]
[Route("api/v1/ap/other-payments")]
[Produces("application/json")]
[Tags("FI - Other payments")]
public sealed class OtherPaymentsController(OtherPaymentQueryService queries, OtherPaymentDocumentService documents,
    OtherPaymentSubmissionService workflow) : ControllerBase
{
    /// <summary>List current-company other payments (deleted ones excluded) with stable pagination.</summary>
    /// <remarks>Filters: status (Draft/Pending/Rejected/Approved/Posted), expenseCategoryId, bankAccountId, from/to
    /// (inclusive business dates), keyword (≤ 50; document number, withdrawal reference or payee). Sort: one of
    /// businessDate (default descending), documentNo, amount. <c>journalId</c> is null until Posted. 400 lists every
    /// invalid filter.</remarks>
    [HttpGet]
    [RequirePermission(OtherPaymentDocumentService.ViewPermission)]
    [ProducesResponseType<PagedResult<OtherPaymentSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<PagedResult<OtherPaymentSummary>> List([FromQuery] OtherPaymentQuery request, CancellationToken token) =>
        queries.ListAsync(request, token);

    /// <summary>Read one other payment; a deleted or other-company id is 404 FI.OTHER_PAYMENT.NOT_FOUND. A Posted
    /// payment includes the committed GL journal id.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(OtherPaymentDocumentService.ViewPermission)]
    [ProducesResponseType<OtherPaymentDocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<OtherPaymentDocumentDetail> Get(long id, CancellationToken token) => documents.GetAsync(id, token);

    /// <summary>Save a Draft other payment. 400 lists every invalid field; 409
    /// FI.OTHER_PAYMENT.BANK_REFERENCE_DUPLICATE when the fund account already has the withdrawal reference (live Draft
    /// or Posted source, case-insensitive); 422 for a missing/inactive/income category, a missing/inactive fund
    /// account, a fund account not in the base currency, a missing base currency, or a missing SYS number rule
    /// (SYS.NUMBER_RULE.NOT_CONFIGURED).</summary>
    [HttpPost]
    [RequirePermission(OtherPaymentDocumentService.CreatePermission)]
    [ProducesResponseType<OtherPaymentDocumentDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OtherPaymentDocumentDetail>> Create(OtherPaymentDocumentInput request,
        CancellationToken token)
    {
        var saved = await documents.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
    }

    /// <summary>Replace a current-version Draft or Rejected other payment; a Rejected one returns to Draft. Stale
    /// version is 409 COMMON.CONCURRENCY.CONFLICT; Pending/Approved/Posted is 422 FI.OTHER_PAYMENT.INVALID_STATUS.
    /// Same 400/409/422 field and reference rules as create.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(OtherPaymentDocumentService.EditPermission)]
    [ProducesResponseType<OtherPaymentDocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<OtherPaymentDocumentDetail> Update(long id, OtherPaymentUpdateRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return documents.ReplaceAsync(id, request.Version, new(request.PayeeDescription, request.ExpenseCategoryId,
            request.BankAccountId, request.BankReference, request.BusinessDate, request.Amount, request.Remark), token);
    }

    /// <summary>Soft delete a current-version Draft or Rejected other payment; the number stays used and the
    /// withdrawal reference is released. Same 404/409/422 rules as update.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(OtherPaymentDocumentService.DeletePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await documents.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>Submit a current-version Draft or Rejected other payment to SYS. Returns Pending, or Posted with the
    /// posting result when SYS approves at once (no applicable step). MD category / fund account / base currency are
    /// re-validated (422) before SYS is called; on an immediate approval every posting rejection keeps its
    /// FI.OTHER_PAYMENT_POSTING.* code (404/409/422) and nothing is saved.</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(OtherPaymentDocumentService.SubmitPermission)]
    [ProducesResponseType<OtherPaymentWorkflowResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<OtherPaymentWorkflowResult> Submit(long id, OtherPaymentVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return workflow.SubmitAsync(id, request.Version, token);
    }

    /// <summary>Withdraw a current-version Pending other payment: SYS cancels the round and the payment returns to
    /// Draft. Any other status is 422 FI.OTHER_PAYMENT.INVALID_STATUS.</summary>
    [HttpPost("{id:long}/withdraw")]
    [RequirePermission(OtherPaymentDocumentService.SubmitPermission)]
    [ProducesResponseType<OtherPaymentWorkflowResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<OtherPaymentWorkflowResult> Withdraw(long id, OtherPaymentVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return workflow.WithdrawAsync(id, request.Version, token);
    }
}

/// <summary>Replacement other payment fields plus the version last read by the client.</summary>
public sealed record OtherPaymentUpdateRequest(int Version, string? PayeeDescription, long ExpenseCategoryId,
    long BankAccountId, string? BankReference, DateOnly BusinessDate, decimal Amount, string? Remark);

public sealed record OtherPaymentVersionRequest(int Version);
