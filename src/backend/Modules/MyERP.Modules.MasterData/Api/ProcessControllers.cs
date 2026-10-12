using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record ProcessCategoryResponse(long Id, string Code, string Name, int SortOrder, int ProcessCount, bool IsActive, int Version);
public sealed record CreateProcessCategoryRequest(string Code, string Name, int SortOrder);
public sealed record UpdateProcessCategoryRequest(string Name, int SortOrder, bool IsActive, int Version);

/// <summary>工序類別（ADR-042）；單層清單，預設按排序號。</summary>
[ApiController]
[Route("api/v1/md/process-categories")]
[Produces("application/json")]
[Tags("MD - Process Categories")]
public sealed class ProcessCategoriesController(ProcessCategoryService service) : ControllerBase
{
    /// <summary>分頁查詢目前公司未刪除的工序類別（含停用）及各類別工序數；排序可用 sortOrder、code、name。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ProcessCategoryView)]
    [ProducesResponseType<PagedResult<ProcessCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ProcessCategoryResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await service.ListAsync(request, token);
        var counts = await service.ProcessCountsAsync([.. page.Items.Select(x => x.Id)], token);
        return new([.. page.Items.Select(x => Map(x, counts.GetValueOrDefault(x.Id)))], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立工序類別；代碼公司內永久唯一。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ProcessCategoryCreate)]
    [ProducesResponseType<ProcessCategoryResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProcessCategoryResponse>> Create(CreateProcessCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created, Map(await service.CreateAsync(request.Code, request.Name, request.SortOrder, token), 0));
    }

    /// <summary>按版本修改名稱、排序號與啟用狀態。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ProcessCategoryEdit)]
    [ProducesResponseType<ProcessCategoryResponse>(StatusCodes.Status200OK)]
    public async Task<ProcessCategoryResponse> Update(long id, UpdateProcessCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await service.UpdateAsync(id, request.Name, request.SortOrder, request.IsActive, request.Version, token);
        return Map(item, (await service.ProcessCountsAsync([item.Id], token)).GetValueOrDefault(item.Id));
    }

    /// <summary>按版本軟刪除；類別下仍有工序時 409。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ProcessCategoryDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await service.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static ProcessCategoryResponse Map(ProcessCategory x, int processCount) =>
        new(x.Id, x.Code, x.Name, x.SortOrder, processCount, x.IsActive, x.Version);
}

public sealed record ProcessResponse(long Id, string Code, string Name, long? CategoryId, long? UomId, decimal StandardRate,
    int SortOrder, bool IsActive, int Version);
public sealed record CreateProcessRequest(string Code, string Name, long? CategoryId, long? UomId, decimal StandardRate, int SortOrder);
public sealed record UpdateProcessRequest(string Name, long? CategoryId, long? UomId, decimal StandardRate, int SortOrder,
    bool IsActive, int Version);

/// <summary>工序主檔（ADR-042）；PP 經 IProcessLookup 讀取。</summary>
[ApiController]
[Route("api/v1/md/processes")]
[Produces("application/json")]
[Tags("MD - Processes")]
public sealed class ProcessesController(ProcessService service) : ControllerBase
{
    /// <summary>分頁查詢目前公司未刪除工序（含停用），可按類別篩選；排序可用 sortOrder、code、name。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ProcessView)]
    [ProducesResponseType<PagedResult<ProcessResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ProcessResponse>> List([FromQuery] PagedRequest request, [FromQuery] long? categoryId, CancellationToken token)
    {
        var page = await service.ListAsync(request, categoryId, token);
        return new([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立工序，驗證類別及計件單位為同公司啟用資料、工價不為負。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ProcessCreate)]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProcessResponse>> Create(CreateProcessRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await service.CreateAsync(request.Code, request.Name,
            new(request.CategoryId, request.UomId, request.StandardRate, request.SortOrder), token);
        return StatusCode(StatusCodes.Status201Created, Map(item));
    }

    /// <summary>按版本修改名稱、類別、單位、工價、排序號及啟用狀態；代碼不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ProcessEdit)]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status200OK)]
    public async Task<ProcessResponse> Update(long id, UpdateProcessRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await service.UpdateAsync(id, request.Name,
            new(request.CategoryId, request.UomId, request.StandardRate, request.SortOrder), request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除；被 PP 引用時只能停用。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ProcessDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await service.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static ProcessResponse Map(Process x) =>
        new(x.Id, x.Code, x.Name, x.CategoryId, x.UomId, x.StandardRate, x.SortOrder, x.IsActive, x.Version);
}
