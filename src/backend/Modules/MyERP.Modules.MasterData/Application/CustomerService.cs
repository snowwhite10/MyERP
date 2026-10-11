using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

public sealed class CustomerService(MyErpDbContext db)
{
    public async Task<PagedResult<Customer>> ListAsync(PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<Customer>().AsNoTracking().AsQueryable();
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

    public async Task<Customer> CreateAsync(string? code, string? name, long? categoryId, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var normalizedCode = ValidateCode(code);
        var normalizedName = ValidateName(name);
        await ValidateCategoryAsync(categoryId, false, token);
        if (await db.Set<Customer>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == normalizedCode, token))
            throw Duplicate();
        var entity = new Customer { CompanyId = companyId, Code = normalizedCode, Name = normalizedName, CategoryId = categoryId };
        db.Add(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
        return entity;
    }

    public async Task<Customer> UpdateAsync(long id, string? name, bool isActive, long? categoryId, int version, CancellationToken token = default)
    {
        RequireCompany();
        var normalizedName = ValidateName(name);
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

    private async Task ValidateCategoryAsync(long? categoryId, bool allowInactive, CancellationToken token)
    {
        if (categoryId is null) return;
        if (categoryId <= 0 || !await db.Set<CustomerCategory>().AnyAsync(x => x.Id == categoryId && (x.IsActive || allowInactive), token))
            throw new RequestValidationException("categoryId", "Category must be in the current company and active when newly selected.");
    }

    private async Task<Customer> FindAsync(long id, CancellationToken token) =>
        await db.Set<Customer>().SingleOrDefaultAsync(x => x.Id == id, token)
        ?? throw new NotFoundException("MD.CUSTOMER.NOT_FOUND", "Customer", id);

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static void CheckVersion(Customer entity, int version)
    {
        if (version < 1 || entity.Version != version) throw new ConflictException();
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

    private static ConflictException Duplicate() => new("MD.CUSTOMER.DUPLICATE_CODE", "Customer code already exists in this company.");
}
