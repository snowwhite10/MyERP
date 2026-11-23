using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>
/// Sales invoices issued from Posted deliveries of one Approved order (WU-SD-007a3b, ADR-179). ISSUE saves the invoice
/// and posts its FI/AR receivable and GL journal in one transaction; an invoice is never a Draft and is never edited.
/// </summary>
[ApiController]
[Route("api/v1/sal/invoices")]
[Produces("application/json")]
[Tags("SD - Invoices")]
public sealed class SalesInvoicesController(SalesInvoiceIssueService issue, SalesInvoiceQueryService queries)
    : ControllerBase
{
    /// <summary>Issue one invoice and post it to FI/AR and GL in the same transaction.</summary>
    /// <remarks>
    /// Needs SAL.INVOICE.ISSUE; the source order must be in its SYS data scope (otherwise 404
    /// SAL.INVOICE.SOURCE_NOT_FOUND). The body carries only an idempotency <c>requestKey</c> (1–64 letters, digits,
    /// <c>-</c> or <c>_</c>), the invoice date and the Posted delivery lines with base-unit quantities; prices, tax,
    /// customer, currency and amounts come from the saved order. Every line must come from one order
    /// (422 SOURCE_MIXED) and stay within its uninvoiced quantity (422 BALANCE_EXCEEDED, with <c>lineNo</c>).
    /// A new invoice returns 201 with Location; repeating the same key with the same content returns the saved
    /// invoice with 200 and posts nothing again; the same key with other content is 409 SAL.INVOICE.REQUEST_CONFLICT.
    /// FI refusals keep FI's code (for example 422 FI.SALES_POSTING.MAPPING_MISSING, 409 FI.CREDIT.LOCK_TIMEOUT).
    /// An FI fault or an FI receipt that does not match the invoice is 500 with errorCode
    /// SAL.INVOICE.FI_POSTING_FAILED, SAL.INVOICE.FI_RECEIPT_MISMATCH or FI's own code. Any failure saves nothing:
    /// no invoice, number, receivable or journal.
    /// </remarks>
    [HttpPost]
    [RequirePermission(SalesInvoiceIssuing.IssuePermission)]
    [ProducesResponseType<SalesInvoiceDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<SalesInvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SalesInvoiceDetail>> Issue(SalesInvoiceIssueInput request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await issue.IssueAsync(request, token);
        var detail = await queries.DescribeCommittedAsync(outcome.Invoice.Id, token);
        return outcome.Replayed ? Ok(detail) : Created($"/api/v1/sal/invoices/{detail.Id}", detail);
    }

    /// <summary>List Posted invoices whose source order is in the caller's SAL.INVOICE.VIEW data scope.</summary>
    /// <remarks>
    /// Filters: <c>keyword</c> (invoice or order number), <c>from</c>/<c>to</c> invoice date (inclusive),
    /// <c>orderId</c>, <c>customerId</c>. Sort: <c>documentNo</c> or <c>businessDate</c> with optional <c>-</c>
    /// (default <c>-businessDate</c>; the invoice ID breaks ties). Invoices outside the scope are not counted.
    /// </remarks>
    [HttpGet]
    [RequirePermission(SalesInvoiceQueryService.ViewPermission)]
    [ProducesResponseType<PagedResult<SalesInvoiceSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesInvoiceSummary>> List([FromQuery] SalesInvoiceQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>Get a Posted invoice with its lines, source deliveries and FI/AR references.</summary>
    /// <remarks>Needs SAL.INVOICE.VIEW; the source order must be in its SYS data scope, otherwise 404
    /// SAL.INVOICE.NOT_FOUND.</remarks>
    [HttpGet("{id:long}")]
    [RequirePermission(SalesInvoiceQueryService.ViewPermission)]
    [ProducesResponseType<SalesInvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesInvoiceDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);
}
