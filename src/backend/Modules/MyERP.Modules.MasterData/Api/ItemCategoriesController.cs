using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.MasterData.Api;

public sealed record ItemCategoryResponse(long Id, string Code, string Name, long? ParentId, bool IsActive, int Version);
public sealed record CreateItemCategoryRequest(string Code, string Name, long? ParentId);
public sealed record UpdateItemCategoryRequest(string Name, bool IsActive, long? ParentId, int Version);

/// <summary>物料分類樹；所有查詢依目前授權公司的 X-Company-Id 隔離。</summary>
[ApiController]
[Route("api/v1/md/item-categories")]
[Produces("application/json")]
[Tags("MD - Item Categories")]
public sealed class ItemCategoriesController(ItemCategoryService categories) : ControllerBase
{
    /// <summary>列出目前公司全部未刪除分類（含停用，按代碼排序），以 parentId 組樹；最多 5 層，不分頁。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ItemCategoryView)]
    [ProducesResponseType<IReadOnlyList<ItemCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ItemCategoryResponse>> List(CancellationToken token) =>
        [.. (await categories.TreeAsync(token)).Select(Map)];

    /// <summary>建立公司內代碼唯一的分類；上層須為同公司啟用分類，且不可超過 5 層。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ItemCategoryCreate)]
    [ProducesResponseType<ItemCategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ItemCategoryResponse>> Create(CreateItemCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var category = await categories.CreateAsync(request.Code, request.Name, request.ParentId, token);
        return StatusCode(StatusCodes.Status201Created, Map(category));
    }

    /// <summary>按版本修改名稱、啟用狀態與上層（搬移整棵子樹）；代碼不可修改，不可掛到自己或子孫下。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemCategoryEdit)]
    [ProducesResponseType<ItemCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ItemCategoryResponse> Update(long id, UpdateItemCategoryRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await categories.UpdateAsync(id, request.Name, request.IsActive, request.ParentId, request.Version, token));
    }

    /// <summary>按版本軟刪除；有子分類時回 409，代碼仍保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemCategoryDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await categories.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static ItemCategoryResponse Map(ItemCategory entity) =>
        new(entity.Id, entity.Code, entity.Name, entity.ParentId, entity.IsActive, entity.Version);
}
