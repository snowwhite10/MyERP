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

public sealed record CreateItemUomConversionRequest(long UomId, decimal Factor);
public sealed record UpdateItemUomConversionRequest(decimal Factor, int Version);
public sealed record ItemUomConvertResponse(long ItemId, long FromUomId, long ToUomId, decimal Quantity, decimal Result);

/// <summary>物料專屬換算：1 個換算單位 = factor 個物料基本單位；換算時覆寫同一單位的公司級換算（ADR-035）。</summary>
[ApiController]
[Route("api/v1/md/items/{itemId:long}/uom-conversions")]
[Produces("application/json")]
[Tags("MD - Items")]
public sealed class ItemUomConversionsController(ItemUomConversionService conversions, UomConversionService converter) : ControllerBase
{
    /// <summary>列出物料的專屬換算（依單位代碼排序）。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ItemView)]
    [ProducesResponseType<IReadOnlyList<ItemUomConversionView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ItemUomConversionView>> List(long itemId, CancellationToken token) =>
        conversions.ListAsync(itemId, token);

    /// <summary>新增專屬換算；單位須為本公司啟用單位且不是基本單位，同一單位重複回 409 <c>MD.ITEM_UOM_CONVERSION.DUPLICATE</c>。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType<ItemUomConversionView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ItemUomConversionView>> Create(
        long itemId, CreateItemUomConversionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created, await conversions.CreateAsync(itemId, request.UomId, request.Factor, token));
    }

    /// <summary>按版本修改換算係數。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType<ItemUomConversionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ItemUomConversionView> Update(long itemId, long id, UpdateItemUomConversionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return conversions.UpdateAsync(itemId, id, request.Factor, request.Version, token);
    }

    /// <summary>按版本軟刪除換算；物料沒有專屬換算後才可修改基本單位。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long itemId, long id, [FromQuery] int version, CancellationToken token)
    {
        await conversions.DeleteAsync(itemId, id, version, token);
        return NoContent();
    }

    /// <summary>試算：按物料換算數量（專屬換算優先，公司級換算補充），按目標單位小數位數四捨五入。</summary>
    [HttpGet("convert")]
    [RequirePermission(MasterDataPermissions.ItemView)]
    [ProducesResponseType<ItemUomConvertResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ItemUomConvertResponse> Convert(long itemId,
        [FromQuery] long fromUomId, [FromQuery] long toUomId, [FromQuery] decimal quantity, CancellationToken token) =>
        new(itemId, fromUomId, toUomId, quantity, await converter.ConvertForItemAsync(itemId, fromUomId, toUomId, quantity, token));
}
