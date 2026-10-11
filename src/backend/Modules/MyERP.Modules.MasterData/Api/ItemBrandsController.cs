using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record ItemBrandResponse(long Id, string Code, string Name, bool IsActive, int Version);
public sealed record CreateItemBrandRequest(string Code, string Name);
public sealed record UpdateItemBrandRequest(string Name, bool IsActive, int Version);

/// <summary>物料品牌；所有查詢依目前授權公司的 X-Company-Id 隔離。</summary>
[ApiController]
[Route("api/v1/md/item-brands")]
[Produces("application/json")]
[Tags("MD - Item Brands")]
public sealed class ItemBrandsController(ItemBrandService brands) : ControllerBase
{
    /// <summary>分頁列出目前公司未刪除的品牌，包含停用品牌。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ItemBrandView)]
    [ProducesResponseType<PagedResult<ItemBrandResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResult<ItemBrandResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await brands.ListAsync(request, token);
        return new PagedResult<ItemBrandResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立公司內代碼唯一的品牌。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ItemBrandCreate)]
    [ProducesResponseType<ItemBrandResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ItemBrandResponse>> Create(CreateItemBrandRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brand = await brands.CreateAsync(request.Code, request.Name, token);
        return StatusCode(StatusCodes.Status201Created, Map(brand));
    }

    /// <summary>按版本修改名稱及啟用狀態；代碼不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemBrandEdit)]
    [ProducesResponseType<ItemBrandResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ItemBrandResponse> Update(long id, UpdateItemBrandRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await brands.UpdateAsync(id, request.Name, request.IsActive, request.Version, token));
    }

    /// <summary>按版本軟刪除；代碼仍保留。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemBrandDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await brands.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static ItemBrandResponse Map(ItemBrand entity) =>
        new(entity.Id, entity.Code, entity.Name, entity.IsActive, entity.Version);
}
