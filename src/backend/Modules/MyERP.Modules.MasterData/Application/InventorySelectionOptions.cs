using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

public sealed class InventorySelectionOptions(MyErpDbContext db) : IInventorySelectionOptions
{
    public async Task<IReadOnlyList<long>> SearchSavedItemIdsAsync(string keyword, int page, int pageSize,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var request = new PagedRequest { Page = page, PageSize = pageSize };
        request.EnsureValid();
        var term = keyword.Trim();
        return await db.Set<Item>().AsNoTracking()
            .Where(x => x.Code.Contains(term) || x.Name.Contains(term))
            .OrderBy(x => x.Id).Skip(request.Skip).Take(pageSize)
            .Select(x => x.Id).ToListAsync(token);
    }

    public async Task<IReadOnlyList<InventorySelectionOption>> SavedItemsByCodeAsync(int page, int pageSize,
        bool descending, CancellationToken token = default)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize };
        request.EnsureValid();
        var items = db.Set<Item>().AsNoTracking();
        var ordered = descending ? items.OrderByDescending(x => x.Code).ThenBy(x => x.Id)
            : items.OrderBy(x => x.Code).ThenBy(x => x.Id);
        return await ordered.Skip(request.Skip).Take(pageSize)
            .Select(x => new InventorySelectionOption(x.Id, x.Code, x.Name, x.TrackingMode, x.IsActive))
            .ToListAsync(token);
    }

    public Task<PagedResult<InventorySelectionOption>> ItemsAsync(PagedRequest request, CancellationToken token = default) =>
        PageAsync(db.Set<Item>().AsNoTracking().Where(x => x.IsActive), request,
            x => new InventorySelectionOption(x.Id, x.Code, x.Name, x.TrackingMode), token);

    public Task<PagedResult<InventorySelectionOption>> WarehousesAsync(PagedRequest request, CancellationToken token = default) =>
        PageAsync(db.Set<Warehouse>().AsNoTracking().Where(x => x.IsActive), request,
            x => new InventorySelectionOption(x.Id, x.Code, x.Name), token);

    public async Task<PagedResult<InventorySelectionOption>> LocationsAsync(long warehouseId, PagedRequest request, CancellationToken token = default)
    {
        if (warehouseId <= 0 || !await db.Set<Warehouse>().AsNoTracking().AnyAsync(x => x.Id == warehouseId && x.IsActive, token))
            throw new NotFoundException("MD.WAREHOUSE.NOT_FOUND", "Warehouse", warehouseId);
        return await PageAsync(db.Set<StorageLocation>().AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.IsActive), request,
            x => new InventorySelectionOption(x.Id, x.Code, x.Name), token);
    }

    public async Task<InventorySavedReferences> SavedAsync(IReadOnlyCollection<long> itemIds,
        IReadOnlyCollection<long> warehouseIds, IReadOnlyCollection<long> locationIds, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentNullException.ThrowIfNull(warehouseIds);
        ArgumentNullException.ThrowIfNull(locationIds);
        var items = await db.Set<Item>().AsNoTracking().Where(x => itemIds.Contains(x.Id))
            .Select(x => new InventorySelectionOption(x.Id, x.Code, x.Name, x.TrackingMode, x.IsActive)).ToListAsync(token);
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(x => warehouseIds.Contains(x.Id))
            .Select(x => new InventorySelectionOption(x.Id, x.Code, x.Name, null, x.IsActive)).ToListAsync(token);
        var locations = await db.Set<StorageLocation>().AsNoTracking().Where(x => locationIds.Contains(x.Id))
            .Select(x => new InventorySelectionOption(x.Id, x.Code, x.Name, null, x.IsActive)).ToListAsync(token);
        return new InventorySavedReferences(items, warehouses, locations);
    }

    private static async Task<PagedResult<InventorySelectionOption>> PageAsync<T>(
        IQueryable<T> source, PagedRequest request,
        System.Linq.Expressions.Expression<Func<T, InventorySelectionOption>> selector, CancellationToken token) where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (request.Sort is not null && request.Sort != "code")
            throw new RequestValidationException("sort", "Only code ordering is supported.");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            source = source.Where(x => EF.Property<string>(x, "Code").Contains(keyword)
                || EF.Property<string>(x, "Name").Contains(keyword));
        }
        var count = await source.LongCountAsync(token);
        var rows = await source.OrderBy(x => EF.Property<string>(x, "Code"))
            .ThenBy(x => EF.Property<long>(x, "Id"))
            .Skip(request.Skip).Take(request.PageSize).Select(selector).ToListAsync(token);
        return PagedResult.Create(rows, request, count);
    }
}
