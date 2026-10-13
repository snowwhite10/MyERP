using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>客戶主檔；分類須為同公司客戶分類。</summary>
public sealed class CustomerService(MyErpDbContext db, IEnumerable<ICustomerUsage>? usage = null,
    IApprovalWorkflowService? workflow = null)
    : BusinessPartnerService<Customer, CustomerCategory>(db, CustomerApprovalService.DocumentType, "Customer", workflow), ICustomerLookup
{
    protected override Customer New(long companyId, string code, string name, long? categoryId) =>
        new() { CompanyId = companyId, Code = code, Name = name, CategoryId = categoryId };

    protected override string CategoryLock => CompanyLock.CustomerCategory;

    public async Task<CustomerIdentity> RequireApprovedAsync(long id, CancellationToken token = default)
    {
        var entity = await RequireApprovedEntityAsync(id, token);
        var (term, method) = await DefaultCodesAsync(entity, token);
        return new CustomerIdentity(entity.Id, entity.Code, entity.Name, term, method);
    }

    protected override async Task<bool> IsUsedAsync(long id, CancellationToken token)
    {
        if (usage is not null)
            foreach (var reference in usage)
                if (await reference.IsUsedAsync(id, token)) return true;
        return false;
    }
}

/// <summary>供應商主檔；分類須為同公司供應商分類。</summary>
public sealed class SupplierService(MyErpDbContext db, IEnumerable<ISupplierUsage>? usage = null,
    IApprovalWorkflowService? workflow = null)
    : BusinessPartnerService<Supplier, SupplierCategory>(db, SupplierApprovalService.DocumentType, "Supplier", workflow), ISupplierLookup
{
    protected override Supplier New(long companyId, string code, string name, long? categoryId) =>
        new() { CompanyId = companyId, Code = code, Name = name, CategoryId = categoryId };

    protected override string CategoryLock => CompanyLock.SupplierCategory;

    public async Task<SupplierIdentity> RequireApprovedAsync(long id, CancellationToken token = default)
    {
        var entity = await RequireApprovedEntityAsync(id, token);
        var (term, method) = await DefaultCodesAsync(entity, token);
        return new SupplierIdentity(entity.Id, entity.Code, entity.Name, term, method);
    }

    protected override async Task<bool> IsUsedAsync(long id, CancellationToken token)
    {
        if (usage is not null)
            foreach (var reference in usage)
                if (await reference.IsUsedAsync(id, token)) return true;
        return false;
    }
}

/// <summary>往來對象的預設付款條件與結算方式 id；null 表示不設預設值（ADR-047）。</summary>
public sealed record PartnerDefaults(long? PaymentTermId, long? SettlementMethodId)
{
    public static PartnerDefaults None { get; } = new(null, null);
}

/// <summary>
/// 往來對象 CRUD；公司存取權在 HTTP 中介層驗證，查詢再由 EF 公司篩選保護。
/// <paramref name="errorPrefix"/> 同時是 SYS 審批的單據類型；審批中的資料不可修改（ADR-048）。
/// </summary>
public abstract class BusinessPartnerService<TPartner, TCategory>(MyErpDbContext db, string errorPrefix, string resourceName,
    IApprovalWorkflowService? workflow = null)
    where TPartner : BusinessPartner<TCategory>
    where TCategory : MasterCategory
{
    /// <summary>交易模組只可使用啟用且已核准的往來對象。</summary>
    protected async Task<TPartner> RequireApprovedEntityAsync(long id, CancellationToken token)
    {
        var entity = await FindAsync(id, token);
        if (!entity.IsActive || entity.ReviewStatus != PartnerReviewStatus.Approved)
            throw new BusinessRuleException(errorPrefix + ".NOT_APPROVED", resourceName + " must be active and approved for transactions.");
        return entity;
    }

    /// <summary>
    /// 以版本號軟刪除；審批中或已被單據引用者不可刪，代碼仍保留不可重用。
    /// 從未送審或已撤回的待審資料可刪（例如匯入錯誤）；送審會遞增版本，與刪除並發時由樂觀鎖擋下。
    /// 沒有審批服務時無法判斷，待審一律視為審批中。
    /// </summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        if (entity.ReviewStatus == PartnerReviewStatus.Pending && (workflow is null
                || (await workflow.GetStateAsync(errorPrefix, entity.Id, token))?.Status == ApprovalStatus.Pending))
            throw new BusinessRuleException(errorPrefix + ".APPROVAL_PENDING", resourceName + " in approval cannot be deleted.");
        if (await IsUsedAsync(id, token))
            throw new ConflictException(errorPrefix + ".IN_USE", resourceName + " is referenced by a document.");
        entity.IsDeleted = true;
        entity.IsActive = false;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
    }

    protected abstract Task<bool> IsUsedAsync(long id, CancellationToken token);

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

    public Task<TPartner> CreateAsync(string? code, string? name, long? categoryId, CancellationToken token = default) =>
        CreateAsync(code, name, categoryId, PartnerDefaults.None, token);

    public Task<TPartner> CreateAsync(string? code, string? name, long? categoryId, PartnerDefaults defaults,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return CompanyLock.RunSharedAsync(db, ReferencedTables, RequireCompany(),
            () => CreateCoreAsync(code, name, categoryId, defaults, token), token);
    }

    /// <summary>分類與預設值的刪除鎖；驗證到保存期間以共享鎖持有，避免引用剛刪除的資料（MD-B03）。</summary>
    private string[] ReferencedTables => [CategoryLock, CompanyLock.PaymentTerm, CompanyLock.SettlementMethod];

    /// <summary>分類資料表的 <see cref="CompanyLock"/> 鎖名。</summary>
    protected abstract string CategoryLock { get; }

    private async Task<TPartner> CreateCoreAsync(string? code, string? name, long? categoryId, PartnerDefaults defaults,
        CancellationToken token)
    {
        var companyId = RequireCompany();
        var normalizedCode = MasterDataInput.Code(code);
        var normalizedName = MasterDataInput.Name(name);
        await ValidateCategoryAsync(categoryId, false, token);
        await ValidateDefaultsAsync(defaults, null, token);
        if (await db.Set<TPartner>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == normalizedCode, token))
            throw Duplicate();
        var entity = New(companyId, normalizedCode, normalizedName, categoryId);
        entity.DefaultPaymentTermId = defaults.PaymentTermId;
        entity.DefaultSettlementMethodId = defaults.SettlementMethodId;
        db.Add(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Duplicate(); }
        return entity;
    }

    /// <summary>修改名稱、分類及啟用狀態，保留原有預設付款條件與結算方式。</summary>
    public Task<TPartner> UpdateAsync(long id, string? name, bool isActive, long? categoryId, int version, CancellationToken token = default) =>
        UpdateAsync(id, name, isActive, categoryId, null, version, token);

    /// <summary><paramref name="defaults"/> 為 null 時保留原值，否則完整取代兩個預設值。</summary>
    public Task<TPartner> UpdateAsync(long id, string? name, bool isActive, long? categoryId, PartnerDefaults? defaults,
        int version, CancellationToken token = default) =>
        CompanyLock.RunSharedAsync(db, ReferencedTables, RequireCompany(),
            () => UpdateCoreAsync(id, name, isActive, categoryId, defaults, version, token), token);

    private async Task<TPartner> UpdateCoreAsync(long id, string? name, bool isActive, long? categoryId, PartnerDefaults? defaults,
        int version, CancellationToken token)
    {
        var normalizedName = MasterDataInput.Name(name);
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        await EnsureNotInApprovalAsync(entity, token);
        await ValidateCategoryAsync(categoryId, entity.CategoryId == categoryId, token);
        if (defaults is not null)
        {
            await ValidateDefaultsAsync(defaults, entity, token);
            entity.DefaultPaymentTermId = defaults.PaymentTermId;
            entity.DefaultSettlementMethodId = defaults.SettlementMethodId;
        }
        entity.Name = normalizedName;
        entity.IsActive = isActive;
        entity.CategoryId = categoryId;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        return entity;
    }

    protected abstract TPartner New(long companyId, string code, string name, long? categoryId);

    /// <summary>
    /// 審批人核准的是送審時的代碼與名稱快照，審批中修改會令核准內容與主檔不符。
    /// 只有待審資料需要查 SYS：從未送審、已撤回者仍可修改；核准後的維護不需重審（ADR-048）。
    /// </summary>
    private async Task EnsureNotInApprovalAsync(TPartner entity, CancellationToken token)
    {
        if (workflow is null || entity.ReviewStatus != PartnerReviewStatus.Pending) return;
        if ((await workflow.GetStateAsync(errorPrefix, entity.Id, token))?.Status == ApprovalStatus.Pending)
            throw new BusinessRuleException(errorPrefix + ".APPROVAL_PENDING", resourceName + " in approval cannot be changed.");
    }

    private async Task ValidateCategoryAsync(long? categoryId, bool allowInactive, CancellationToken token)
    {
        if (categoryId is null) return;
        if (categoryId <= 0 || !await db.Set<TCategory>().AnyAsync(x => x.Id == categoryId && (x.IsActive || allowInactive), token))
            throw new RequestValidationException("categoryId", "Category must be in the current company and active when newly selected.");
    }

    /// <summary>預設值須為同公司未刪除資料；新選取時須啟用，未改變的已停用值可保留（ADR-047）。</summary>
    private async Task ValidateDefaultsAsync(PartnerDefaults defaults, TPartner? current, CancellationToken token)
    {
        var termId = defaults.PaymentTermId;
        var keepTerm = current is not null && current.DefaultPaymentTermId == termId;
        if (termId is not null && (termId <= 0 || !await db.Set<PaymentTerm>().AnyAsync(
                x => x.Id == termId && (x.IsActive || keepTerm), token)))
            throw new RequestValidationException("defaultPaymentTermId",
                "Payment term must be in the current company and active when newly selected.");
        var methodId = defaults.SettlementMethodId;
        var keepMethod = current is not null && current.DefaultSettlementMethodId == methodId;
        if (methodId is not null && (methodId <= 0 || !await db.Set<SettlementMethod>().AnyAsync(
                x => x.Id == methodId && (x.IsActive || keepMethod), token)))
            throw new RequestValidationException("defaultSettlementMethodId",
                "Settlement method must be in the current company and active when newly selected.");
    }

    /// <summary>付款條件與結算方式代碼，供契約帶出預設值。</summary>
    protected async Task<(string? PaymentTermCode, string? SettlementMethodCode)> DefaultCodesAsync(TPartner entity,
        CancellationToken token)
    {
        var term = entity.DefaultPaymentTermId is { } termId
            ? await db.Set<PaymentTerm>().Where(x => x.Id == termId).Select(x => x.Code).SingleOrDefaultAsync(token) : null;
        var method = entity.DefaultSettlementMethodId is { } methodId
            ? await db.Set<SettlementMethod>().Where(x => x.Id == methodId).Select(x => x.Code).SingleOrDefaultAsync(token) : null;
        return (term, method);
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
