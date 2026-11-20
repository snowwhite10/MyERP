using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Application;

/// <summary>
/// Available lots/serials at one warehouse/location (ADR-152), summed from <c>inv_stock_movement_tracking</c> in the
/// same way as <see cref="TrackingQueryService"/>; the company filter applies automatically.
/// </summary>
public sealed class StockTrackingAvailabilityQuery(MyErpDbContext db) : IStockTrackingAvailabilityQuery
{
    public async Task<AvailableTrackingNumbers> LotsAsync(long itemId, long warehouseId, long? locationId, string? keyword,
        int limit, CancellationToken token = default)
    {
        var term = Validate(itemId, warehouseId, locationId, keyword, limit, StockLot.MaxLength);
        var lots = db.Set<StockLot>().AsNoTracking().Where(x => x.ItemId == itemId);
        if (term is not null) lots = lots.Where(x => x.LotNo.Contains(term));
        var rows = await lots.Select(x => new
            {
                x.Id, x.LotNo, x.ManufactureDate, x.ExpiryDate,
                OnHand = db.Set<StockMovementTracking>()
                    .Where(t => t.LotId == x.Id && t.Movement!.WarehouseId == warehouseId && t.Movement.LocationId == locationId)
                    .Sum(t => t.Movement!.Direction == StockMovementDirection.In ? t.Quantity : -t.Quantity),
            })
            .Where(x => x.OnHand > 0)
            .OrderBy(x => x.ExpiryDate == null).ThenBy(x => x.ExpiryDate).ThenBy(x => x.LotNo).ThenBy(x => x.Id)
            .Take(limit + 1).ToListAsync(token);
        return Page([.. rows.Select(x => new AvailableTrackingNumber(x.LotNo, x.OnHand, x.ManufactureDate, x.ExpiryDate))], limit);
    }

    public async Task<AvailableTrackingNumbers> SerialsAsync(long itemId, long warehouseId, long? locationId, string? keyword,
        int limit, CancellationToken token = default)
    {
        var term = Validate(itemId, warehouseId, locationId, keyword, limit, StockSerial.MaxLength);
        var serials = db.Set<StockSerial>().AsNoTracking().Where(x => x.ItemId == itemId);
        if (term is not null) serials = serials.Where(x => x.SerialNo.Contains(term));
        var rows = await serials.Select(x => new
            {
                x.Id, x.SerialNo,
                OnHand = db.Set<StockMovementTracking>()
                    .Where(t => t.SerialId == x.Id && t.Movement!.WarehouseId == warehouseId && t.Movement.LocationId == locationId)
                    .Sum(t => t.Movement!.Direction == StockMovementDirection.In ? t.Quantity : -t.Quantity),
            })
            .Where(x => x.OnHand > 0)
            .OrderBy(x => x.SerialNo).ThenBy(x => x.Id)
            .Take(limit + 1).ToListAsync(token);
        return Page([.. rows.Select(x => new AvailableTrackingNumber(x.SerialNo, 1m, null, null))], limit);
    }

    private static AvailableTrackingNumbers Page(List<AvailableTrackingNumber> rows, int limit) =>
        rows.Count > limit ? new AvailableTrackingNumbers(rows[..limit], true) : new AvailableTrackingNumbers(rows, false);

    private string? Validate(long itemId, long warehouseId, long? locationId, string? keyword, int limit, int maxKeyword)
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (itemId <= 0) errors["itemId"] = ["Item id must be positive."];
        if (warehouseId <= 0) errors["warehouseId"] = ["Warehouse id must be positive."];
        if (locationId is <= 0) errors["locationId"] = ["Location id must be positive."];
        if (limit is < 1 or > IStockTrackingAvailabilityQuery.MaxLimit)
            errors["limit"] = [$"Limit must be 1 to {IStockTrackingAvailabilityQuery.MaxLimit}."];
        var term = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        if (term?.Length > maxKeyword) errors["keyword"] = [$"Keyword must be at most {maxKeyword} characters."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return term;
    }
}
