using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record ItemResponse(long Id, string Code, string Name, string? Specification,
    long BaseUomId, long? BrandId, long? CategoryId, ItemTrackingMode TrackingMode, bool IsActive, int Version);
/// <summary>建立物料；批號／序號追蹤方式 <c>None</c>／<c>Lot</c>／<c>Serial</c>，省略為 <c>None</c>（ADR-060）。</summary>
public sealed record CreateItemRequest(string Code, string Name, string? Specification,
    long BaseUomId, long? BrandId, long? CategoryId, ItemTrackingMode TrackingMode = ItemTrackingMode.None);
/// <summary>修改物料；追蹤方式省略時保持不變，物料已被使用時改變回 409 <c>MD.ITEM.TRACKING_MODE_LOCKED</c>。</summary>
public sealed record UpdateItemRequest(string Name, string? Specification, long BaseUomId,
    long? BrandId, long? CategoryId, bool IsActive, int Version, ItemTrackingMode? TrackingMode = null);

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
                request.BaseUomId, request.BrandId, request.CategoryId, request.TrackingMode, token)));
    }

    /// <summary>
    /// 按版本修改物料資料、追蹤方式與啟用狀態；代碼不可修改。已有庫存、交易或單據時不可改基本單位
    /// （409 <c>MD.ITEM.BASE_UOM_LOCKED</c>）或追蹤方式（409 <c>MD.ITEM.TRACKING_MODE_LOCKED</c>）。
    /// </summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType<ItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ItemResponse> Update(long id, UpdateItemRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await items.UpdateAsync(id, request.Name, request.Specification, request.BaseUomId,
            request.BrandId, request.CategoryId, request.IsActive, request.Version, request.TrackingMode, token));
    }

    /// <summary>
    /// 按版本軟刪除物料（REQ_F4118.3.4）；代碼保留不可重用。專屬換算、圖片、屬性值一併刪除（ADR-050）。
    /// 已有庫存、交易或單據時回 409 <c>MD.ITEM.IN_USE</c>；版本不符回 409，找不到回 404 <c>MD.ITEM.NOT_FOUND</c>。
    /// </summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await items.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static ItemResponse Map(Item item) => new(item.Id, item.Code, item.Name, item.Specification,
        item.BaseUomId, item.BrandId, item.CategoryId, ItemService.TrackingModeOf(item), item.IsActive, item.Version);
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

public sealed record UpdateItemImageRequest(int SortOrder, bool IsPrimary, int Version);

/// <summary>
/// 物料圖片（REQ_F4118.44.1–4、介面 8.15、ADR-045）。只接受 PNG／JPEG（依內容判斷）、2 MB 以內、寬高 16–4096 像素，
/// 每個物料最多 10 張；有圖片的物料恰好一張主圖。查看用 <c>MD.ITEM.VIEW</c>，維護用 <c>MD.ITEM.EDIT</c>。
/// </summary>
[ApiController]
[Route("api/v1/md/items/{itemId:long}/images")]
[Produces("application/json")]
[Tags("MD - Items")]
public sealed class ItemImagesController(ItemImageService images) : ControllerBase
{
    /// <summary>請求本體上限：圖片 2 MB 加上 multipart 表單欄位。</summary>
    private const long MaxRequestBytes = ItemImageService.MaxBytes + 64 * 1024;

    /// <summary>物料的圖片（不含內容）：主圖排第一，其餘按排序號。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.ItemView)]
    [ProducesResponseType<IReadOnlyList<ItemImageView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ItemImageView>> List(long itemId, CancellationToken token) => images.ListAsync(itemId, token);

    /// <summary>
    /// 圖片內容（<c>image/png</c>／<c>image/jpeg</c>，inline）。<c>ETag</c> 為版本號，帶 <c>If-None-Match</c> 且未變更時回 304。
    /// 圖片不屬該物料時回 404 <c>MD.ITEM_IMAGE.NOT_FOUND</c>。
    /// </summary>
    [HttpGet("{id:long}/content")]
    [RequirePermission(MasterDataPermissions.ItemView)]
    [Produces("image/png", "image/jpeg", "application/json")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Content(long itemId, long id, CancellationToken token)
    {
        Response.Headers.CacheControl = "private, no-cache";
        // 瀏覽器重新驗證（no-cache）時只查版本，未變更就不讀取圖片內容
        var conditions = Request.GetTypedHeaders().IfNoneMatch;
        if (conditions.Count > 0 && await images.GetVersionAsync(itemId, id, token) is { } current
            && conditions.Any(tag => tag.Tag == $"\"{current}\""))
        {
            Response.Headers.ETag = $"\"{current}\"";
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var image = await images.GetContentAsync(itemId, id, token);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = image.FileName }.ToString();
        return File(image.Content, image.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue($"\"{image.Version}\""));
    }

    /// <summary>
    /// 上傳圖片（multipart：<c>file</c>、可選 <c>sortOrder</c> 0–9999（省略時排最後）、<c>isPrimary</c>）。第一張自動成為主圖。
    /// 沒有檔案或空檔回 400；類型、大小、尺寸、張數不符回 422（<c>MD.ITEM_IMAGE.UNSUPPORTED_TYPE</c>／<c>TOO_LARGE</c>／
    /// <c>INVALID_DIMENSIONS</c>／<c>LIMIT_REACHED</c>）。
    /// </summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<ItemImageView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ItemImageView>> Upload(long itemId, IFormFile? file, [FromForm] int? sortOrder,
        [FromForm] bool isPrimary, CancellationToken token)
    {
        if (file is null) throw new RequestValidationException("file", "Select an image file.");
        if (file.Length > ItemImageService.MaxBytes)
            throw new BusinessRuleException(ItemImageService.TooLargeCode, $"The image must not exceed {ItemImageService.MaxBytes} bytes.");
        await using var stream = file.OpenReadStream();
        return StatusCode(StatusCodes.Status201Created, await images.UploadAsync(itemId, file.FileName, stream, sortOrder, isPrimary, token));
    }

    /// <summary>按版本修改排序號或設為主圖（原主圖自動取消）；不可直接取消主圖（400 <c>isPrimary</c>）。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType<ItemImageView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ItemImageView> Update(long itemId, long id, UpdateItemImageRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return images.UpdateAsync(itemId, id, request.SortOrder, request.IsPrimary, request.Version, token);
    }

    /// <summary>按版本刪除圖片；刪除主圖時由排序號最小的圖片接任。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.ItemEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long itemId, long id, [FromQuery] int version, CancellationToken token)
    {
        await images.DeleteAsync(itemId, id, version, token);
        return NoContent();
    }
}
