using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Sales.Api;

/// <summary>Saved sales orders. Each action uses its own SYS permission and data scope.</summary>
[ApiController]
[Route("api/v1/sal/orders")]
[Produces("application/json")]
[Tags("SD - Orders")]
public sealed class SalesOrdersController(SalesOrderQueryService queries,
    SalesOrderCreateService create) : ControllerBase
{
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
}
