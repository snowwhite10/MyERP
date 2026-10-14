using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

/// <summary>唯讀暫估庫存成本；結果不能直接用於財務過帳（REQ_F4115.25.1–3）。</summary>
[ApiController]
[Route("api/v1/inv/valuations")]
[Produces("application/json")]
[Tags("INV - Valuation")]
public sealed class ValuationsController(IInventoryValuationQuery query, IValuationItemScope items) : ControllerBase
{
    /// <summary>列出目前公司估值可選物料，包括停用但保留歷史異動的物料。</summary>
    [HttpGet("items")]
    [RequirePermission(InventoryPermissions.ValuationView)]
    [ProducesResponseType<PagedResult<ValuationItemOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ValuationItemOption>> ListItems([FromQuery] PagedRequest request, CancellationToken token) =>
        items.ListItemsAsync(request, token);

    /// <summary>依目前公司、物料及可選過帳截止時間計算移動加權平均暫估。</summary>
    [HttpGet("items/{itemId:long}")]
    [RequirePermission(InventoryPermissions.ValuationView)]
    [ProducesResponseType<InventoryValuation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<InventoryValuation> Item(long itemId, [FromQuery] DateTime? cutoffPostedAt, CancellationToken token)
    {
        await items.RequireItemAsync(itemId, token);
        return await query.GetItemAsync(itemId, cutoffPostedAt, token);
    }
}
