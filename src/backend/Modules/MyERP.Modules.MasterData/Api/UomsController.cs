using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Api;

public sealed record UomResponse(long Id, string Code, string Name, int DecimalPlaces, bool IsActive, int Version);
public sealed record CreateUomRequest(string Code, string Name, int DecimalPlaces);
public sealed record UpdateUomRequest(string Name, bool IsActive, int DecimalPlaces, int Version);
public sealed record CreateUomConversionRequest(long FromUomId, long ToUomId, decimal Factor);
public sealed record UpdateUomConversionRequest(decimal Factor, int Version);
public sealed record UomConvertResponse(long FromUomId, long ToUomId, decimal Quantity, decimal Result);

/// <summary>計量單位；所有查詢依目前授權公司的 X-Company-Id 隔離。</summary>
[ApiController]
[Route("api/v1/md/uoms")]
[Produces("application/json")]
[Tags("MD - Units of Measure")]
public sealed class UomsController(UomService uoms) : ControllerBase
{
    /// <summary>分頁列出目前公司未刪除的單位，包含停用單位。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.UomView)]
    [ProducesResponseType<PagedResult<UomResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResult<UomResponse>> List([FromQuery] PagedRequest request, CancellationToken token)
    {
        var page = await uoms.ListAsync(request, token);
        return new PagedResult<UomResponse>([.. page.Items.Select(Map)], page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>建立公司內代碼唯一的單位，小數位數 0–6。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.UomCreate)]
    [ProducesResponseType<UomResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UomResponse>> Create(CreateUomRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uom = await uoms.CreateAsync(request.Code, request.Name, request.DecimalPlaces, token);
        return StatusCode(StatusCodes.Status201Created, Map(uom));
    }

    /// <summary>按版本修改名稱、小數位數及啟用狀態；代碼不可修改。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.UomEdit)]
    [ProducesResponseType<UomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<UomResponse> Update(long id, UpdateUomRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Map(await uoms.UpdateAsync(id, request.Name, request.IsActive, request.DecimalPlaces, request.Version, token));
    }

    /// <summary>按版本軟刪除；已被換算引用時回 409 <c>MD.UOM.IN_USE</c>。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.UomDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await uoms.DeleteAsync(id, version, token);
        return NoContent();
    }

    private static UomResponse Map(UnitOfMeasure entity) =>
        new(entity.Id, entity.Code, entity.Name, entity.DecimalPlaces, entity.IsActive, entity.Version);
}

/// <summary>單位換算：1 個來源單位 = factor 個目標單位；兩單位之間只允許一條換算路徑。</summary>
[ApiController]
[Route("api/v1/md/uom-conversions")]
[Produces("application/json")]
[Tags("MD - Units of Measure")]
public sealed class UomConversionsController(UomConversionService conversions) : ControllerBase
{
    /// <summary>列出目前公司全部換算（依來源、目標單位代碼排序）。</summary>
    [HttpGet]
    [RequirePermission(MasterDataPermissions.UomView)]
    [ProducesResponseType<IReadOnlyList<UomConversionView>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<UomConversionView>> List(CancellationToken token) => conversions.ListAsync(token);

    /// <summary>建立換算；兩單位已可換算（直接、反向或經其他單位）時回 409 <c>MD.UOM_CONVERSION.PATH_EXISTS</c>。</summary>
    [HttpPost]
    [RequirePermission(MasterDataPermissions.UomCreate)]
    [ProducesResponseType<UomConversionView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UomConversionView>> Create(CreateUomConversionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conversion = await conversions.CreateAsync(request.FromUomId, request.ToUomId, request.Factor, token);
        return StatusCode(StatusCodes.Status201Created, conversion);
    }

    /// <summary>按版本修改換算係數。</summary>
    [HttpPut("{id:long}")]
    [RequirePermission(MasterDataPermissions.UomEdit)]
    [ProducesResponseType<UomConversionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UomConversionView> Update(long id, UpdateUomConversionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return conversions.UpdateAsync(id, request.Factor, request.Version, token);
    }

    /// <summary>按版本軟刪除換算。</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(MasterDataPermissions.UomDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, [FromQuery] int version, CancellationToken token)
    {
        await conversions.DeleteAsync(id, version, token);
        return NoContent();
    }

    /// <summary>試算：沿換算路徑換算數量，並按目標單位小數位數四捨五入。</summary>
    [HttpGet("convert")]
    [RequirePermission(MasterDataPermissions.UomView)]
    [ProducesResponseType<UomConvertResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<UomConvertResponse> Convert(
        [FromQuery] long fromUomId, [FromQuery] long toUomId, [FromQuery] decimal quantity, CancellationToken token) =>
        new(fromUomId, toUomId, quantity, await conversions.ConvertAsync(fromUomId, toUomId, quantity, token));
}
