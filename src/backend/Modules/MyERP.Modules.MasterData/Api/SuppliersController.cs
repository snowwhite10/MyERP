using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record SupplierResponse(long Id, string Code, string Name, long? CategoryId, bool IsActive, int Version, PartnerReviewStatus ReviewStatus);
public sealed record CreateSupplierRequest(string Code, string Name, long? CategoryId);
public sealed record UpdateSupplierRequest(string Name, bool IsActive, long? CategoryId, int Version);
public sealed record SupplierVersionRequest(int Version);

/// <summary>目前公司的供應商主檔；分類須屬同一公司。</summary>
[ApiController]
[Route("api/v1/md/suppliers")]
[Produces("application/json")]
[Tags("MD - Suppliers")]
public sealed class SuppliersController(SupplierService suppliers, SupplierApprovalService approval) : ControllerBase
{
    /// <summary>依代碼或名稱搜尋供應商；含停用但排除軟刪除資料。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.SupplierView)]
    [ProducesResponseType<PagedResult<SupplierResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<SupplierResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await suppliers.ListAsync(request, token);
        return new PagedResult<SupplierResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立代碼在公司內唯一的供應商。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.SupplierCreate)]
    [ProducesResponseType<SupplierResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierResponse>> Create(CreateSupplierRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created,
            Map(await suppliers.CreateAsync(request.Code, request.Name, request.CategoryId, token)));
    }

    /// <summary>按版本修改名稱、分類及啟用狀態；代碼不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.SupplierEdit)]
    [ProducesResponseType<SupplierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<SupplierResponse> Update(long id, UpdateSupplierRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await suppliers.UpdateAsync(id, request.Name, request.IsActive, request.CategoryId, request.Version, token));
    }

    /// <summary>將待審或遭駁回的供應商送交 SYS 審批流程；無適用步驟時立即核准。</summary>
    [HttpPost("{id:long}/submit")]
    [RequirePermission(MasterDataPermissions.SupplierApprove)]
    [ProducesResponseType<ApprovalState>(StatusCodes.Status200OK)]
    public Task<ApprovalState> Submit(long id, SupplierVersionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return approval.SubmitAsync(id, request.Version, token);
    }

    /// <summary>以版本號軟刪除未被單據引用、且不在審批中的供應商。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.SupplierDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await suppliers.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static SupplierResponse Map(Supplier entity) =>
        new(entity.Id, entity.Code, entity.Name, entity.CategoryId, entity.IsActive, entity.Version, entity.ReviewStatus);
}
