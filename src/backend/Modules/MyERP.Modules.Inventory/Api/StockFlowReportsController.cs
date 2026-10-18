using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyERP.Modules.Inventory.Application;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.Modules.Inventory.Api;

/// <summary>進出存數量報表；日期按業務日期含頭尾，結果不代表估值金額。</summary>
[ApiController]
[Route("api/v1/inv/reports/stock-flow")]
[Produces("application/json")]
[Tags("INV - Stock Flow Report")]
public sealed class StockFlowReportsController(StockFlowReportService report) : ControllerBase
{
    /// <summary>按目前公司及物料、倉庫、儲位查詢期初／入／出／期末基本單位數量。</summary>
    [HttpGet]
    [RequirePermission(InventoryPermissions.ReportView)]
    [ProducesResponseType<StockFlowReportPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<StockFlowReportPage> Get([FromQuery] StockFlowReportQuery request, CancellationToken token) =>
        report.QueryAsync(request, token);
}
