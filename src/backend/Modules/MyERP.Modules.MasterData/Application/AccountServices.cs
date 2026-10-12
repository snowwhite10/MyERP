using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>科目樹節點；<see cref="IsPostable"/> = 啟用且沒有未刪除子科目。</summary>
public sealed record AccountNode(Account Account, bool IsPostable);

/// <summary>
/// 會計科目 CRUD（ADR-039）。上層與類別建立後固定，子科目代碼以上層代碼開頭；
/// 是否已被使用由 <see cref="IAccountUsage"/> 實作判斷。維護以公司級 applock 序列化。
/// </summary>
public sealed class AccountService(MyErpDbContext db, IEnumerable<IAccountUsage> usages)
    : MasterCategoryService<Account>(db, "MD.ACCOUNT", "Account")
{
    private const string LockTable = "md_account";

    /// <summary>目前公司全部未刪除科目（含停用），按代碼排序；前端自行組樹。</summary>
    public async Task<IReadOnlyList<AccountNode>> TreeAsync(CancellationToken token = default)
    {
        RequireCompany();
        var accounts = await Db.Set<Account>().AsNoTracking().ToListAsync(token);
        var parents = accounts.Where(x => x.ParentId is not null).Select(x => x.ParentId!.Value).ToHashSet();
        return [.. accounts.OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => new AccountNode(x, x.IsActive && !parents.Contains(x.Id)))];
    }

    public async Task<bool> IsPostableAsync(Account account, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.IsActive && !await Db.Set<Account>().AnyAsync(x => x.ParentId == account.Id, token);
    }

    public Task<Account> CreateAsync(string? code, string? name, long? parentId, AccountType? accountType,
        BalanceDirection? balanceDirection, CancellationToken token = default)
    {
        var normalizedCode = DigitsCode(code);
        if (accountType is { } type && !Enum.IsDefined(type)) throw InvalidType();
        if (balanceDirection is { } direction && !Enum.IsDefined(direction)) throw InvalidDirection();
        return CompanyLock.RunAsync(Db, LockTable, RequireCompany(), async () =>
        {
            var kind = accountType?.ToString();
            if (parentId is not null)
            {
                var parent = await Db.Set<Account>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == parentId, token);
                if (parent is not { IsActive: true })
                    throw new BusinessRuleException(ErrorPrefix + ".INVALID_PARENT", "Parent must be an active account of this company.");
                if (!Account.IsValidChildCode(parent.Code, normalizedCode))
                    throw new BusinessRuleException(ErrorPrefix + ".CODE_PREFIX", "Account code must start with the parent code and be longer.");
                if (kind is not null && kind != parent.AccountType)
                    throw new BusinessRuleException(ErrorPrefix + ".TYPE_MISMATCH", "Account type must match the parent account.");
                if (await IsInUseAsync(parent.Code, token))
                    throw new BusinessRuleException(ErrorPrefix + ".PARENT_IN_USE", "Parent account is already in use.");
                kind = parent.AccountType;
            }
            if (kind is not { } resolved) throw InvalidType();
            return await CreateCoreAsync(normalizedCode, name, x =>
            {
                x.ParentId = parentId;
                x.AccountType = resolved;
                x.BalanceDirection = balanceDirection?.ToString() ?? Account.DefaultDirection(resolved);
            }, token);
        }, token);
    }

    /// <summary>按版本修改名稱、借貸方向與啟用狀態；代碼、上層與類別不可修改。</summary>
    public Task<Account> UpdateAsync(long id, string? name, BalanceDirection balanceDirection, bool isActive, int version,
        CancellationToken token = default)
    {
        if (!Enum.IsDefined(balanceDirection)) throw InvalidDirection();
        var direction = balanceDirection.ToString();
        return CompanyLock.RunAsync(Db, LockTable, RequireCompany(), async () =>
        {
            var entity = await FindAsync(id, token);
            if (entity.Version == version)
            {
                if (direction != entity.BalanceDirection && await IsInUseAsync(entity.Code, token))
                    throw new ConflictException(ErrorPrefix + ".IN_USE", "Account is in use; its balance direction cannot change.");
                if (!isActive && entity.IsActive && await Db.Set<Account>().AnyAsync(x => x.ParentId == id && x.IsActive, token))
                    throw new BusinessRuleException(ErrorPrefix + ".HAS_ACTIVE_CHILDREN", "Deactivate the child accounts first.");
                if (isActive && !entity.IsActive && entity.ParentId is not null &&
                    !await Db.Set<Account>().AnyAsync(x => x.Id == entity.ParentId && x.IsActive, token))
                    throw new BusinessRuleException(ErrorPrefix + ".PARENT_INACTIVE", "Activate the parent account first.");
            }
            return await UpdateCoreAsync(id, name, isActive, version, x => x.BalanceDirection = direction, token);
        }, token);
    }

    public override Task DeleteAsync(long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, LockTable, RequireCompany(), async () =>
        {
            await base.DeleteAsync(id, version, token);
            return true;
        }, token);

    protected override Account New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<Account>().AnyAsync(x => x.ParentId == id, token))
            throw new ConflictException(ErrorPrefix + ".HAS_CHILDREN", "Account has child accounts.");
        var code = await Db.Set<Account>().Where(x => x.Id == id).Select(x => x.Code).SingleAsync(token);
        if (await IsInUseAsync(code, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Account is in use.");
    }

    private async Task<bool> IsInUseAsync(string code, CancellationToken token)
    {
        foreach (var usage in usages)
            if (await usage.IsInUseAsync(code, token)) return true;
        return false;
    }

    private static string DigitsCode(string? value)
    {
        var code = value?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 30 || !code.All(char.IsAsciiDigit))
            throw new RequestValidationException("code", "Use 1–30 digits.");
        return code;
    }

    private static RequestValidationException InvalidType() =>
        new("accountType", "Use Asset, Liability, Equity, Cost, Revenue or Expense; required for root accounts.");

    private static RequestValidationException InvalidDirection() => new("balanceDirection", "Use Debit or Credit.");
}

/// <summary><see cref="IChartOfAccounts"/> 的 MD 實作；只讀目前公司未刪除科目。</summary>
public sealed class ChartOfAccounts(MyErpDbContext db) : IChartOfAccounts
{
    public async Task<AccountInfo?> FindAsync(string code, CancellationToken token = default)
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        var normalized = code?.Trim();
        // 上層代碼與是否有子科目以子查詢一次取回；公司與軟刪除篩選同樣套用在子查詢。
        var accounts = db.Set<Account>().AsNoTracking();
        var account = await accounts.Where(x => x.Code == normalized).Select(x => new
        {
            x.Code, x.Name, x.AccountType, x.BalanceDirection, x.IsActive,
            ParentCode = accounts.Where(p => p.Id == x.ParentId).Select(p => p.Code).FirstOrDefault(),
            HasChildren = accounts.Any(c => c.ParentId == x.Id),
        }).SingleOrDefaultAsync(token);
        return account is null ? null : new AccountInfo(account.Code, account.Name, account.ParentCode,
            Enum.Parse<AccountType>(account.AccountType), Enum.Parse<BalanceDirection>(account.BalanceDirection),
            account.IsActive, account.IsActive && !account.HasChildren);
    }

    public async Task<AccountInfo> RequirePostableAsync(string code, CancellationToken token = default)
    {
        var account = await FindAsync(code, token) ?? throw new NotFoundException("MD.ACCOUNT.NOT_FOUND", "Account", code ?? string.Empty);
        if (!account.IsActive) throw new BusinessRuleException("MD.ACCOUNT.INACTIVE", "Account is inactive.");
        if (!account.IsPostable) throw new BusinessRuleException("MD.ACCOUNT.NOT_POSTABLE", "Only accounts without child accounts can be posted.");
        return account;
    }
}
