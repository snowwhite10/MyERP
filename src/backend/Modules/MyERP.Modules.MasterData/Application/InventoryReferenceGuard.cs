using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>INV 寫入端驗證並保護 MD 引用；不存取 INV 表。</summary>
public sealed class InventoryReferenceGuard(MyErpDbContext db) : IInventoryReferenceGuard
{
    public Task<T> RunAsync<T>(long itemId, long warehouseId, long? locationId,
        Func<Task<T>> action, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunWithCodesAsync(itemId, warehouseId, locationId, _ => action(), token);
    }

    public Task<T> RunWithCodesAsync<T>(long itemId, long warehouseId, long? locationId,
        Func<InventoryReferenceCodes, Task<T>> action, CancellationToken token = default)
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
            StorageLocation? location = null;
            if (locationId is long id)
            {
                location = await db.Set<StorageLocation>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id && x.WarehouseId == warehouseId, token)
                    ?? throw new NotFoundException("MD.STORAGE_LOCATION.NOT_FOUND", "Storage location", id);
                if (!location.IsActive)
                    throw new BusinessRuleException("MD.STORAGE_LOCATION.INACTIVE", "Storage location is inactive.");
            }
            return await action(new InventoryReferenceCodes(item.Code, warehouse.Code, location?.Code, ItemService.TrackingModeOf(item)));
        }, token);
    }

    public Task RequireWarehousesAsync(IReadOnlyCollection<long> warehouseIds, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(warehouseIds);
        var companyId = MasterDataQueries.RequireCompany(db);
        return CompanyLock.RunSharedAsync(db, [WarehouseService.LockName], companyId, async () =>
        {
            var ids = warehouseIds.Distinct().ToList();
            var found = await db.Set<Warehouse>().AsNoTracking().Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.IsActive }).ToListAsync(token);
            foreach (var id in ids.Order())
            {
                var warehouse = found.SingleOrDefault(x => x.Id == id)
                    ?? throw new NotFoundException("MD.WAREHOUSE.NOT_FOUND", "Warehouse", id);
                if (!warehouse.IsActive)
                    throw new BusinessRuleException("MD.WAREHOUSE.INACTIVE", "Warehouse is inactive.");
            }
            return true;
        }, token);
    }
}

/// <summary>物料類別範圍（含下層）展開；不存取 INV 表。</summary>
public sealed class ItemCategoryScope(MyErpDbContext db) : IItemCategoryScope
{
    public async Task<IReadOnlyList<long>?> ResolveItemsAsync(IReadOnlyCollection<long> categoryIds,
        IReadOnlyCollection<long> itemIds, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        ArgumentNullException.ThrowIfNull(itemIds);
        MasterDataQueries.RequireCompany(db);
        if (categoryIds.Count == 0 && itemIds.Count == 0) return null;

        HashSet<long>? categories = null;
        if (categoryIds.Count > 0)
        {
            var nodes = await db.Set<ItemCategory>().AsNoTracking()
                .Select(x => new { x.Id, x.ParentId, x.IsActive }).ToListAsync(token);
            foreach (var id in categoryIds.Distinct().Order())
            {
                var node = nodes.SingleOrDefault(x => x.Id == id)
                    ?? throw new NotFoundException("MD.ITEM_CATEGORY.NOT_FOUND", "Item category", id);
                if (!node.IsActive)
                    throw new BusinessRuleException("MD.ITEM_CATEGORY.INACTIVE", "Item category is inactive.");
            }
            var children = nodes.Where(x => x.ParentId is not null).ToLookup(x => x.ParentId!.Value, x => x.Id);
            categories = [];
            var pending = new Queue<long>(categoryIds.Distinct());
            while (pending.TryDequeue(out var id))
                if (categories.Add(id))
                    foreach (var child in children[id]) pending.Enqueue(child);
        }

        var query = db.Set<Item>().AsNoTracking().Where(x => x.IsActive);
        if (itemIds.Count > 0)
        {
            var ids = itemIds.Distinct().ToList();
            var found = await db.Set<Item>().AsNoTracking().Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.IsActive }).ToListAsync(token);
            foreach (var id in ids.Order())
            {
                var item = found.SingleOrDefault(x => x.Id == id) ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", id);
                if (!item.IsActive) throw new BusinessRuleException("MD.ITEM.INACTIVE", "Item is inactive.");
            }
            query = query.Where(x => ids.Contains(x.Id));
        }
        if (categories is not null)
        {
            var ids = categories.ToList();
            query = query.Where(x => x.CategoryId != null && ids.Contains(x.CategoryId.Value));
        }
        return await query.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(token);
    }
}

public sealed class ValuationItemScope(MyErpDbContext db) : IValuationItemScope
{
    public async Task<PagedResult<ValuationItemOption>> ListItemsAsync(PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var query = db.Set<Item>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword));
        }
        var sort = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (sort.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var ordered = (sort[0].Field, sort[0].Descending) switch
        {
            ("name", true) => query.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            ("name", false) => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ("code", true) => query.OrderByDescending(x => x.Code).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Code).ThenBy(x => x.Id),
        };
        var count = await query.LongCountAsync(token);
        var rows = await ordered
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new ValuationItemOption(x.Id, x.Code, x.Name)).ToListAsync(token);
        return PagedResult.Create(rows, request, count);
    }

    public async Task RequireItemAsync(long itemId, CancellationToken token = default)
    {
        if (itemId <= 0 || !await db.Set<Item>().AsNoTracking().AnyAsync(x => x.Id == itemId, token))
            throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", itemId);
    }
}
