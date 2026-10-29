using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>Saved sales quotations. Each operation checks its own SYS permission and data scope.</summary>
[ApiController]
[Route("api/v1/sal/quotations")]
[Produces("application/json")]
[Tags("SD - Quotations")]
public sealed class SalesQuotationsController(SalesQuotationQueryService queries,
    SalesQuotationCreateService create, SalesQuotationDraftCommandService commands,
    SalesQuotationSubmissionService submission, SalesQuotationVoidService voids) : ControllerBase
{
    /// <summary>List quotations in the current company and VIEW scope with stable pagination.</summary>
    [HttpGet]
    [RequirePermission("SAL.QUOTATION.VIEW")]
    [ProducesResponseType<PagedResult<SalesQuotationSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<SalesQuotationSummary>> List([FromQuery] SalesQuotationQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>Read a quotation and its saved commercial snapshots.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission("SAL.QUOTATION.VIEW")]
    [ProducesResponseType<SalesQuotationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesQuotationDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

    /// <summary>Create an atomic Draft with a permanent SYS number and MD snapshots.</summary>
    [HttpPost]
    [RequirePermission("SAL.QUOTATION.CREATE")]
    [ProducesResponseType<SalesQuotationDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SalesQuotationDetail>> Create(SalesQuotationInput request,
        CancellationToken token)
    {
        var saved = await create.CreateAsync(request, token);
        return StatusCode(StatusCodes.Status201Created, SalesQuotationQueryService.Map(saved));
    }

    /// <summary>Replace a current-version Draft or Rejected quotation using the EDIT scope.</summary>
    [HttpPut("{id:long}")]
    [RequirePermission("SAL.QUOTATION.EDIT")]
    [ProducesResponseType<SalesQuotationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesQuotationDetail> Replace(long id, SalesQuotationReplaceRequest request,
        CancellationToken token) => SalesQuotationQueryService.Map(
            await commands.ReplaceAsync(id, request.Version, request.Draft, token));

    /// <summary>Soft-delete a current-version Draft; permanent numbering is retained.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission("SAL.QUOTATION.DELETE")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version,
        CancellationToken token)
    {
        await commands.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>Submit a current-version Draft to SYS approval in the same transaction.</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission("SAL.QUOTATION.SUBMIT")]
    [ProducesResponseType<SalesQuotationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesQuotationDetail> Submit(long id, SalesQuotationVersionRequest request,
        CancellationToken token) => SalesQuotationQueryService.Map(
            (await submission.SubmitAsync(id, request.Version, token)).Quotation);

    /// <summary>Void a current-version Approved quotation using the separate VOID permission.</summary>
    [HttpPost("{id:long}/void")]
    [RequirePermission("SAL.QUOTATION.VOID")]
    [ProducesResponseType<SalesQuotationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SalesQuotationDetail> Void(long id, SalesQuotationVersionRequest request,
        CancellationToken token) => SalesQuotationQueryService.Map(
            await voids.VoidAsync(id, request.Version, token));
}

public sealed record SalesQuotationReplaceRequest(int Version, SalesQuotationInput Draft);
public sealed record SalesQuotationVersionRequest(int Version);
