using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>物料主檔 CRUD；所有參照必須屬目前公司，重新選擇時必須啟用。</summary>
public sealed class ItemService(MyErpDbContext db)
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
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("name", true) => query.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            ("name", false) => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ("code", true) => query.OrderByDescending(x => x.Code).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Code).ThenBy(x => x.Id),
        };
        var count = await query.LongCountAsync(token);
        return PagedResult.Create(await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(token), request, count);
    }

    public async Task<Item> CreateAsync(string? code, string? name, string? specification,
        long baseUomId, long? brandId, long? categoryId, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var normalizedCode = ValidateCode(code);
        var normalizedName = ValidateName(name);
        var normalizedSpecification = ValidateSpecification(specification);
        await ValidateReferencesAsync(baseUomId, brandId, categoryId, null, token);
        if (await db.Set<Item>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == normalizedCode, token))
            throw Duplicate();
        var entity = new Item { CompanyId = companyId, Code = normalizedCode, Name = normalizedName,
            Specification = normalizedSpecification, BaseUomId = baseUomId, BrandId = brandId, CategoryId = categoryId };
        db.Add(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
        return entity;
    }

    public async Task<Item> UpdateAsync(long id, string? name, string? specification, long baseUomId,
        long? brandId, long? categoryId, bool isActive, int version, CancellationToken token = default)
    {
        RequireCompany();
        var normalizedName = ValidateName(name);
        var normalizedSpecification = ValidateSpecification(specification);
        var entity = await db.Set<Item>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        await ValidateReferencesAsync(baseUomId, brandId, categoryId, entity, token);
        entity.Name = normalizedName;
        entity.Specification = normalizedSpecification;
        entity.BaseUomId = baseUomId;
        entity.BrandId = brandId;
        entity.CategoryId = categoryId;
        entity.IsActive = isActive;
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

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static string ValidateCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > 30 ||
            code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not '_' and not '-'))
            throw new RequestValidationException("code", "Use 1–30 ASCII letters, digits, hyphen or underscore.");
        return code;
    }

    private static string ValidateName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new RequestValidationException("name", "Required; maximum 200 characters.");
        return name;
    }

    private static string? ValidateSpecification(string? value)
    {
        var specification = value?.Trim();
        if (specification?.Length > 500) throw new RequestValidationException("specification", "Maximum 500 characters.");
        return string.IsNullOrEmpty(specification) ? null : specification;
    }

    private static ConflictException Duplicate() => new("MD.ITEM.DUPLICATE_CODE", "Item code already exists in this company.");
}
