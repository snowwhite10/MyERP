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

    /// <summary>於單一資料庫快照串流完整 CSV；超過 10,000 行先回 422，不產生截斷檔案。</summary>
    [HttpGet("export")]
    [RequirePermission(InventoryPermissions.ReportView)]
    [RequirePermission(InventoryPermissions.ReportExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task Export([FromQuery] StockFlowReportQuery request, CancellationToken token)
    {
        await report.ExportAsync(request, Response.Body, onReady: (count, _) =>
        {
            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = "attachment; filename=stock-flow.csv";
            Response.Headers["X-Export-Row-Count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        }, token: token);
    }
}
