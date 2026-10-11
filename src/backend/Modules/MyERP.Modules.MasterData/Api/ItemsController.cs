using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record ItemResponse(long Id, string Code, string Name, string? Specification,
    long BaseUomId, long? BrandId, long? CategoryId, bool IsActive, int Version);
public sealed record CreateItemRequest(string Code, string Name, string? Specification,
    long BaseUomId, long? BrandId, long? CategoryId);
public sealed record UpdateItemRequest(string Name, string? Specification, long BaseUomId,
    long? BrandId, long? CategoryId, bool IsActive, int Version);

/// <summary>目前公司的物料主檔；數量與期初庫存由庫存模組管理。</summary>
[ApiController]
[Route("api/v1/md/items")]
[Produces("application/json")]
[Tags("MD - Items")]
public sealed class ItemsController(ItemService items) : ControllerBase
{
    /// <summary>依代碼、名稱或規格搜尋物料；含停用物料。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ItemView)]
    [ProducesResponseType<PagedResult<ItemResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ItemResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await items.ListAsync(request, token);
        return new PagedResult<ItemResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立物料；代碼在公司內永久唯一，參照須為目前公司的啟用資料。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ItemCreate)]
    [ProducesResponseType<ItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ItemResponse>> Create(CreateItemRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created,
            Map(await items.CreateAsync(request.Code, request.Name, request.Specification,
                request.BaseUomId, request.BrandId, request.CategoryId, token)));
    }

    /// <summary>按版本修改物料資料與啟用狀態；代碼不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType<ItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ItemResponse> Update(long id, UpdateItemRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await items.UpdateAsync(id, request.Name, request.Specification, request.BaseUomId,
            request.BrandId, request.CategoryId, request.IsActive, request.Version, token));
    }

    private static ItemResponse Map(Item item) => new(item.Id, item.Code, item.Name, item.Specification,
        item.BaseUomId, item.BrandId, item.CategoryId, item.IsActive, item.Version);
}
