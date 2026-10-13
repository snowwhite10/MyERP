using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Application;

public sealed record StockBalancePageQuery : PagedRequest
{
    public long? ItemId { get; init; }
    public long? WarehouseId { get; init; }
    public long? LocationId { get; init; }
}

public sealed record StockMovementPageQuery : PagedRequest
{
    public long? ItemId { get; init; }
    public long? WarehouseId { get; init; }
    public long? LocationId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public StockMovementDirection? Direction { get; init; }
}

public sealed record StockBalanceRow(long ItemId, long WarehouseId, long? LocationId, decimal Quantity);

public sealed record StockMovementRow(long Id, DateOnly BusinessDate, DateTime PostedAt, string SourceType,
    long SourceId, int SourceLineNo, StockMovementDirection Direction, long ItemId, string ItemCode,
    long WarehouseId, string WarehouseCode, long? LocationId, string? LocationCode, decimal Quantity, decimal? UnitCost);

/// <summary>目前公司異動帳的唯讀查詢；分組、篩選與分頁皆由 SQL Server 執行（ADR-058）。</summary>
public sealed class StockQueryService(MyErpDbContext db)
{
    public async Task<PagedResult<StockBalanceRow>> BalancesAsync(StockBalancePageQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommon(request, request.ItemId, request.WarehouseId, request.LocationId);
        var movements = FilterKeys(db.Set<StockMovement>().AsNoTracking(), request.ItemId, request.WarehouseId, request.LocationId);
        var balances = movements.GroupBy(x => new { x.ItemId, x.WarehouseId, x.LocationId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, g.Key.LocationId,
                Quantity = g.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity) })
            .Where(x => x.Quantity != 0);
        var count = await balances.LongCountAsync(token);
        var items = await balances.OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new StockBalanceRow(x.ItemId, x.WarehouseId, x.LocationId, x.Quantity)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<PagedResult<StockMovementRow>> MovementsAsync(StockMovementPageQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommon(request, request.ItemId, request.WarehouseId, request.LocationId);
        if (request.From > request.To) throw new RequestValidationException("to", "End date must not be earlier than start date.");
        if (request.Direction is { } direction && !Enum.IsDefined(direction))
            throw new RequestValidationException("direction", "Unknown movement direction.");
        var movements = FilterKeys(db.Set<StockMovement>().AsNoTracking(), request.ItemId, request.WarehouseId, request.LocationId);
        if (request.From is { } from) movements = movements.Where(x => x.BusinessDate >= from);
        if (request.To is { } to) movements = movements.Where(x => x.BusinessDate <= to);
        if (request.Direction is { } selected) movements = movements.Where(x => x.Direction == selected);
        var count = await movements.LongCountAsync(token);
        var items = await movements.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new StockMovementRow(x.Id, x.BusinessDate, x.PostedAt, x.SourceType, x.SourceId,
                x.SourceLineNo, x.Direction, x.ItemId, x.Reference!.ItemCode, x.WarehouseId,
                x.Reference!.WarehouseCode, x.LocationId, x.Reference!.LocationCode, x.Quantity, x.UnitCost))
            .ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    private void ValidateCommon(PagedRequest request, long? itemId, long? warehouseId, long? locationId)
    {
        request.EnsureValid();
        if (db.CurrentCompanyId is not > 0) throw new BusinessRuleException("COMMON.COMPANY.REQUIRED", "Company is required.");
        if (itemId is <= 0) throw new RequestValidationException("itemId", "Item id must be positive.");
        if (warehouseId is <= 0) throw new RequestValidationException("warehouseId", "Warehouse id must be positive.");
        if (locationId is <= 0) throw new RequestValidationException("locationId", "Location id must be positive.");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            throw new RequestValidationException("keyword", "Keyword is not supported by this query.");
        if (!string.IsNullOrWhiteSpace(request.Sort))
            throw new RequestValidationException("sort", "Sorting is fixed for stable inventory paging.");
    }

    private static IQueryable<StockMovement> FilterKeys(IQueryable<StockMovement> query,
        long? itemId, long? warehouseId, long? locationId)
    {
        if (itemId is { } item) query = query.Where(x => x.ItemId == item);
        if (warehouseId is { } warehouse) query = query.Where(x => x.WarehouseId == warehouse);
        if (locationId is { } location) query = query.Where(x => x.LocationId == location);
        return query;
    }
}
