using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>客戶分類 CRUD；公司存取權在 HTTP 中介層驗證，查詢再由 EF 公司篩選保護。</summary>
public sealed class CustomerCategoryService(MyErpDbContext db)
{
    public async Task<PagedResult<CustomerCategory>> ListAsync(PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var sort = sorts[0];
        var query = db.Set<CustomerCategory>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword));
        }
        query = (sort.Field, sort.Descending) switch
        {
            ("name", true) => query.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            ("name", false) => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ("code", true) => query.OrderByDescending(x => x.Code).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Code).ThenBy(x => x.Id),
        };
        var count = await query.LongCountAsync(token);
        return PagedResult.Create(await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(token), request, count);
    }

    public async Task<CustomerCategory> CreateAsync(string? code, string? name, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var normalizedCode = ValidateCode(code);
        var normalizedName = ValidateName(name);
        if (await db.Set<CustomerCategory>().IgnoreQueryFilters().AnyAsync(
            x => x.CompanyId == companyId && x.Code == normalizedCode, token))
            throw Duplicate();
        var entity = new CustomerCategory { CompanyId = companyId, Code = normalizedCode, Name = normalizedName };
        db.Add(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
        return entity;
    }

    public async Task<CustomerCategory> UpdateAsync(long id, string? name, bool isActive, int version, CancellationToken token = default)
    {
        RequireCompany();
        var normalizedName = ValidateName(name);
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        entity.Name = normalizedName;
        entity.IsActive = isActive;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(token);
        return entity;
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        if (await db.Set<Customer>().AnyAsync(x => x.CategoryId == id, token))
            throw new ConflictException("MD.CUSTOMER_CATEGORY.IN_USE", "Category is referenced by customers.");
        entity.IsDeleted = true;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(token);
    }

    private async Task<CustomerCategory> FindAsync(long id, CancellationToken token) =>
        await db.Set<CustomerCategory>().SingleOrDefaultAsync(x => x.Id == id, token)
        ?? throw new NotFoundException("MD.CUSTOMER_CATEGORY.NOT_FOUND", "Customer category", id);

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static void CheckVersion(CustomerCategory entity, int version)
    {
        if (version < 1 || entity.Version != version) throw new ConflictException();
    }

    private async Task SaveUpdateAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
    }

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

    private static ConflictException Duplicate() => new("MD.CUSTOMER_CATEGORY.DUPLICATE_CODE", "Category code already exists in this company.");
}
