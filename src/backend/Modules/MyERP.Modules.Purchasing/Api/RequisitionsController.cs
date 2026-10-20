using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Purchasing.Api;

/// <summary>REQ_F4114.3.1：請購列表與已保存詳情。</summary>
[ApiController]
[Route("api/v1/pur/requisitions")]
[Produces("application/json")]
[Tags("PUR - Requisitions")]
public sealed class RequisitionsController(RequisitionQueryService queries, RequisitionCreateService creates,
    RequisitionMutationService mutations, RequisitionSubmissionService submissions,
    IApprovalHistoryReader history, IInventorySelectionOptions itemOptions) : ControllerBase
{
    /// <summary>請購建立／修訂只選目前公司啟用的 MD 物料，按代碼穩定分頁。</summary>
    [HttpGet("item-options")]
    [RequireAnyPermission(PurchasingPermissions.RequisitionCreate, PurchasingPermissions.RequisitionEdit)]
    [ProducesResponseType<PagedResult<InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<InventorySelectionOption>> ItemOptions([FromQuery] PagedRequest request,
        CancellationToken token) => itemOptions.ItemsAsync(request, token);

    /// <summary>已保存行的顯示名稱；先通過請購 EDIT 公司與資料範圍，再讀 MD 引用。</summary>
    [HttpGet("{id:long}/saved-item-options")]
    [RequirePermission(PurchasingPermissions.RequisitionEdit)]
    [ProducesResponseType<IReadOnlyList<InventorySelectionOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<InventorySelectionOption>> SavedItemOptions(long id, CancellationToken token)
    {
        var detail = await queries.GetForEditAsync(id, token);
        var references = await itemOptions.SavedAsync(detail.Lines.Select(line => line.ItemId).ToArray(), [], [], token);
        return references.Items;
    }

    /// <summary>供修訂畫面讀取 EDIT 資料範圍內的單據與版本。</summary>
    [HttpGet("{id:long}/edit")]
    [RequirePermission(PurchasingPermissions.RequisitionEdit)]
    [ProducesResponseType<RequisitionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RequisitionDetail> GetForEdit(long id, CancellationToken token) => queries.GetForEditAsync(id, token);
    [HttpGet]
    [RequirePermission(PurchasingPermissions.RequisitionView)]
    [ProducesResponseType<PagedResult<RequisitionSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<RequisitionSummary>> List([FromQuery] RequisitionQuery request,
        CancellationToken token) => queries.ListAsync(request, token);

    [HttpGet("{id:long}")]
    [RequirePermission(PurchasingPermissions.RequisitionView)]
    [ProducesResponseType<RequisitionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<RequisitionDetail> Get(long id, CancellationToken token)
    {
        var document = await queries.GetAsync(id, token);
        return document with { ApprovalHistory = await history.GetHistoryAsync(
            RequisitionSubmissionService.DocumentType, document.Id, token) };
    }

    [HttpPost]
    [RequirePermission(PurchasingPermissions.RequisitionCreate)]
    [ProducesResponseType<RequisitionDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RequisitionDetail>> Create([FromBody] RequisitionInput input, CancellationToken token)
    {
        var result = await creates.CreateAsync(input, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:long}")]
    [RequirePermission(PurchasingPermissions.RequisitionEdit)]
    [ProducesResponseType<RequisitionDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<RequisitionDetail> Update(long id, [FromQuery] int version, [FromBody] RequisitionInput input,
        CancellationToken token) => mutations.UpdateAsync(id, version, input, token);

    [HttpDelete("{id:long}")]
    [RequirePermission(PurchasingPermissions.RequisitionDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await mutations.DeleteAsync(id, version, token);
        return NoContent();
    }

    [HttpPost("{id:long}/submit")]
    [RequirePermission(PurchasingPermissions.RequisitionSubmit)]
    [ProducesResponseType<RequisitionSubmissionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<RequisitionSubmissionResult> Submit(long id, [FromBody] RequisitionSubmitInput input,
        CancellationToken token) => submissions.SubmitAsync(id, input.Version, token);
}

public sealed record RequisitionSubmitInput(int Version);
