using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>客戶分類 CRUD；已被客戶引用的分類只可停用。</summary>
public sealed class CustomerCategoryService(MyErpDbContext db)
    : MasterCategoryService<CustomerCategory>(db, "MD.CUSTOMER_CATEGORY", "Customer category")
{
    protected override CustomerCategory New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<Customer>().AnyAsync(x => x.CategoryId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Category is referenced by customers.");
    }
}

/// <summary>供應商分類 CRUD；已被供應商引用的分類只可停用。</summary>
public sealed class SupplierCategoryService(MyErpDbContext db)
    : MasterCategoryService<SupplierCategory>(db, "MD.SUPPLIER_CATEGORY", "Supplier category")
{
    protected override SupplierCategory New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<Supplier>().AnyAsync(x => x.CategoryId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Category is referenced by suppliers.");
    }
}

/// <summary>物料品牌 CRUD；已被物料引用的品牌只可停用。</summary>
public sealed class ItemBrandService(MyErpDbContext db)
    : MasterCategoryService<ItemBrand>(db, "MD.ITEM_BRAND", "Item brand")
{
    protected override ItemBrand New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<Item>().AnyAsync(x => x.BrandId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Brand is referenced by items.");
    }
}

/// <summary>
/// 物料分類樹 CRUD。上層必須是同公司、未刪除且啟用的分類，不可成環，最多 <see cref="ItemCategory.MaxDepth"/> 層；
/// 有子分類不可刪除。掛載與刪除以公司級 applock 序列化，避免並行搬移形成循環或掛到剛刪除的分類下。
/// </summary>
public sealed class ItemCategoryService(MyErpDbContext db)
    : MasterCategoryService<ItemCategory>(db, "MD.ITEM_CATEGORY", "Item category")
{
    private const string LockTable = "md_item_category";

    /// <summary>目前公司全部未刪除分類（含停用），按代碼排序；前端自行組樹。</summary>
    public async Task<IReadOnlyList<ItemCategory>> TreeAsync(CancellationToken token = default)
    {
        RequireCompany();
        var nodes = await Db.Set<ItemCategory>().AsNoTracking().ToListAsync(token);
        return [.. nodes.OrderBy(x => x.Code, StringComparer.Ordinal)];
    }

    public async Task<ItemCategory> CreateAsync(string? code, string? name, long? parentId, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        return await CompanyLock.RunAsync(Db, LockTable, companyId, async () =>
        {
            if (parentId is not null) await CheckPlacementAsync(null, parentId.Value, token);
            return await CreateCoreAsync(code, name, x => x.ParentId = parentId, token);
        }, token);
    }

    /// <summary>按版本修改名稱、啟用狀態與上層；上層不變時（即使已停用）不重新檢查。</summary>
    public async Task<ItemCategory> UpdateAsync(
        long id, string? name, bool isActive, long? parentId, int version, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        return await CompanyLock.RunAsync(Db, LockTable, companyId, async () =>
        {
            var entity = await FindAsync(id, token);
            if (parentId is not null && parentId != entity.ParentId) await CheckPlacementAsync(id, parentId.Value, token);
            return await UpdateCoreAsync(id, name, isActive, version, x => x.ParentId = parentId, token);
        }, token);
    }

    public override Task DeleteAsync(long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, LockTable, RequireCompany(), async () =>
        {
            await base.DeleteAsync(id, version, token);
            return true;
        }, token);

    protected override ItemCategory New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<ItemCategory>().AnyAsync(x => x.ParentId == id, token))
            throw new ConflictException(ErrorPrefix + ".HAS_CHILDREN", "Category has child categories.");
        if (await Db.Set<Item>().AnyAsync(x => x.CategoryId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Category is referenced by items.");
    }

    private async Task CheckPlacementAsync(long? nodeId, long parentId, CancellationToken token)
    {
        if (parentId == nodeId) throw new BusinessRuleException(ErrorPrefix + ".CYCLE", "A category cannot be its own parent.");
        var nodes = await Db.Set<ItemCategory>().AsNoTracking()
            .Select(x => new { x.Id, x.ParentId, x.IsActive }).ToListAsync(token);
        if (!nodes.Any(x => x.Id == parentId && x.IsActive))
            throw new BusinessRuleException(ErrorPrefix + ".INVALID_PARENT", "Parent category must be an active category of this company.");
        switch (ItemCategoryTree.Check(nodes.ToDictionary(x => x.Id, x => x.ParentId), nodeId, parentId))
        {
            case ItemCategoryPlacement.Cycle:
                throw new BusinessRuleException(ErrorPrefix + ".CYCLE", "A category cannot be moved under its own descendant.");
            case ItemCategoryPlacement.TooDeep:
                throw new BusinessRuleException(ErrorPrefix + ".TOO_DEEP", $"Category tree is limited to {ItemCategory.MaxDepth} levels.");
        }
    }
}

/// <summary>扁平分類 CRUD；公司存取權在 HTTP 中介層驗證，查詢再由 EF 公司篩選保護。</summary>
public abstract class MasterCategoryService<TCategory>(MyErpDbContext db, string errorPrefix, string resourceName)
    where TCategory : MasterCategory
{
    protected MyErpDbContext Db => db;
    protected string ErrorPrefix => errorPrefix;

    public async Task<PagedResult<TCategory>> ListAsync(PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var sort = sorts[0];
        var query = db.Set<TCategory>().AsNoTracking().AsQueryable();
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

    public Task<TCategory> CreateAsync(string? code, string? name, CancellationToken token = default) =>
        CreateCoreAsync(code, name, null, token);

    public Task<TCategory> UpdateAsync(long id, string? name, bool isActive, int version, CancellationToken token = default) =>
        UpdateCoreAsync(id, name, isActive, version, null, token);

    /// <summary>建立並在儲存前以 <paramref name="configure"/> 設定子類欄位（呼叫前須先驗證）。</summary>
    protected async Task<TCategory> CreateCoreAsync(
        string? code, string? name, Action<TCategory>? configure, CancellationToken token)
    {
        var companyId = RequireCompany();
        var normalizedCode = ValidateCode(code);
        var normalizedName = ValidateName(name);
        if (await db.Set<TCategory>().IgnoreQueryFilters().AnyAsync(
            x => x.CompanyId == companyId && x.Code == normalizedCode, token))
            throw Duplicate();
        var entity = New(companyId, normalizedCode, normalizedName);
        configure?.Invoke(entity);
        db.Add(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
        return entity;
    }

    /// <summary>按版本修改名稱、啟用狀態，並以 <paramref name="apply"/> 修改子類欄位（呼叫前須先驗證）。</summary>
    protected async Task<TCategory> UpdateCoreAsync(
        long id, string? name, bool isActive, int version, Action<TCategory>? apply, CancellationToken token)
    {
        RequireCompany();
        var normalizedName = ValidateName(name);
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        entity.Name = normalizedName;
        entity.IsActive = isActive;
        apply?.Invoke(entity);
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(token);
        return entity;
    }

    public virtual async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        await EnsureNotInUseAsync(id, token);
        entity.IsDeleted = true;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(token);
    }

    protected abstract TCategory New(long companyId, string code, string name);

    /// <summary>刪除前檢查同公司主檔引用；有引用時拋出 <c>{prefix}.IN_USE</c> 409。</summary>
    protected virtual Task EnsureNotInUseAsync(long id, CancellationToken token) => Task.CompletedTask;

    protected async Task<TCategory> FindAsync(long id, CancellationToken token) =>
        await db.Set<TCategory>().SingleOrDefaultAsync(x => x.Id == id, token)
        ?? throw new NotFoundException(errorPrefix + ".NOT_FOUND", resourceName, id);

    protected long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static void CheckVersion(TCategory entity, int version)
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

    private ConflictException Duplicate() => new(errorPrefix + ".DUPLICATE_CODE", "Category code already exists in this company.");
}
