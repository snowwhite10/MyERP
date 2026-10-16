using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Inventory.Application;

public sealed record SafetyStockQuery : PagedRequest
{
    public long? WarehouseId { get; init; }
    public bool LowOnly { get; init; }
}

public sealed record SafetyStockRow(long Id, long ItemId, long WarehouseId, decimal MinimumQuantity,
    decimal OnHand, bool IsLow, decimal Shortage, int Version);
public sealed record SafetyStockPage(IReadOnlyList<SafetyStockRow> Items, int Page, int PageSize,
    long TotalCount, DateTime QueriedAt);

/// <summary>REQ_F4115.26.1–3：門檻寫入與依已過帳異動帳計算低量。</summary>
public sealed class SafetyStockService(MyErpDbContext db, IInventoryReferenceGuard guard, IClock clock,
    ICurrentUser user)
{
    private const decimal MaxQuantity = 10_000_000_000_000m;
    private static readonly string Duplicate = "INV.SAFETY_STOCK.DUPLICATE";
    private static readonly string MissingCode = "INV.SAFETY_STOCK.NOT_FOUND";

    public async Task<SafetyStockPage> ListAsync(SafetyStockQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        if (request.WarehouseId is <= 0) throw new RequestValidationException("warehouseId", "Warehouse id must be positive.");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            throw new RequestValidationException("keyword", "Keyword search is not available yet.");
        if (!string.IsNullOrWhiteSpace(request.Sort))
            throw new RequestValidationException("sort", "Unsupported sort field.");
        var queriedAt = clock.LocalNow;
        var thresholds = db.Set<SafetyStockThreshold>().AsNoTracking();
        if (request.WarehouseId is long warehouseId)
            thresholds = thresholds.Where(x => x.WarehouseId == warehouseId);
        var query = thresholds.Select(threshold => new { threshold,
            OnHand = db.Set<StockMovement>().Where(m => m.ItemId == threshold.ItemId
                && m.WarehouseId == threshold.WarehouseId)
                .Sum(m => m.Direction == StockMovementDirection.In ? m.Quantity : -m.Quantity) });
        if (request.LowOnly) query = query.Where(x => x.OnHand < x.threshold.MinimumQuantity);
        var total = await query.LongCountAsync(token);
        var rows = await query.OrderBy(x => x.threshold.ItemId).ThenBy(x => x.threshold.WarehouseId)
            .ThenBy(x => x.threshold.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(x => new SafetyStockRow(x.threshold.Id, x.threshold.ItemId, x.threshold.WarehouseId,
                x.threshold.MinimumQuantity, x.OnHand, x.OnHand < x.threshold.MinimumQuantity,
                x.OnHand < x.threshold.MinimumQuantity ? x.threshold.MinimumQuantity - x.OnHand : 0m,
                x.threshold.Version)).ToListAsync(token);
        return new SafetyStockPage(rows, request.Page, request.PageSize, total, queriedAt);
    }

    public async Task<SafetyStockThreshold> CreateAsync(long itemId, long warehouseId, decimal quantity,
        CancellationToken token = default) =>
        (await CreateWithStatusAsync(itemId, warehouseId, quantity, token)).Entity;

    public async Task<(SafetyStockThreshold Entity, bool Restored)> CreateWithStatusAsync(long itemId, long warehouseId, decimal quantity,
        CancellationToken token = default)
    {
        var companyId = RequireCompany();
        Validate(itemId, warehouseId, quantity);
        return await guard.RunAsync(itemId, warehouseId, null, async () =>
        {
            try
            {
                var existing = await db.Set<SafetyStockThreshold>().IgnoreQueryFilters()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ItemId == itemId
                        && x.WarehouseId == warehouseId, token);
                if (existing is { IsDeleted: false })
                    throw new ConflictException(Duplicate, "A safety stock threshold already exists.");
                var entity = existing ?? new SafetyStockThreshold
                    { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId };
                entity.MinimumQuantity = quantity;
                entity.IsDeleted = false;
                entity.DeletedAt = null;
                entity.DeletedBy = null;
                if (existing is null) db.Add(entity);
                await db.SaveChangesAsync(token);
                return (entity, existing is not null);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                db.ChangeTracker.Clear();
                throw new ConflictException(Duplicate, "A safety stock threshold already exists.");
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                throw new ConflictException();
            }
        }, token);
    }

    public async Task<SafetyStockThreshold> UpdateAsync(long id, int version, decimal quantity,
        CancellationToken token = default)
    {
        RequireCompany();
        ValidateQuantity(quantity);
        var current = await LoadAsync(id, version, token);
        return await guard.RunAsync(current.ItemId, current.WarehouseId, null, async () =>
        {
            current.MinimumQuantity = quantity;
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new ConflictException(); }
            return current;
        }, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var current = await LoadAsync(id, version, token);
        current.IsDeleted = true;
        current.DeletedAt = clock.LocalNow;
        current.DeletedBy = user.UserId ?? SystemUser.Id;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new ConflictException(); }
    }

    private async Task<SafetyStockThreshold> LoadAsync(long id, int version, CancellationToken token)
    {
        var entity = await db.Set<SafetyStockThreshold>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(MissingCode, "Safety stock threshold", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        return entity;
    }

    private long RequireCompany() => db.CurrentCompanyId is long id && id > 0 ? id :
        throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
    private static void Validate(long itemId, long warehouseId, decimal quantity)
    {
        if (itemId <= 0) throw new RequestValidationException("itemId", "Item is required.");
        if (warehouseId <= 0) throw new RequestValidationException("warehouseId", "Warehouse is required.");
        ValidateQuantity(quantity);
    }
    private static void ValidateQuantity(decimal quantity)
    {
        if (quantity <= 0 || quantity >= MaxQuantity || decimal.Round(quantity, 6) != quantity)
            throw new RequestValidationException("minimumQuantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
    }
}
