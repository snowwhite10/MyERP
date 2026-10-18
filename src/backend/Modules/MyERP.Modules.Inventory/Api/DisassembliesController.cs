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

public sealed record DisassemblyLineRequest(int LineNo, long ItemId, long WarehouseId, long? LocationId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? AllocationWeight = null,
    IReadOnlyList<InventoryTrackingRequest>? Tracking = null);
public sealed record CreateDisassemblyRequest(DateOnly BusinessDate, string? Remark,
    DisassemblyLineRequest? Finished, IReadOnlyList<DisassemblyLineRequest>? Components);
public sealed record UpdateDisassemblyRequest(DateOnly BusinessDate, string? Remark,
    DisassemblyLineRequest? Finished, IReadOnlyList<DisassemblyLineRequest>? Components, int Version);
public sealed record DisassemblyLineResponse(long Id, int LineNo, DisassemblyLineRole Role, long ItemId,
    long WarehouseId, long? LocationId, decimal Quantity, decimal? AllocationWeight, decimal? PostedUnitCost,
    IReadOnlyList<InventoryTrackingResponse> Tracking);
public sealed record DisassemblyApprovalResponse(long InstanceId, ApprovalStatus Status, int? CurrentStepNo, int StepCount);
public sealed record DisassemblyResponse(long Id, string DocumentNo, DateOnly BusinessDate,
    InventoryDocumentStatus Status, string? Remark, DateTime? PostedAt, DateTime CreatedAt, int Version,
    IReadOnlyList<DisassemblyLineResponse> Lines, DisassemblyApprovalResponse? Approval,
    IReadOnlyList<ApprovalDocumentHistory> ApprovalHistory);

/// <summary>REQ_F4115.4.1–3/6：組裝草稿與查詢。</summary>
[ApiController]
[Route("api/v1/inv/disassemblies")]
[Produces("application/json")]
[Tags("INV - Disassemblies")]
public sealed class DisassembliesController(DisassemblyService disassemblies, IInventorySelectionOptions references,
    IApprovalWorkflowService workflow, IApprovalHistoryReader history) : ControllerBase
{
    [HttpGet]
    [RequirePermission(InventoryPermissions.DisassemblyView)]
    [ProducesResponseType<PagedResult<DisassemblySummary>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<DisassemblySummary>> List([FromQuery] DisassemblyQuery request, CancellationToken token)
    {
        var result = await disassemblies.ListAsync(request, token);
        if (result.Items.Count == 0) return result;
        var saved = await references.SavedAsync(result.Items.Select(x => x.FinishedItemId).Distinct().ToArray(),
            [], [], token);
        var names = saved.Items.ToDictionary(x => x.Id);
        return result with { Items = result.Items.Select(x => names.TryGetValue(x.FinishedItemId, out var item)
            ? x with { FinishedItemCode = item.Code, FinishedItemName = item.Name } : x).ToArray() };
    }

    [HttpGet("{id:long}")]
    [RequirePermission(InventoryPermissions.DisassemblyView)]
    [ProducesResponseType<DisassemblyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DisassemblyResponse> Get(long id, CancellationToken token) =>
        await Map(await disassemblies.GetAsync(id, token), token);

    [HttpGet("{id:long}/saved-references")]
    [RequirePermission(InventoryPermissions.DisassemblyView)]
    [ProducesResponseType<InventorySavedReferences>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<InventorySavedReferences> SavedReferences(long id, CancellationToken token)
    {
        var lines = (await disassemblies.GetAsync(id, token)).Lines;
        return await references.SavedAsync(lines.Select(x => x.ItemId).ToArray(),
            lines.Select(x => x.WarehouseId).ToArray(),
            lines.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).ToArray(), token);
    }

    [HttpPost]
    [RequirePermission(InventoryPermissions.DisassemblyCreate)]
    [ProducesResponseType<DisassemblyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DisassemblyResponse>> Create(CreateDisassemblyRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var disassembly = await disassemblies.CreateAsync(Input(request.BusinessDate, request.Remark, request.Finished, request.Components), token);
        return StatusCode(StatusCodes.Status201Created, await Map(disassembly, token));
    }

    [HttpPut("{id:long}")]
    [RequirePermission(InventoryPermissions.DisassemblyEdit)]
    [ProducesResponseType<DisassemblyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<DisassemblyResponse> Update(long id, UpdateDisassemblyRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await Map(await disassemblies.UpdateAsync(id,
            Input(request.BusinessDate, request.Remark, request.Finished, request.Components), request.Version, token), token);
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(InventoryPermissions.DisassemblyDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await disassemblies.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static DisassemblyInput Input(DateOnly date, string? remark, DisassemblyLineRequest? finished,
        IReadOnlyList<DisassemblyLineRequest>? components) => new(date, remark, Line(finished),
            components?.Select(Line).ToList()!);
    private static DisassemblyLineInput? Line(DisassemblyLineRequest? line) => line is null ? null :
        new(line.LineNo, line.ItemId, line.WarehouseId, line.LocationId, line.Quantity, line.AllocationWeight,
            InventoryTrackingRequest.ToLines(line.Tracking));

    private async Task<DisassemblyResponse> Map(Disassembly disassembly, CancellationToken token)
    {
        var state = await workflow.GetStateAsync(DisassemblyService.DocumentType, disassembly.Id, token);
        return new DisassemblyResponse(disassembly.Id, disassembly.DocumentNo, disassembly.BusinessDate, disassembly.Status,
            disassembly.Remark, disassembly.PostedAt, disassembly.CreatedAt, disassembly.Version,
            [.. disassembly.Lines.OrderBy(x => x.LineNo).Select(x => new DisassemblyLineResponse(x.Id, x.LineNo,
                x.Role, x.ItemId, x.WarehouseId, x.LocationId, x.Quantity, x.AllocationWeight, x.PostedUnitCost,
                [.. x.Tracking.OrderBy(y => y.SeqNo).Select(y => new InventoryTrackingResponse(y.LotNo, y.SerialNo,
                    y.Quantity, null, null))]))],
            state is { } a ? new DisassemblyApprovalResponse(a.InstanceId, a.Status, a.CurrentStepNo, a.StepCount) : null,
            await history.GetHistoryAsync(DisassemblyService.DocumentType, disassembly.Id, token));
    }
}
