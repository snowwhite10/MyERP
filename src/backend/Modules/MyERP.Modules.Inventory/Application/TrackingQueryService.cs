using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Inventory.Application;

/// <summary>批號／序號列表篩選：關鍵字比對批號或序號；<see cref="InStockOnly"/> 只列仍有結存者。</summary>
public sealed record TrackingPageQuery : PagedRequest
{
    public long? ItemId { get; init; }
    public bool InStockOnly { get; init; }
}

public sealed record LotRow(long Id, long ItemId, string LotNo, DateOnly? ManufactureDate, DateOnly? ExpiryDate, decimal OnHand);

public sealed record SerialRow(long Id, long ItemId, string SerialNo, bool InStock);

public sealed record TrackedLocation(long WarehouseId, long? LocationId, decimal Quantity);

public sealed record TrackedMovement(long MovementId, DateOnly BusinessDate, DateTime PostedAt, string SourceType, long SourceId,
    int SourceLineNo, StockMovementDirection Direction, long WarehouseId, long? LocationId, decimal Quantity);

/// <summary>追溯結果：現存位置與異動歷史（新到舊，最多 <see cref="TrackingQueryService.MaxHistory"/> 筆）。</summary>
public sealed record TrackingTrace<T>(T Header, IReadOnlyList<TrackedLocation> Locations, IReadOnlyList<TrackedMovement> History,
    bool HistoryTruncated);

/// <summary>
/// 批號／序號追溯（ADR-060 第 9 點）：結存與歷史一律由只增不改的 <c>inv_stock_movement_tracking</c> 即時加總，
/// 公司篩選自動套用。大量資料的進出存報表留 WU-INV-017。
/// </summary>
public sealed class TrackingQueryService(MyErpDbContext db)
{
    public const int MaxHistory = 1000;
    public const string LotNotFound = "INV.LOT.NOT_FOUND";
    public const string SerialNotFound = "INV.SERIAL.NOT_FOUND";

    public async Task<PagedResult<LotRow>> LotsAsync(TrackingPageQuery request, CancellationToken token = default)
    {
        Validate(request);
        var lots = db.Set<StockLot>().AsNoTracking();
        if (request.ItemId is { } itemId) lots = lots.Where(x => x.ItemId == itemId);
        if (Keyword(request) is { } keyword) lots = lots.Where(x => x.LotNo.Contains(keyword));
        var rows = lots.Select(x => new
        {
            x.Id, x.ItemId, x.LotNo, x.ManufactureDate, x.ExpiryDate,
            OnHand = db.Set<StockMovementTracking>().Where(t => t.LotId == x.Id)
                .Sum(t => t.Movement!.Direction == StockMovementDirection.In ? t.Quantity : -t.Quantity),
        });
        if (request.InStockOnly) rows = rows.Where(x => x.OnHand != 0);
        var count = await rows.LongCountAsync(token);
        var items = await rows.OrderBy(x => x.ItemId).ThenBy(x => x.LotNo).ThenBy(x => x.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new LotRow(x.Id, x.ItemId, x.LotNo, x.ManufactureDate, x.ExpiryDate, x.OnHand)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<PagedResult<SerialRow>> SerialsAsync(TrackingPageQuery request, CancellationToken token = default)
    {
        Validate(request);
        var serials = db.Set<StockSerial>().AsNoTracking();
        if (request.ItemId is { } itemId) serials = serials.Where(x => x.ItemId == itemId);
        if (Keyword(request) is { } keyword) serials = serials.Where(x => x.SerialNo.Contains(keyword));
        var rows = serials.Select(x => new
        {
            x.Id, x.ItemId, x.SerialNo,
            OnHand = db.Set<StockMovementTracking>().Where(t => t.SerialId == x.Id)
                .Sum(t => t.Movement!.Direction == StockMovementDirection.In ? t.Quantity : -t.Quantity),
        });
        if (request.InStockOnly) rows = rows.Where(x => x.OnHand > 0);
        var count = await rows.LongCountAsync(token);
        var items = await rows.OrderBy(x => x.ItemId).ThenBy(x => x.SerialNo).ThenBy(x => x.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new SerialRow(x.Id, x.ItemId, x.SerialNo, x.OnHand > 0)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<TrackingTrace<LotRow>> LotAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        var lot = await db.Set<StockLot>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(LotNotFound, "Lot", id);
        var (locations, history, truncated) = await TraceAsync(db.Set<StockMovementTracking>().Where(x => x.LotId == id), token);
        return new TrackingTrace<LotRow>(new LotRow(lot.Id, lot.ItemId, lot.LotNo, lot.ManufactureDate, lot.ExpiryDate,
            locations.Sum(x => x.Quantity)), locations, history, truncated);
    }

    public async Task<TrackingTrace<SerialRow>> SerialAsync(long id, CancellationToken token = default)
    {
        RequireCompany();
        var serial = await db.Set<StockSerial>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(SerialNotFound, "Serial", id);
        var (locations, history, truncated) = await TraceAsync(db.Set<StockMovementTracking>().Where(x => x.SerialId == id), token);
        return new TrackingTrace<SerialRow>(new SerialRow(serial.Id, serial.ItemId, serial.SerialNo, locations.Sum(x => x.Quantity) > 0),
            locations, history, truncated);
    }

    private static async Task<(List<TrackedLocation>, List<TrackedMovement>, bool)> TraceAsync(
        IQueryable<StockMovementTracking> rows, CancellationToken token)
    {
        var locations = await rows.GroupBy(x => new { x.Movement!.WarehouseId, x.Movement.LocationId })
            .Select(g => new { g.Key.WarehouseId, g.Key.LocationId,
                Quantity = g.Sum(x => x.Movement!.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity) })
            .Where(x => x.Quantity != 0)
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.LocationId)
            .Select(x => new TrackedLocation(x.WarehouseId, x.LocationId, x.Quantity))
            .ToListAsync(token);
        var history = await rows.OrderByDescending(x => x.Movement!.BusinessDate).ThenByDescending(x => x.MovementId)
            .Take(MaxHistory + 1)
            .Select(x => new TrackedMovement(x.MovementId, x.Movement!.BusinessDate, x.Movement.PostedAt, x.Movement.SourceType,
                x.Movement.SourceId, x.Movement.SourceLineNo, x.Movement.Direction, x.Movement.WarehouseId, x.Movement.LocationId, x.Quantity))
            .ToListAsync(token);
        var truncated = history.Count > MaxHistory;
        if (truncated) history.RemoveAt(MaxHistory);
        return (locations, history, truncated);
    }

    private void Validate(TrackingPageQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.ItemId is <= 0) throw new RequestValidationException("itemId", "Item id must be positive.");
    }

    private static string? Keyword(PagedRequest request) =>
        string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();

    private void RequireCompany()
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
    }
}
