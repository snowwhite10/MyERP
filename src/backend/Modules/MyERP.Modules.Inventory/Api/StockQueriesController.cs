using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Inventory.Application;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Api;

/// <summary>從目前公司不可改寫的異動帳查詢結存與異動明細（REQ_F4115.1.1–2）。</summary>
[ApiController]
[Route("api/v1/inv")]
[Produces("application/json")]
[Tags("INV - Stock Queries")]
public sealed class StockQueriesController(StockQueryService query) : ControllerBase
{
    /// <summary>依物料、倉庫、可選儲位查詢非零結存；數量為基本單位，按 ID 穩定排序。</summary>
    [HttpGet("balances")]
    [RequirePermission(InventoryPermissions.InventoryView)]
    [ProducesResponseType<PagedResult<StockBalanceRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<StockBalanceRow>> Balances([FromQuery] StockBalancePageQuery request, CancellationToken token) =>
        query.BalancesAsync(request, token);

    /// <summary>依物料、倉庫、儲位、業務日期及方向查詢異動；按業務日期與 ID 由新到舊排序。</summary>
    [HttpGet("movements")]
    [RequirePermission(InventoryPermissions.MovementView)]
    [ProducesResponseType<PagedResult<StockMovementRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<StockMovementRow>> Movements([FromQuery] StockMovementPageQuery request, CancellationToken token) =>
        query.MovementsAsync(request, token);
}
