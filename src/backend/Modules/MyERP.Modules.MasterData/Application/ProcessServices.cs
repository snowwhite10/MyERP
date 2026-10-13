using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>
/// 工序類別 CRUD（ADR-042）；列表預設按排序號。有未刪除工序的類別不可刪除；
/// 刪除與工序設定類別共用公司級 applock，避免工序掛到剛刪除的類別。
/// </summary>
public sealed class ProcessCategoryService(MyErpDbContext db)
    : MasterCategoryService<ProcessCategory>(db, "MD.PROCESS_CATEGORY", "Process category")
{
    internal const string LockName = "md_process_category";

    public Task<ProcessCategory> CreateAsync(string? code, string? name, int sortOrder, CancellationToken token = default)
    {
        ProcessInput.CheckSortOrder(sortOrder);
        return CreateCoreAsync(code, name, x => x.SortOrder = sortOrder, token);
    }

    public Task<ProcessCategory> UpdateAsync(long id, string? name, int sortOrder, bool isActive, int version, CancellationToken token = default)
    {
        ProcessInput.CheckSortOrder(sortOrder);
        return UpdateCoreAsync(id, name, isActive, version, x => x.SortOrder = sortOrder, token);
    }

    public override Task DeleteAsync(long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, LockName, RequireCompany(), async () =>
        {
            await base.DeleteAsync(id, version, token);
            return true;
        }, token);

    /// <summary>各類別的未刪除工序數（列表顯示用）；沒有工序的類別不在結果內。</summary>
    public async Task<IReadOnlyDictionary<long, int>> ProcessCountsAsync(IReadOnlyCollection<long> categoryIds, CancellationToken token = default)
    {
        RequireCompany();
        if (categoryIds.Count == 0) return new Dictionary<long, int>();
        return await Db.Set<Process>().AsNoTracking()
            .Where(x => x.CategoryId != null && categoryIds.Contains(x.CategoryId.Value))
            .GroupBy(x => x.CategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, token);
    }

    protected override ProcessCategory New(long companyId, string code, string name) => new() { CompanyId = companyId, Code = code, Name = name };

    protected override IReadOnlyCollection<string> SortFields => ProcessInput.SortFields;

    protected override string DefaultSortField => "sortOrder";

    protected override IQueryable<ProcessCategory>? OrderBy(IQueryable<ProcessCategory> query, string field, bool descending) =>
        field != "sortOrder" ? null
        : descending ? query.OrderByDescending(x => x.SortOrder).ThenBy(x => x.Code).ThenBy(x => x.Id)
        : query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ThenBy(x => x.Id);

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<Process>().AnyAsync(x => x.CategoryId == id, token))
            throw new ConflictException(ErrorPrefix + ".HAS_PROCESSES", "Move or delete the processes before deleting the category.");
    }
}

public sealed record ProcessInput(long? CategoryId, long? UomId, decimal StandardRate, int SortOrder)
{
    internal static readonly string[] SortFields = ["sortOrder", "code", "name"];

    internal static void CheckSortOrder(int value)
    {
        if (value is < 0 or > ProcessCategory.MaxSortOrder)
            throw new RequestValidationException("sortOrder", $"Use 0–{ProcessCategory.MaxSortOrder}.");
    }

    internal void Validate()
    {
        CheckSortOrder(SortOrder);
        if (StandardRate < 0 || StandardRate >= 10_000_000_000_000m || decimal.Round(StandardRate, 6) != StandardRate)
            throw new RequestValidationException("standardRate", "Use a non-negative rate with at most 6 decimal places.");
    }
}

/// <summary>
/// 工序 CRUD（ADR-042）；列表預設按排序號，可按類別篩選。類別與單位在設定或變更時必須是同公司啟用資料；
/// 被 PP 引用（<see cref="IProcessUsage"/>）的工序只可停用。
/// </summary>
public sealed class ProcessService(MyErpDbContext db, IEnumerable<IProcessUsage> usages)
    : MasterCategoryService<Process>(db, "MD.PROCESS", "Process")
{
    public Task<PagedResult<Process>> ListAsync(PagedRequest request, long? categoryId, CancellationToken token = default) =>
        ListCoreAsync(request, categoryId is null ? null : q => q.Where(x => x.CategoryId == categoryId), token);

    public Task<Process> CreateAsync(string? code, string? name, ProcessInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        return RunLockedAsync(async () =>
        {
            await ValidateReferencesAsync(null, input, token);
            return await CreateCoreAsync(code, name, x => Apply(x, input), token);
        }, token);
    }

    public Task<Process> UpdateAsync(long id, string? name, ProcessInput input, bool isActive, int version, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        return RunLockedAsync(async () =>
        {
            var entity = await FindAsync(id, token);
            if (entity.Version == version) await ValidateReferencesAsync(entity, input, token);
            return await UpdateCoreAsync(id, name, isActive, version, x => Apply(x, input), token);
        }, token);
    }

    /// <summary>類別排他鎖內再以共享鎖持有單位，避免引用剛刪除的類別或單位（MD-B03）。</summary>
    private Task<Process> RunLockedAsync(Func<Task<Process>> action, CancellationToken token)
    {
        var companyId = RequireCompany();
        return CompanyLock.RunAsync(Db, ProcessCategoryService.LockName, companyId,
            () => CompanyLock.RunSharedAsync(Db, [CompanyLock.Uom], companyId, action, token), token);
    }

    protected override Process New(long companyId, string code, string name) => new() { CompanyId = companyId, Code = code, Name = name };

    protected override IReadOnlyCollection<string> SortFields => ProcessInput.SortFields;

    protected override string DefaultSortField => "sortOrder";

    protected override IQueryable<Process>? OrderBy(IQueryable<Process> query, string field, bool descending) =>
        field != "sortOrder" ? null
        : descending ? query.OrderByDescending(x => x.SortOrder).ThenBy(x => x.Code).ThenBy(x => x.Id)
        : query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ThenBy(x => x.Id);

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (!usages.Any()) return;
        // 刪除流程已載入並追蹤該筆資料，FindAsync 直接取自追蹤器。
        var code = (await Db.Set<Process>().FindAsync([id], token))!.Code;
        foreach (var usage in usages)
            if (await usage.IsInUseAsync(code, token)) throw new ConflictException(ErrorPrefix + ".IN_USE", "Process is in use.");
    }

    /// <summary>類別、單位未變更時不重新檢查（即使之後已停用）。</summary>
    private async Task ValidateReferencesAsync(Process? current, ProcessInput input, CancellationToken token)
    {
        if (input.CategoryId is { } categoryId && categoryId != current?.CategoryId &&
            !await Db.Set<ProcessCategory>().AnyAsync(x => x.Id == categoryId && x.IsActive, token))
            throw new BusinessRuleException(ErrorPrefix + ".INVALID_CATEGORY", "Select an active process category of this company.");
        if (input.UomId is { } uomId && uomId != current?.UomId &&
            !await Db.Set<UnitOfMeasure>().AnyAsync(x => x.Id == uomId && x.IsActive, token))
            throw new BusinessRuleException(ErrorPrefix + ".INVALID_UOM", "Select an active unit of this company.");
    }

    private static void Apply(Process x, ProcessInput input) =>
        (x.CategoryId, x.UomId, x.StandardRate, x.SortOrder) = (input.CategoryId, input.UomId, input.StandardRate, input.SortOrder);
}

public sealed class ProcessLookup(MyErpDbContext db) : IProcessLookup
{
    public async Task<ProcessInfo?> FindAsync(string code, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var normalized = code.Trim().ToUpperInvariant();
        return (await QueryAsync(q => q.Where(x => x.Code == normalized), token)).Select(x => x.Info).SingleOrDefault();
    }

    public async Task<ProcessInfo> RequireActiveAsync(string code, CancellationToken token = default)
    {
        var item = await FindAsync(code, token) ?? throw new NotFoundException("MD.PROCESS.NOT_FOUND", "Process", code);
        return item.IsActive ? item : throw new BusinessRuleException("MD.PROCESS.INACTIVE", "Process is inactive.");
    }

    public async Task<IReadOnlyList<ProcessInfo>> ListActiveAsync(string? categoryCode = null, CancellationToken token = default)
    {
        var category = categoryCode?.Trim().ToUpperInvariant();
        var rows = await QueryAsync(q => q.Where(x => x.IsActive), token);
        return [.. rows.Where(x => category is null || x.Info.CategoryCode == category)
            .OrderBy(x => x.Info.CategoryCode is null)
            .ThenBy(x => x.CategorySort).ThenBy(x => x.Info.CategoryCode, StringComparer.Ordinal)
            .ThenBy(x => x.Info.SortOrder).ThenBy(x => x.Info.Code, StringComparer.Ordinal)
            .Select(x => x.Info)];
    }

    private async Task<List<(ProcessInfo Info, int CategorySort)>> QueryAsync(
        Func<IQueryable<Process>, IQueryable<Process>> filter, CancellationToken token)
    {
        PaymentTermLookup.RequireCompany(db);
        var rows = await (
            from p in filter(db.Set<Process>().AsNoTracking())
            join c in db.Set<ProcessCategory>() on p.CategoryId equals (long?)c.Id into cs
            from c in cs.DefaultIfEmpty()
            join u in db.Set<UnitOfMeasure>() on p.UomId equals (long?)u.Id into us
            from u in us.DefaultIfEmpty()
            select new
            {
                p.Code, p.Name, CategoryCode = c == null ? null : c.Code, CategoryName = c == null ? null : c.Name,
                CategorySort = c == null ? 0 : c.SortOrder, UomCode = u == null ? null : u.Code,
                p.StandardRate, p.SortOrder, p.IsActive,
            }).ToListAsync(token);
        return [.. rows.Select(x => (new ProcessInfo(x.Code, x.Name, x.CategoryCode, x.CategoryName, x.UomCode,
            x.StandardRate, x.SortOrder, x.IsActive), x.CategorySort))];
    }
}
