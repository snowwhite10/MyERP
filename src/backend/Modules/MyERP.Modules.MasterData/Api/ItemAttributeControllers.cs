using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record ItemAttributeResponse(long Id, string Code, string Name, int SortOrder, int ItemCount, bool IsActive, int Version);
public sealed record CreateItemAttributeRequest(string Code, string Name, int SortOrder);
public sealed record UpdateItemAttributeRequest(string Name, int SortOrder, bool IsActive, int Version);

/// <summary>物料屬性定義（REQ_F4118.45.1–4、ADR-045），例如顏色、尺寸；單層清單，預設按排序號。</summary>
[ApiController]
[Route("api/v1/md/item-attributes")]
[Produces("application/json")]
[Tags("MD - Item Attributes")]
public sealed class ItemAttributesController(ItemAttributeService service) : ControllerBase
{
    /// <summary>分頁查詢目前公司未刪除的屬性（含停用）及各屬性的使用物料數；排序可用 sortOrder、code、name。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ItemAttributeView)]
    [ProducesResponseType<PagedResult<ItemAttributeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResult<ItemAttributeResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await service.ListAsync(request, token);
        var counts = await service.ItemCountsAsync([.. page.Items.Select(x => x.Id)], token);
        return new([.. page.Items.Select(x => Map(x, counts.GetValueOrDefault(x.Id)))], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立屬性；代碼公司內永久唯一，排序號 0–9999。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ItemAttributeCreate)]
    [ProducesResponseType<ItemAttributeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ItemAttributeResponse>> Create(CreateItemAttributeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StatusCode(StatusCodes.Status201Created, Map(await service.CreateAsync(request.Code, request.Name, request.SortOrder, token), 0));
    }

    /// <summary>按版本修改名稱、排序號與啟用狀態；被物料使用的屬性也可停用。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemAttributeEdit)]
    [ProducesResponseType<ItemAttributeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ItemAttributeResponse> Update(long id, UpdateItemAttributeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await service.UpdateAsync(id, request.Name, request.SortOrder, request.IsActive, request.Version, token);
        return Map(item, (await service.ItemCountsAsync([item.Id], token)).GetValueOrDefault(item.Id));
    }

    /// <summary>按版本軟刪除；被物料使用時 409 <c>MD.ITEM_ATTRIBUTE.IN_USE</c>（請改為停用）。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemAttributeDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await service.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static ItemAttributeResponse Map(ItemAttributeDefinition x, int itemCount) =>
        new(x.Id, x.Code, x.Name, x.SortOrder, itemCount, x.IsActive, x.Version);
}

public sealed record ReplaceItemAttributesRequest(IReadOnlyList<ItemAttributeValueInput>? Attributes);

/// <summary>
/// 物料的屬性值（REQ_F4118.45.5–6、介面 8.16、ADR-045）。查看用 <c>MD.ITEM.VIEW</c>，整批取代用 <c>MD.ITEM.EDIT</c>。
/// </summary>
[ApiController]
[Route("api/v1/md/items/{itemId:long}/attributes")]
[Produces("application/json")]
[Tags("MD - Items")]
public sealed class ItemAttributeValuesController(ItemAttributeValueService values) : ControllerBase
{
    /// <summary>物料的屬性值，按屬性排序號、代碼；含已停用的屬性。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ItemView)]
    [ProducesResponseType<IReadOnlyList<ItemAttributeValueView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ItemAttributeValueView>> List(long itemId, CancellationToken token) => values.ListAsync(itemId, token);

    /// <summary>
    /// 以完整集合取代物料的屬性值（空集合清除全部），最多 50 個；值必填、最多 200 字；同一屬性只可出現一次。
    /// 新設定的屬性須為本公司啟用屬性，原有值的屬性停用後仍可保留。錯誤鍵如 <c>attributes[1].value</c>。
    /// </summary>
    [HttpPut]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType<IReadOnlyList<ItemAttributeValueView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ItemAttributeValueView>> Replace(long itemId, ReplaceItemAttributesRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return values.ReplaceAsync(itemId, request.Attributes, token);
    }
}
