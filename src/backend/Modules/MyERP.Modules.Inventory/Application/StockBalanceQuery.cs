using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Application;

/// <summary>以異動帳即時加總結存（公司篩選自動套用）；索引 ix_inv_stock_movement_balance 覆蓋分組欄位。</summary>
public sealed class StockBalanceQuery(MyErpDbContext db) : IStockBalanceQuery
{
    public async Task<decimal> GetOnHandAsync(long itemId, long warehouseId, long? locationId, CancellationToken token = default) =>
        await Signed(db.Set<StockMovement>().Where(x => x.ItemId == itemId && x.WarehouseId == warehouseId && x.LocationId == locationId))
            .SumAsync(token);

    public async Task<IReadOnlyList<StockBalance>> ListAsync(long? itemId = null, long? warehouseId = null, CancellationToken token = default)
    {
        var movements = db.Set<StockMovement>().AsQueryable();
        if (itemId is long item) movements = movements.Where(x => x.ItemId == item);
        if (warehouseId is long warehouse) movements = movements.Where(x => x.WarehouseId == warehouse);
        return await movements
            .GroupBy(x => new { x.ItemId, x.WarehouseId, x.LocationId })
            .Select(g => new
            {
                g.Key.ItemId, g.Key.WarehouseId, g.Key.LocationId,
                Quantity = g.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity),
            })
            .Where(x => x.Quantity != 0)
            .OrderBy(x => x.ItemId).ThenBy(x => x.WarehouseId).ThenBy(x => x.LocationId)
            .Select(x => new StockBalance(x.ItemId, x.WarehouseId, x.LocationId, x.Quantity))
            .ToListAsync(token);
    }

    internal static IQueryable<decimal> Signed(IQueryable<StockMovement> movements) =>
        movements.Select(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity);
}
