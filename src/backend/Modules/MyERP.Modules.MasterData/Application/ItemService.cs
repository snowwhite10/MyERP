using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>
/// 物料主檔 CRUD；所有參照必須屬目前公司，重新選擇時必須啟用。
/// 有專屬換算或被其他模組使用（<see cref="IItemUsage"/>，ADR-049）的物料不可改基本單位。
/// </summary>
public sealed class ItemService(MyErpDbContext db, IEnumerable<IItemUsage>? usages = null)
{
    public async Task<PagedResult<Item>> ListAsync(PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<Item>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword) ||
                (x.Specification != null && x.Specification.Contains(keyword)));
        }
        query = query.OrderByCodeOrName(sorts[0], x => x.Code, x => x.Name);
        var count = await query.LongCountAsync(token);
        return PagedResult.Create(await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(token), request, count);
    }

    /// <summary>單位、品牌、分類的刪除鎖；驗證到保存期間以共享鎖持有，避免引用剛刪除的資料（MD-B03）。</summary>
    internal static readonly string[] ReferencedTables = [CompanyLock.Uom, CompanyLock.ItemBrand, CompanyLock.ItemCategory];

    public async Task<Item> CreateAsync(string? code, string? name, string? specification,
        long baseUomId, long? brandId, long? categoryId, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var normalizedCode = MasterDataInput.Code(code);
        var normalizedName = MasterDataInput.Name(name);
        var normalizedSpecification = MasterDataInput.OptionalText(specification, "specification", 500);
        return await CompanyLock.RunSharedAsync(db, ReferencedTables, companyId, async () =>
        {
            await ValidateReferencesAsync(baseUomId, brandId, categoryId, null, token);
            if (await db.Set<Item>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == normalizedCode, token))
                throw Duplicate();
            var entity = new Item { CompanyId = companyId, Code = normalizedCode, Name = normalizedName,
                Specification = normalizedSpecification, BaseUomId = baseUomId, BrandId = brandId, CategoryId = categoryId };
            db.Add(entity);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (MasterDataQueries.IsUniqueViolation(ex)) { throw Duplicate(); }
            return entity;
        }, token);
    }

    public async Task<Item> UpdateAsync(long id, string? name, string? specification, long baseUomId,
        long? brandId, long? categoryId, bool isActive, int version, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var normalizedName = MasterDataInput.Name(name);
        var normalizedSpecification = MasterDataInput.OptionalText(specification, "specification", 500);
        var entity = await db.Set<Item>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        Task<Item> ApplyAsync() => CompanyLock.RunSharedAsync(db, ReferencedTables, companyId, async () =>
        {
            await ValidateReferencesAsync(baseUomId, brandId, categoryId, entity, token);
            if (entity.BaseUomId != baseUomId && await db.Set<ItemUomConversion>().AnyAsync(x => x.ItemId == id, token))
                throw new ConflictException("MD.ITEM.BASE_UOM_LOCKED", "Delete the item unit conversions before changing the base unit.");
            if (entity.BaseUomId != baseUomId)
                foreach (var usage in usages ?? [])
                    if (await usage.IsInUseAsync(entity.Code, token))
                        throw new ConflictException("MD.ITEM.BASE_UOM_LOCKED", "Items with stock or documents cannot change the base unit.");
            entity.Name = normalizedName;
            entity.Specification = normalizedSpecification;
            entity.BaseUomId = baseUomId;
            entity.BrandId = brandId;
            entity.CategoryId = categoryId;
            entity.IsActive = isActive;
            return await SaveUpdateAsync(entity, version, token);
        }, token);
        if (entity.BaseUomId == baseUomId) return await ApplyAsync();
        // 專屬換算以基本單位為準（ADR-035）：與新增換算共用 applock，有換算時不可改基本單位。
        // 與新增換算的取鎖順序一致：先取換算排他鎖，再取引用共享鎖。
        return await CompanyLock.RunAsync(db, ItemUomConversionService.LockName, companyId, ApplyAsync, token);
    }

    /// <summary>刪除時持有的排他鎖：排除引用端與新增換算、圖片、屬性值到剛刪除的物料（ADR-050）。</summary>
    private static readonly string[] DeleteLocks =
        [CompanyLock.Item, ItemAttributeService.LockName, ItemImageService.LockName, ItemUomConversionService.LockName];

    /// <summary>
    /// 按版本軟刪除物料（REQ_F4118.3.4）；代碼保留不可重用。任一使用方回報已有庫存、交易或單據時回 409 <c>MD.ITEM.IN_USE</c>。
    /// 專屬換算一併軟刪除，圖片與屬性值一併刪除（ADR-050）。
    /// </summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var entity = await db.Set<Item>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        await CompanyLock.RunAsync(db, DeleteLocks, companyId, async () =>
        {
            foreach (var usage in usages ?? [])
                if (await usage.IsInUseAsync(entity.Code, token))
                    throw new ConflictException("MD.ITEM.IN_USE", "Item is referenced by stock, transactions or documents.");
            foreach (var conversion in await db.Set<ItemUomConversion>().Where(x => x.ItemId == id).ToListAsync(token))
                conversion.IsDeleted = true;
            db.RemoveRange(await db.Set<ItemAttributeValue>().Where(x => x.ItemId == id).ToListAsync(token));
            await new ItemImageService(db).RemoveAllAsync(id, token);
            entity.IsDeleted = true;
            entity.IsActive = false;
            await SaveUpdateAsync(entity, version, token);
            return true;
        }, token);
    }

    private async Task<Item> SaveUpdateAsync(Item entity, int version, CancellationToken token)
    {
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        return entity;
    }

    private async Task ValidateReferencesAsync(long uomId, long? brandId, long? categoryId, Item? current, CancellationToken token)
    {
        if (uomId <= 0 || !await db.Set<UnitOfMeasure>().AnyAsync(x => x.Id == uomId && (x.IsActive || current != null && current.BaseUomId == uomId), token))
            throw new RequestValidationException("baseUomId", "Base unit must be active in the current company.");
        if (brandId is not null && (brandId <= 0 || !await db.Set<ItemBrand>().AnyAsync(x => x.Id == brandId && (x.IsActive || current != null && current.BrandId == brandId), token)))
            throw new RequestValidationException("brandId", "Brand must be active in the current company.");
        if (categoryId is not null && (categoryId <= 0 || !await db.Set<ItemCategory>().AnyAsync(x => x.Id == categoryId && (x.IsActive || current != null && current.CategoryId == categoryId), token)))
            throw new RequestValidationException("categoryId", "Category must be active in the current company.");
    }

    private long RequireCompany() => MasterDataQueries.RequireCompany(db);

    private static ConflictException Duplicate() => new("MD.ITEM.DUPLICATE_CODE", "Item code already exists in this company.");
}
