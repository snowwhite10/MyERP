using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.Inventory.Application;

public sealed record StockFlowReportQuery : PagedRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public long? ItemId { get; init; }
    public long? WarehouseId { get; init; }
    public long? LocationId { get; init; }
}

public sealed record StockFlowReportRow(long ItemId, long WarehouseId, long? LocationId,
    decimal OpeningQuantity, decimal InboundQuantity, decimal OutboundQuantity,
    decimal ClosingQuantity, bool NegativeClosing);

public sealed record StockFlowReportPage(DateTimeOffset QueriedAt, long TotalCount,
    IReadOnlyList<StockFlowReportRow> Items, int Page, int PageSize);

/// <summary>依業務日期統計單一公司異動帳；總數和資料頁在同一 SQL Server SNAPSHOT 交易讀取。</summary>
public sealed class StockFlowReportService(MyErpDbContext db, IClock clock)
{
    public const int MaxExportRows = 10000;
    private sealed class Aggregate
    {
        public long ItemId { get; init; }
        public long WarehouseId { get; init; }
        public long? LocationId { get; init; }
        public decimal Opening { get; init; }
        public decimal Inbound { get; init; }
        public decimal Outbound { get; init; }
    }

    public async Task<StockFlowReportPage> QueryAsync(StockFlowReportQuery request, CancellationToken token = default)
    {
        Validate(request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var queriedAt = clock.Now;
        var grouped = BuildQuery(request);
        var count = await grouped.LongCountAsync(token);
        var values = await Order(grouped).Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        var rows = values.Select(ToRow).ToArray();
        await tx.CommitAsync(token);
        return new StockFlowReportPage(queriedAt, count, rows, request.Page, request.PageSize);
    }

    /// <summary>先在同一快照檢查列數，再直接寫入 HTTP stream；超限時絕不寫檔案位元組。</summary>
    public async Task<long> ExportAsync(StockFlowReportQuery request, Stream output,
        int maxRows = MaxExportRows, Func<long, DateTimeOffset, Task>? onReady = null, CancellationToken token = default)
    {
        Validate(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var queriedAt = clock.Now;
        var grouped = BuildQuery(request);
        var count = await grouped.LongCountAsync(token);
        if (count > maxRows)
            throw new BusinessRuleException("INV.REPORT.EXPORT_LIMIT", $"Report has {count} rows; narrow the filters to {maxRows} rows or fewer.");
        if (onReady is not null) await onReady(count, queriedAt);
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), token);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync($"QueriedAt,{Csv(queriedAt.ToString("O", CultureInfo.InvariantCulture))}".AsMemory(), token);
        await writer.WriteLineAsync("ItemId,WarehouseId,LocationId,OpeningQuantity,InboundQuantity,OutboundQuantity,ClosingQuantity,NegativeClosing".AsMemory(), token);
        await foreach (var value in Order(grouped).AsAsyncEnumerable().WithCancellation(token))
        {
            var row = ToRow(value);
            await writer.WriteLineAsync(string.Join(',', row.ItemId, row.WarehouseId, row.LocationId?.ToString(CultureInfo.InvariantCulture) ?? "",
                Number(row.OpeningQuantity), Number(row.InboundQuantity), Number(row.OutboundQuantity),
                Number(row.ClosingQuantity), row.NegativeClosing ? "true" : "false").AsMemory(), token);
        }
        await writer.FlushAsync(token);
        await tx.CommitAsync(token);
        return count;
    }

    private static string Number(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
    private static string Csv(string value)
    {
        var first = value.AsSpan().TrimStart();
        if (!first.IsEmpty && "=+-@".Contains(first[0])) value = "'" + value;
        return value.ContainsAny([',', '"', '\r', '\n']) ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
    private static StockFlowReportRow ToRow(Aggregate x)
    {
        var closing = x.Opening + x.Inbound - x.Outbound;
        return new StockFlowReportRow(x.ItemId, x.WarehouseId, x.LocationId,
            x.Opening, x.Inbound, x.Outbound, closing, closing < 0);
    }
    private static IOrderedQueryable<Aggregate> Order(IQueryable<Aggregate> query) =>
        query.OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId);

    private void Validate(StockFlowReportQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (request.From is null) throw new RequestValidationException("from", "Start date is required.");
        if (request.To is null || request.From > request.To)
            throw new RequestValidationException("to", "End date must be on or after start date.");
        if (request.ItemId is <= 0) throw new RequestValidationException("itemId", "Item id must be positive.");
        if (request.WarehouseId is <= 0) throw new RequestValidationException("warehouseId", "Warehouse id must be positive.");
        if (request.LocationId is <= 0) throw new RequestValidationException("locationId", "Location id must be positive.");
        if (!string.IsNullOrWhiteSpace(request.Sort)) throw new RequestValidationException("sort", "Sorting is fixed.");
        if (!string.IsNullOrWhiteSpace(request.Keyword)) throw new RequestValidationException("keyword", "Keyword is not supported.");
        if (db.CurrentCompanyId is not > 0) throw new BusinessRuleException("COMMON.COMPANY.REQUIRED", "Company is required.");

    }

    private IQueryable<Aggregate> BuildQuery(StockFlowReportQuery request)
    {
        var movements = db.Set<StockMovement>().AsNoTracking().Where(x => x.BusinessDate <= request.To);
        if (request.ItemId is { } item) movements = movements.Where(x => x.ItemId == item);
        if (request.WarehouseId is { } warehouse) movements = movements.Where(x => x.WarehouseId == warehouse);
        if (request.LocationId is { } location) movements = movements.Where(x => x.LocationId == location);
        var grouped = movements.GroupBy(x => new { x.ItemId, x.WarehouseId, x.LocationId })
            .Select(g => new Aggregate
            {
                ItemId = g.Key.ItemId, WarehouseId = g.Key.WarehouseId, LocationId = g.Key.LocationId,
                Opening = g.Sum(x => x.BusinessDate < request.From && x.Direction == StockMovementDirection.In ? x.Quantity
                    : x.BusinessDate < request.From ? -x.Quantity : 0m),
                Inbound = g.Sum(x => x.BusinessDate >= request.From && x.Direction == StockMovementDirection.In ? x.Quantity : 0m),
                Outbound = g.Sum(x => x.BusinessDate >= request.From && x.Direction == StockMovementDirection.Out ? x.Quantity : 0m)
            });
        return grouped;
    }
}
