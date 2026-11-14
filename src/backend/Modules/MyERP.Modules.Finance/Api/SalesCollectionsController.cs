using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Sales collection receipts (REQ_F4116.1.1–3/1.11/1.12, WU-FI-005b4a, ADR-129). A receipt is saved as Draft in the
/// company base currency without any GL or AR effect; submit enters the SYS <c>AR.COLLECTION</c> workflow and the final
/// SYS approval posts it in the same transaction (debit bank, credit unapplied cash). Approve and reject are SYS routes
/// (<c>/api/v1/sys/approvals/{id}/approve|reject</c>); there is no public posting route. Company, currency, number,
/// status and actor are server-derived.
/// </summary>
[ApiController]
[Route("api/v1/ar/sales-collections")]
[Produces("application/json")]
[Tags("FI - Sales collections")]
public sealed class SalesCollectionsController(CollectionReceiptQueryService queries,
    CollectionReceiptDocumentService documents, CollectionReceiptSubmissionService workflow) : ControllerBase
{
    /// <summary>List current-company receipts (deleted ones excluded) with stable pagination.</summary>
    /// <remarks>Filters: customerId, bankAccountId, status (Draft/Pending/Rejected/Approved/Posted), from/to
    /// (inclusive business dates), keyword (document number or bank reference). Sort: one of businessDate (default
    /// descending), documentNo, amount. <c>unappliedAmount</c> is null until Posted.</remarks>
    [HttpGet]
    [RequirePermission(CollectionReceiptDocumentService.ViewPermission)]
    [ProducesResponseType<PagedResult<CollectionReceiptSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<CollectionReceiptSummary>> List([FromQuery] CollectionReceiptQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>Read one receipt; a deleted or other-company id is 404 FI.COLLECTION.RECEIPT_NOT_FOUND. Posted
    /// receipts include the current unapplied amount and the GL journal id.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(CollectionReceiptDocumentService.ViewPermission)]
    [ProducesResponseType<CollectionReceiptDocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CollectionReceiptDocumentDetail> Get(long id, CancellationToken token) =>
        documents.GetAsync(id, token);

    /// <summary>Save a Draft receipt. 400 lists every invalid field; 409 FI.COLLECTION.BANK_REFERENCE_DUPLICATE when
    /// the fund account already has the bank reference; 422 for an unapproved customer, missing/inactive fund account,
    /// a fund account not in the base currency, or a missing SYS number rule (SYS.NUMBER_RULE.NOT_CONFIGURED).</summary>
    [HttpPost]
    [RequirePermission(CollectionReceiptDocumentService.CreatePermission)]
    [ProducesResponseType<CollectionReceiptDocumentDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CollectionReceiptDocumentDetail>> Create(CollectionReceiptDocumentInput request,
        CancellationToken token)
    {
        var saved = await documents.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
    }

    /// <summary>Replace a current-version Draft or Rejected receipt; a Rejected one returns to Draft. Stale version is
    /// 409 COMMON.CONCURRENCY.CONFLICT; Pending/Approved/Posted is 422 FI.COLLECTION.RECEIPT_INVALID_STATUS.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(CollectionReceiptDocumentService.EditPermission)]
    [ProducesResponseType<CollectionReceiptDocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionReceiptDocumentDetail> Update(long id, CollectionReceiptUpdateRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return documents.ReplaceAsync(id, request.Version, new(request.CustomerId, request.BusinessDate,
            request.Amount, request.BankAccountId, request.BankReference, request.Remark), token);
    }

    /// <summary>Soft delete a current-version Draft or Rejected receipt; the number stays used and the bank reference
    /// is released. Same 404/409/422 rules as update.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(CollectionReceiptDocumentService.DeletePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await documents.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>Submit a current-version Draft to SYS. Returns Pending, or Posted with the posting result when SYS
    /// approves at once (no applicable step). MD references are re-validated (422) before SYS is called.</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(CollectionReceiptSubmissionService.SubmitPermission)]
    [ProducesResponseType<CollectionReceiptWorkflowResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionReceiptWorkflowResult> Submit(long id, CollectionReceiptVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return workflow.SubmitAsync(id, request.Version, token);
    }

    /// <summary>Withdraw a current-version Pending receipt: SYS cancels the round and the receipt returns to Draft.</summary>
    [HttpPost("{id:long}/withdraw")]
    [RequirePermission(CollectionReceiptSubmissionService.SubmitPermission)]
    [ProducesResponseType<CollectionReceiptWorkflowResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionReceiptWorkflowResult> Withdraw(long id, CollectionReceiptVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return workflow.WithdrawAsync(id, request.Version, token);
    }
}

/// <summary>Replacement receipt fields plus the version last read by the client.</summary>
public sealed record CollectionReceiptUpdateRequest(int Version, long CustomerId, DateOnly BusinessDate,
    decimal Amount, long BankAccountId, string? BankReference, string? Remark);

public sealed record CollectionReceiptVersionRequest(int Version);
