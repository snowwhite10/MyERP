using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Api;

/// <summary>Saved sales orders. Each action uses its own SYS permission and data scope.</summary>
[ApiController]
[Route("api/v1/sal/orders")]
[Produces("application/json")]
[Tags("SD - Orders")]
public sealed class SalesOrdersController(SalesOrderQueryService queries,
    SalesOrderCreateService create, SalesOrderDraftCommandService commands) : ControllerBase
{
    /// <summary>List saved orders in the current company and VIEW data scope with stable pagination.</summary>
    [HttpGet]
    [RequirePermission("SAL.ORDER.VIEW")]
    [ProducesResponseType<PagedResult<SalesOrderSummary>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SalesOrderSummary>> List([FromQuery] SalesOrderQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    /// <summary>Get a saved order and its lines in the current company and VIEW action scope.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission("SAL.ORDER.VIEW")]
    [ProducesResponseType<SalesOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SalesOrderDetail> Get(long id, CancellationToken token) => queries.GetAsync(id, token);

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
