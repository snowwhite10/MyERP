using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>客戶主檔；分類須為同公司客戶分類。</summary>
public sealed class CustomerService(MyErpDbContext db)
    : BusinessPartnerService<Customer, CustomerCategory>(db, "MD.CUSTOMER", "Customer")
{
    protected override Customer New(long companyId, string code, string name, long? categoryId) =>
        new() { CompanyId = companyId, Code = code, Name = name, CategoryId = categoryId };
}

/// <summary>供應商主檔；分類須為同公司供應商分類。</summary>
public sealed class SupplierService(MyErpDbContext db)
    : BusinessPartnerService<Supplier, SupplierCategory>(db, "MD.SUPPLIER", "Supplier")
{
    protected override Supplier New(long companyId, string code, string name, long? categoryId) =>
        new() { CompanyId = companyId, Code = code, Name = name, CategoryId = categoryId };
}

/// <summary>往來對象 CRUD；公司存取權在 HTTP 中介層驗證，查詢再由 EF 公司篩選保護。</summary>
public abstract class BusinessPartnerService<TPartner, TCategory>(MyErpDbContext db, string errorPrefix, string resourceName)
    where TPartner : BusinessPartner<TCategory>
    where TCategory : MasterCategory
{
    public async Task<PagedResult<TPartner>> ListAsync(PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<TPartner>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword));
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

    public async Task<TPartner> CreateAsync(string? code, string? name, long? categoryId, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var normalizedCode = MasterDataInput.Code(code);
        var normalizedName = MasterDataInput.Name(name);
        await ValidateCategoryAsync(categoryId, false, token);
        if (await db.Set<TPartner>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == normalizedCode, token))
            throw Duplicate();
        var entity = New(companyId, normalizedCode, normalizedName, categoryId);
        db.Add(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
        return entity;
    }

    public async Task<TPartner> UpdateAsync(long id, string? name, bool isActive, long? categoryId, int version, CancellationToken token = default)
    {
        RequireCompany();
        var normalizedName = MasterDataInput.Name(name);
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        await ValidateCategoryAsync(categoryId, entity.CategoryId == categoryId, token);
        entity.Name = normalizedName;
        entity.IsActive = isActive;
        entity.CategoryId = categoryId;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        return entity;
    }

    protected abstract TPartner New(long companyId, string code, string name, long? categoryId);

    private async Task ValidateCategoryAsync(long? categoryId, bool allowInactive, CancellationToken token)
    {
        if (categoryId is null) return;
        if (categoryId <= 0 || !await db.Set<TCategory>().AnyAsync(x => x.Id == categoryId && (x.IsActive || allowInactive), token))
            throw new RequestValidationException("categoryId", "Category must be in the current company and active when newly selected.");
    }

    private async Task<TPartner> FindAsync(long id, CancellationToken token) =>
        await db.Set<TPartner>().SingleOrDefaultAsync(x => x.Id == id, token)
        ?? throw new NotFoundException(errorPrefix + ".NOT_FOUND", resourceName, id);

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static void CheckVersion(TPartner entity, int version)
    {
        if (version < 1 || entity.Version != version) throw new ConflictException();
    }

    private ConflictException Duplicate() => new(errorPrefix + ".DUPLICATE_CODE", resourceName + " code already exists in this company.");
}
