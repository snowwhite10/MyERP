using System.Data;
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
    public async Task<StockFlowReportPage> QueryAsync(StockFlowReportQuery request, CancellationToken token = default)
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

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        // SQL Server establishes the version point at the first read inside the transaction.
        var queriedAt = clock.Now;
        var movements = db.Set<StockMovement>().AsNoTracking().Where(x => x.BusinessDate <= request.To);
        if (request.ItemId is { } item) movements = movements.Where(x => x.ItemId == item);
        if (request.WarehouseId is { } warehouse) movements = movements.Where(x => x.WarehouseId == warehouse);
        if (request.LocationId is { } location) movements = movements.Where(x => x.LocationId == location);
        var grouped = movements.GroupBy(x => new { x.ItemId, x.WarehouseId, x.LocationId })
            .Select(g => new
            {
                g.Key.ItemId, g.Key.WarehouseId, g.Key.LocationId,
                Opening = g.Sum(x => x.BusinessDate < request.From && x.Direction == StockMovementDirection.In ? x.Quantity
                    : x.BusinessDate < request.From ? -x.Quantity : 0m),
                Inbound = g.Sum(x => x.BusinessDate >= request.From && x.Direction == StockMovementDirection.In ? x.Quantity : 0m),
                Outbound = g.Sum(x => x.BusinessDate >= request.From && x.Direction == StockMovementDirection.Out ? x.Quantity : 0m),
            });
        var count = await grouped.LongCountAsync(token);
        var values = await grouped.OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        var rows = values.Select(x =>
        {
            var closing = x.Opening + x.Inbound - x.Outbound;
            return new StockFlowReportRow(x.ItemId, x.WarehouseId, x.LocationId,
                x.Opening, x.Inbound, x.Outbound, closing, closing < 0);
        }).ToArray();
        await tx.CommitAsync(token);
        return new StockFlowReportPage(queriedAt, count, rows, request.Page, request.PageSize);
    }
}
