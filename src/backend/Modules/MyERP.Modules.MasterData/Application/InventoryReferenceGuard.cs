using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>INV 寫入端驗證並保護 MD 引用；不存取 INV 表。</summary>
public sealed class InventoryReferenceGuard(MyErpDbContext db) : IInventoryReferenceGuard
{
    public Task<T> RunAsync<T>(long itemId, long warehouseId, long? locationId,
        Func<Task<T>> action, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var companyId = MasterDataQueries.RequireCompany(db);
        return CompanyLock.RunSharedAsync(db, [CompanyLock.Item, WarehouseService.LockName], companyId, async () =>
        {
            var item = await db.Set<Item>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId, token)
                ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", itemId);
            if (!item.IsActive)
                throw new BusinessRuleException("MD.ITEM.INACTIVE", "Item is inactive.");
            var warehouse = await db.Set<Warehouse>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == warehouseId, token)
                ?? throw new NotFoundException("MD.WAREHOUSE.NOT_FOUND", "Warehouse", warehouseId);
            if (!warehouse.IsActive)
                throw new BusinessRuleException("MD.WAREHOUSE.INACTIVE", "Warehouse is inactive.");
            if (locationId is long id)
            {
                var location = await db.Set<StorageLocation>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id && x.WarehouseId == warehouseId, token)
                    ?? throw new NotFoundException("MD.STORAGE_LOCATION.NOT_FOUND", "Storage location", id);
                if (!location.IsActive)
                    throw new BusinessRuleException("MD.STORAGE_LOCATION.INACTIVE", "Storage location is inactive.");
            }
            return await action();
        }, token);
    }
}
