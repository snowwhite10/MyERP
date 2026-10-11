using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>物料專屬換算列表項目：1 個 UomCode = Factor 個物料基本單位。</summary>
public sealed record ItemUomConversionView(long Id, long ItemId, long UomId, string UomCode, decimal Factor, int Version);

/// <summary>
/// 物料專屬換算維護（ADR-035）。每列連到物料基本單位，同一物料同一單位只有一列；
/// 新增時與「修改基本單位」共用公司級 applock，避免換算掛在舊的基本單位上。
/// </summary>
public sealed class ItemUomConversionService(MyErpDbContext db)
{
    internal const string LockName = "md_item_uom_conversion";
    private const string ErrorPrefix = "MD.ITEM_UOM_CONVERSION";

    public async Task<IReadOnlyList<ItemUomConversionView>> ListAsync(long itemId, CancellationToken token = default)
    {
        RequireCompany();
        await FindItemAsync(itemId, token);
        return await db.Set<ItemUomConversion>().AsNoTracking().Where(x => x.ItemId == itemId)
            .OrderBy(x => x.Uom!.Code)
            .Select(x => new ItemUomConversionView(x.Id, x.ItemId, x.UomId, x.Uom!.Code, x.Factor, x.Version))
            .ToListAsync(token);
    }

    public async Task<ItemUomConversionView> CreateAsync(long itemId, long uomId, decimal factor, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        UomConversionService.ValidateFactor(factor);
        var uom = await db.Set<UnitOfMeasure>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == uomId && x.IsActive, token);
        var entity = await CompanyLock.RunAsync(db, LockName, companyId, async () =>
        {
            var item = await FindItemAsync(itemId, token);
            if (uom is null || uom.Id == item.BaseUomId)
                throw new RequestValidationException("uomId", "Choose an active unit of the current company other than the base unit.");
            if (await db.Set<ItemUomConversion>().AnyAsync(x => x.ItemId == itemId && x.UomId == uomId, token))
                throw Duplicate();
            var created = new ItemUomConversion { CompanyId = companyId, ItemId = itemId, UomId = uomId, Factor = factor };
            db.Add(created);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
            return created;
        }, token);
        return new ItemUomConversionView(entity.Id, entity.ItemId, entity.UomId, uom!.Code, entity.Factor, entity.Version);
    }

    public async Task<ItemUomConversionView> UpdateAsync(long itemId, long id, decimal factor, int version, CancellationToken token = default)
    {
        RequireCompany();
        UomConversionService.ValidateFactor(factor);
        var entity = await FindAsync(itemId, id, version, token);
        entity.Factor = factor;
        await SaveUpdateAsync(entity, version, token);
        return new ItemUomConversionView(entity.Id, entity.ItemId, entity.UomId, entity.Uom!.Code, entity.Factor, entity.Version);
    }

    public async Task DeleteAsync(long itemId, long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(itemId, id, version, token);
        entity.IsDeleted = true;
        await SaveUpdateAsync(entity, version, token);
    }

    private async Task<Item> FindItemAsync(long itemId, CancellationToken token) =>
        await db.Set<Item>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId, token)
        ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", itemId);

    private async Task<ItemUomConversion> FindAsync(long itemId, long id, int version, CancellationToken token)
    {
        var entity = await db.Set<ItemUomConversion>().Include(x => x.Uom)
            .SingleOrDefaultAsync(x => x.Id == id && x.ItemId == itemId, token)
            ?? throw new NotFoundException(ErrorPrefix + ".NOT_FOUND", "Item unit conversion", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        return entity;
    }

    private async Task SaveUpdateAsync(ItemUomConversion entity, int version, CancellationToken token)
    {
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
    }

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static ConflictException Duplicate() =>
        new(ErrorPrefix + ".DUPLICATE", "This item already has a conversion for the unit.");
}
