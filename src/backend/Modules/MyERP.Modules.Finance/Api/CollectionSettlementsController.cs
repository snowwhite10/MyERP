using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Finance.Api;

/// <summary>
/// Collection settlements (REQ_F4116.5.1–5.8, WU-FI-005b4b, ADR-130). A settlement allocates part of a posted receipt's
/// unapplied cash to open invoice receivables of the same customer. It is saved as Draft without any GL or AR effect;
/// submit enters the SYS <c>AR.SETTLEMENT</c> workflow and the final SYS approval posts it in the same transaction
/// (debit unapplied cash, credit receivable). Approve and reject are SYS routes
/// (<c>/api/v1/sys/approvals/{id}/approve|reject</c>). Customer, currency, total, number, status and actor are
/// server-derived. Lines that cannot be settled return 422 <c>FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID</c> with
/// <c>lineErrors</c> (<c>[{line, receivableId, reason}]</c>, BR-FI-039/040); nothing is saved.
/// </summary>
[ApiController]
[Route("api/v1/ar/collection-settlements")]
[Produces("application/json")]
[Tags("FI - Collection settlements")]
public sealed class CollectionSettlementsController(CollectionSettlementQueryService queries,
    CollectionSettlementDocumentService documents, CollectionSettlementSubmissionService workflow,
    OpenReceivableQueryService openReceivables) : ControllerBase
{
    /// <summary>List current-company settlements (deleted ones excluded) with stable pagination.</summary>
    /// <remarks>Filters: customerId, receiptId (receipt document ID), status (Draft/Pending/Rejected/Approved/Posted),
    /// from/to (inclusive business dates), keyword (document number). Sort: one of businessDate (default descending),
    /// documentNo, totalAmount.</remarks>
    [HttpGet]
    [RequirePermission(CollectionSettlementDocumentService.ViewPermission)]
    [ProducesResponseType<PagedResult<CollectionSettlementSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<CollectionSettlementSummary>> List([FromQuery] CollectionSettlementQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>List one customer's current-company posted receivables that still have an open amount, for choosing
    /// settlement lines (REQ_F4116.5.9, WU-FI-005c2a).</summary>
    /// <remarks>customerId is required (400 when missing or not positive). postedOnOrBefore (inclusive) keeps receivables
    /// that may be settled on that business date. Sort: one of postingDate (default ascending, oldest first) or
    /// openAmount. Amounts are company base currency; drafts reserve nothing, so the open amount is the committed
    /// balance.</remarks>
    [HttpGet("open-receivables")]
    [RequirePermission(CollectionSettlementDocumentService.ViewPermission)]
    [ProducesResponseType<PagedResult<OpenReceivableSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<OpenReceivableSummary>> OpenReceivables([FromQuery] OpenReceivableQuery request,
        CancellationToken token) => openReceivables.ListAsync(request, token);

    /// <summary>Read one settlement with its lines (in receivable ID order) and each receivable's current open amount;
    /// a deleted or other-company id is 404 FI.COLLECTION.SETTLEMENT_NOT_FOUND.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(CollectionSettlementDocumentService.ViewPermission)]
    [ProducesResponseType<CollectionSettlementDocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CollectionSettlementDocumentDetail> Get(long id, CancellationToken token) =>
        documents.GetAsync(id, token);

    /// <summary>Save a Draft settlement. 400 lists every invalid field (lines as <c>lines[i].receivableId</c>/
    /// <c>lines[i].amount</c>); 422 when the receipt is not posted, belongs to another customer or is dated later, its
    /// unapplied amount is insufficient, any line cannot be settled (<c>lineErrors</c>), or the SYS number rule is
    /// missing (SYS.NUMBER_RULE.NOT_CONFIGURED).</summary>
    [HttpPost]
    [RequirePermission(CollectionSettlementDocumentService.CreatePermission)]
    [ProducesResponseType<CollectionSettlementDocumentDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CollectionSettlementDocumentDetail>> Create(CollectionSettlementDocumentInput request,
        CancellationToken token)
    {
        var saved = await documents.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
    }

    /// <summary>Replace a current-version Draft or Rejected settlement (lines replaced as a whole); a Rejected one returns
    /// to Draft. Stale version is 409 COMMON.CONCURRENCY.CONFLICT; Pending/Approved/Posted is 422
    /// FI.COLLECTION.SETTLEMENT_INVALID_STATUS; same 400/422 rules as create.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(CollectionSettlementDocumentService.EditPermission)]
    [ProducesResponseType<CollectionSettlementDocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionSettlementDocumentDetail> Update(long id, CollectionSettlementUpdateRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return documents.ReplaceAsync(id, request.Version,
            new(request.ReceiptId, request.BusinessDate, request.Lines, request.Remark), token);
    }

    /// <summary>Soft delete a current-version Draft or Rejected settlement; the number stays used. Same 404/409/422 rules
    /// as update.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(CollectionSettlementDocumentService.DeletePermission)]
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
    /// approves at once (no applicable step). Receipt and lines are re-checked (422, <c>lineErrors</c>) before SYS is
    /// called.</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(CollectionSettlementSubmissionService.SubmitPermission)]
    [ProducesResponseType<CollectionSettlementWorkflowResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionSettlementWorkflowResult> Submit(long id, CollectionSettlementVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return workflow.SubmitAsync(id, request.Version, token);
    }

    /// <summary>Withdraw a current-version Pending settlement: SYS cancels the round and the settlement returns to
    /// Draft.</summary>
    [HttpPost("{id:long}/withdraw")]
    [RequirePermission(CollectionSettlementSubmissionService.SubmitPermission)]
    [ProducesResponseType<CollectionSettlementWorkflowResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionSettlementWorkflowResult> Withdraw(long id, CollectionSettlementVersionRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return workflow.WithdrawAsync(id, request.Version, token);
    }
}

/// <summary>Replacement settlement fields plus the version last read by the client.</summary>
public sealed record CollectionSettlementUpdateRequest(int Version, long ReceiptId, DateOnly BusinessDate,
    IReadOnlyList<CollectionSettlementLineInput>? Lines, string? Remark);

public sealed record CollectionSettlementVersionRequest(int Version);
