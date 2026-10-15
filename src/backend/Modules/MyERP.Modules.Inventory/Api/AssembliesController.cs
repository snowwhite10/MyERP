using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

public sealed record AssemblyLineRequest(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantity,
    IReadOnlyList<InventoryTrackingRequest>? Tracking = null);
public sealed record CreateAssemblyRequest(DateOnly BusinessDate, string? Remark,
    AssemblyLineRequest? Finished, IReadOnlyList<AssemblyLineRequest>? Components);
public sealed record UpdateAssemblyRequest(DateOnly BusinessDate, string? Remark,
    AssemblyLineRequest? Finished, IReadOnlyList<AssemblyLineRequest>? Components, int Version);
public sealed record AssemblyLineResponse(long Id, int LineNo, AssemblyLineRole Role, long ItemId,
    long WarehouseId, long? LocationId, decimal Quantity, decimal? PostedUnitCost,
    IReadOnlyList<InventoryTrackingResponse> Tracking);
public sealed record AssemblyApprovalResponse(long InstanceId, ApprovalStatus Status, int? CurrentStepNo, int StepCount);
public sealed record AssemblyResponse(long Id, string DocumentNo, DateOnly BusinessDate,
    InventoryDocumentStatus Status, string? Remark, DateTime? PostedAt, DateTime CreatedAt, int Version,
    IReadOnlyList<AssemblyLineResponse> Lines, AssemblyApprovalResponse? Approval);

/// <summary>REQ_F4115.4.1–3/6：組裝草稿與查詢。</summary>
[ApiController]
[Route("api/v1/inv/assemblies")]
[Produces("application/json")]
[Tags("INV - Assemblies")]
public sealed class AssembliesController(AssemblyService assemblies, IInventorySelectionOptions references,
    IApprovalWorkflowService workflow) : ControllerBase
{
    [HttpGet]
    [RequirePermission(InventoryPermissions.AssemblyView)]
    [ProducesResponseType<PagedResult<AssemblySummary>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AssemblySummary>> List([FromQuery] AssemblyQuery request, CancellationToken token) =>
        assemblies.ListAsync(request, token);

    [HttpGet("{id:long}")]
    [RequirePermission(InventoryPermissions.AssemblyView)]
    [ProducesResponseType<AssemblyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AssemblyResponse> Get(long id, CancellationToken token) =>
        await Map(await assemblies.GetAsync(id, token), token);

    [HttpGet("{id:long}/saved-references")]
    [RequirePermission(InventoryPermissions.AssemblyView)]
    [ProducesResponseType<InventorySavedReferences>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<InventorySavedReferences> SavedReferences(long id, CancellationToken token)
    {
        var lines = (await assemblies.GetAsync(id, token)).Lines;
        return await references.SavedAsync(lines.Select(x => x.ItemId).ToArray(),
            lines.Select(x => x.WarehouseId).ToArray(),
            lines.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).ToArray(), token);
    }

    [HttpPost]
    [RequirePermission(InventoryPermissions.AssemblyCreate)]
    [ProducesResponseType<AssemblyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AssemblyResponse>> Create(CreateAssemblyRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var assembly = await assemblies.CreateAsync(Input(request.BusinessDate, request.Remark, request.Finished, request.Components), token);
        return StatusCode(StatusCodes.Status201Created, await Map(assembly, token));
    }

    [HttpPut("{id:long}")]
    [RequirePermission(InventoryPermissions.AssemblyEdit)]
    [ProducesResponseType<AssemblyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<AssemblyResponse> Update(long id, UpdateAssemblyRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await Map(await assemblies.UpdateAsync(id,
            Input(request.BusinessDate, request.Remark, request.Finished, request.Components), request.Version, token), token);
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(InventoryPermissions.AssemblyDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await assemblies.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static AssemblyInput Input(DateOnly date, string? remark, AssemblyLineRequest? finished,
        IReadOnlyList<AssemblyLineRequest>? components) => new(date, remark, Line(finished),
            components?.Select(Line).ToList()!);
    private static AssemblyLineInput? Line(AssemblyLineRequest? line) => line is null ? null :
        new(line.LineNo, line.ItemId, line.WarehouseId, line.LocationId, line.Quantity,
            InventoryTrackingRequest.ToLines(line.Tracking));

    private async Task<AssemblyResponse> Map(Assembly assembly, CancellationToken token)
    {
        var state = await workflow.GetStateAsync(AssemblyService.DocumentType, assembly.Id, token);
        return new AssemblyResponse(assembly.Id, assembly.DocumentNo, assembly.BusinessDate, assembly.Status,
            assembly.Remark, assembly.PostedAt, assembly.CreatedAt, assembly.Version,
            [.. assembly.Lines.OrderBy(x => x.LineNo).Select(x => new AssemblyLineResponse(x.Id, x.LineNo,
                x.Role, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, x.PostedUnitCost,
                [.. x.Tracking.OrderBy(y => y.SeqNo).Select(y => new InventoryTrackingResponse(y.LotNo, y.SerialNo,
                    y.Quantity, null, null))]))],
            state is { } a ? new AssemblyApprovalResponse(a.InstanceId, a.Status, a.CurrentStepNo, a.StepCount) : null);
    }
}
