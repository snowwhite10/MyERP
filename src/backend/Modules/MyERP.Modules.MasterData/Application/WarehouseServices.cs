using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>
/// 倉庫 CRUD（ADR-036）。有未刪除儲位的倉庫不可刪除；刪除與新增儲位共用公司級 applock，
/// 避免並行時儲位掛到剛刪除的倉庫。
/// </summary>
public sealed class WarehouseService(MyErpDbContext db)
    : MasterCategoryService<Warehouse>(db, "MD.WAREHOUSE", "Warehouse")
{
    internal const string LockName = "md_warehouse";

    public async Task<Warehouse> CreateAsync(string? code, string? name, string? address, string? contactName, string? phone,
        CancellationToken token = default)
    {
        var details = ValidateDetails(address, contactName, phone);
        return await CreateCoreAsync(code, name, x => Apply(x, details), token);
    }

    public async Task<Warehouse> UpdateAsync(long id, string? name, string? address, string? contactName, string? phone,
        bool isActive, int version, CancellationToken token = default)
    {
        var details = ValidateDetails(address, contactName, phone);
        return await UpdateCoreAsync(id, name, isActive, version, x => Apply(x, details), token);
    }

    public override Task DeleteAsync(long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, LockName, RequireCompany(), async () =>
        {
            await base.DeleteAsync(id, version, token);
            return true;
        }, token);

    protected override Warehouse New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override IQueryable<Warehouse> FilterByKeyword(IQueryable<Warehouse> query, string keyword) =>
        query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword) ||
            (x.Address != null && x.Address.Contains(keyword)));

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<StorageLocation>().AnyAsync(x => x.WarehouseId == id, token))
            throw new ConflictException(ErrorPrefix + ".HAS_LOCATIONS", "Delete the storage locations before deleting the warehouse.");
    }

    private static (string? Address, string? ContactName, string? Phone) ValidateDetails(
        string? address, string? contactName, string? phone) =>
        (MasterDataInput.OptionalText(address, "address", Warehouse.AddressMaxLength),
         MasterDataInput.OptionalText(contactName, "contactName", Warehouse.ContactNameMaxLength),
         MasterDataInput.OptionalText(phone, "phone", Warehouse.PhoneMaxLength));

    private static void Apply(Warehouse entity, (string? Address, string? ContactName, string? Phone) details) =>
        (entity.Address, entity.ContactName, entity.Phone) = details;
}

/// <summary>儲位 CRUD；儲位屬於一個倉庫，代碼在倉庫內唯一且軟刪除後保留（ADR-036）。</summary>
public sealed class StorageLocationService(MyErpDbContext db)
{
    private const string ErrorPrefix = "MD.STORAGE_LOCATION";

    public async Task<PagedResult<StorageLocation>> ListAsync(long warehouseId, PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["code", "name"], "code");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        await FindWarehouseAsync(warehouseId, token);
        var query = db.Set<StorageLocation>().AsNoTracking().Where(x => x.WarehouseId == warehouseId);
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

    public async Task<StorageLocation> CreateAsync(long warehouseId, string? code, string? name, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        var normalizedCode = MasterDataInput.Code(code);
        var normalizedName = MasterDataInput.Name(name);
        return await CompanyLock.RunAsync(db, WarehouseService.LockName, companyId, async () =>
        {
            var warehouse = await FindWarehouseAsync(warehouseId, token);
            if (!warehouse.IsActive)
                throw new BusinessRuleException(ErrorPrefix + ".WAREHOUSE_INACTIVE", "Storage locations can only be added to an active warehouse.");
            if (await db.Set<StorageLocation>().IgnoreQueryFilters().AnyAsync(
                x => x.CompanyId == companyId && x.WarehouseId == warehouseId && x.Code == normalizedCode, token))
                throw Duplicate();
            var entity = new StorageLocation { CompanyId = companyId, WarehouseId = warehouseId, Code = normalizedCode, Name = normalizedName };
            db.Add(entity);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
            return entity;
        }, token);
    }

    /// <summary>按版本修改名稱與啟用狀態；代碼與所屬倉庫不可修改。</summary>
    public async Task<StorageLocation> UpdateAsync(long warehouseId, long id, string? name, bool isActive, int version,
        CancellationToken token = default)
    {
        RequireCompany();
        var normalizedName = MasterDataInput.Name(name);
        var entity = await FindAsync(warehouseId, id, version, token);
        entity.Name = normalizedName;
        entity.IsActive = isActive;
        await SaveUpdateAsync(entity, version, token);
        return entity;
    }

    public async Task DeleteAsync(long warehouseId, long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(warehouseId, id, version, token);
        entity.IsDeleted = true;
        await SaveUpdateAsync(entity, version, token);
    }

    private async Task<Warehouse> FindWarehouseAsync(long warehouseId, CancellationToken token) =>
        await db.Set<Warehouse>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == warehouseId, token)
        ?? throw new NotFoundException("MD.WAREHOUSE.NOT_FOUND", "Warehouse", warehouseId);

    private async Task<StorageLocation> FindAsync(long warehouseId, long id, int version, CancellationToken token)
    {
        var entity = await db.Set<StorageLocation>().SingleOrDefaultAsync(x => x.Id == id && x.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException(ErrorPrefix + ".NOT_FOUND", "Storage location", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        return entity;
    }

    private async Task SaveUpdateAsync(StorageLocation entity, int version, CancellationToken token)
    {
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
    }

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static ConflictException Duplicate() =>
        new(ErrorPrefix + ".DUPLICATE_CODE", "Storage location code already exists in this warehouse.");
}
