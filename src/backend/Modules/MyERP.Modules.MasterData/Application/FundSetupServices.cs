using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

public sealed class IncomeExpenseCategoryService(MyErpDbContext db, IEnumerable<IIncomeExpenseCategoryUsage> usages)
    : MasterCategoryService<IncomeExpenseCategory>(db, "MD.INCOME_EXPENSE_CATEGORY", "Income expense category")
{
    public Task<PagedResult<IncomeExpenseCategory>> ListAsync(PagedRequest request, IncomeExpenseDirection? direction, CancellationToken token = default)
    {
        if (direction is { } value && !Enum.IsDefined(value)) throw new RequestValidationException("direction", "Invalid direction.");
        return ListCoreAsync(request, direction is null ? null : q => q.Where(x => x.Direction == direction.ToString()), token);
    }

    public Task<IncomeExpenseCategory> CreateAsync(string? code, string? name, IncomeExpenseDirection direction, long? ledgerAccountId, CancellationToken token = default)
    {
        if (!Enum.IsDefined(direction)) throw new RequestValidationException("direction", "Invalid direction.");
        return CompanyLock.RunAsync(Db, "md_account", RequireCompany(), async () =>
        {
            await ValidateLedgerAsync(ledgerAccountId, direction, token);
            return await CreateCoreAsync(code, name, x => { x.Direction = direction.ToString(); x.LedgerAccountId = ledgerAccountId; }, token);
        }, token);
    }

    public Task<IncomeExpenseCategory> UpdateAsync(long id, string? name, long? ledgerAccountId, bool isActive, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, "md_account", RequireCompany(), async () =>
        {
            var entity = await FindAsync(id, token);
            if (entity.Version == version && entity.LedgerAccountId != ledgerAccountId)
                await ValidateLedgerAsync(ledgerAccountId, Enum.Parse<IncomeExpenseDirection>(entity.Direction), token);
            return await UpdateCoreAsync(id, name, isActive, version, x => x.LedgerAccountId = ledgerAccountId, token);
        }, token);

    protected override IncomeExpenseCategory New(long companyId, string code, string name) => new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        var code = (await FindAsync(id, token)).Code;
        foreach (var usage in usages)
            if (await usage.IsInUseAsync(code, token)) throw new ConflictException(ErrorPrefix + ".IN_USE", "Category is in use.");
    }

    private async Task ValidateLedgerAsync(long? id, IncomeExpenseDirection direction, CancellationToken token)
    {
        if (id is null) return;
        var account = await Db.Set<Account>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        var validType = direction == IncomeExpenseDirection.Income ? account?.AccountType == "Revenue" : account?.AccountType is "Expense" or "Cost";
        if (!validType || account is not { IsActive: true } || await Db.Set<Account>().AnyAsync(x => x.ParentId == id, token))
            throw new BusinessRuleException(ErrorPrefix + ".INVALID_LEDGER_ACCOUNT", "Select an active postable account of the required type.");
    }
}

public sealed record FundAccountInput(FundAccountType FundType, string? BankName, string? BankAccountNo, long? LedgerAccountId);

public sealed class FundAccountService(MyErpDbContext db, IEnumerable<IFundAccountUsage> usages)
    : MasterCategoryService<FundAccount>(db, "MD.FUND_ACCOUNT", "Fund account")
{
    public Task<PagedResult<FundAccount>> ListAsync(PagedRequest request, FundAccountType? fundType, CancellationToken token = default)
    {
        if (fundType is { } value && !Enum.IsDefined(value)) throw new RequestValidationException("fundType", "Invalid fund type.");
        return ListCoreAsync(request, fundType is null ? null : q => q.Where(x => x.FundType == fundType.ToString()), token);
    }

    public Task<FundAccount> CreateAsync(string? code, string? name, long currencyId, FundAccountInput input, CancellationToken token = default)
    {
        var normalized = Normalize(input);
        return CompanyLock.RunAsync(Db, "md_account", RequireCompany(), async () =>
        {
            await ValidateCurrencyAsync(currencyId, token);
            await ValidateLedgerAsync(normalized.LedgerAccountId, token);
            return await CreateCoreAsync(code, name, x => { x.CurrencyId = currencyId; Apply(x, normalized); }, token);
        }, token);
    }

    public Task<FundAccount> UpdateAsync(long id, string? name, FundAccountInput input, bool isActive, int version, CancellationToken token = default)
    {
        var normalized = Normalize(input);
        return CompanyLock.RunAsync(Db, "md_account", RequireCompany(), async () =>
        {
            var entity = await FindAsync(id, token);
            if (entity.Version == version && entity.LedgerAccountId != normalized.LedgerAccountId)
                await ValidateLedgerAsync(normalized.LedgerAccountId, token);
            return await UpdateCoreAsync(id, name, isActive, version, x => Apply(x, normalized), token);
        }, token);
    }

    protected override FundAccount New(long companyId, string code, string name) => new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        var code = (await FindAsync(id, token)).Code;
        foreach (var usage in usages)
            if (await usage.IsInUseAsync(code, token)) throw new ConflictException(ErrorPrefix + ".IN_USE", "Fund account is in use.");
    }

    private async Task ValidateCurrencyAsync(long id, CancellationToken token)
    {
        if (!await Db.Set<Currency>().AnyAsync(x => x.Id == id && x.IsActive, token))
            throw new BusinessRuleException(ErrorPrefix + ".INVALID_CURRENCY", "Select an active currency of this company.");
    }

    private async Task ValidateLedgerAsync(long? id, CancellationToken token)
    {
        if (id is null) return;
        var account = await Db.Set<Account>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (account is not { IsActive: true, AccountType: "Asset" } || await Db.Set<Account>().AnyAsync(x => x.ParentId == id, token))
            throw new BusinessRuleException(ErrorPrefix + ".INVALID_LEDGER_ACCOUNT", "Select an active postable asset account.");
    }

    private static FundAccountInput Normalize(FundAccountInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.FundType)) throw new RequestValidationException("fundType", "Invalid fund type.");
        if (input.FundType == FundAccountType.Cash) return input with { BankName = null, BankAccountNo = null };
        var name = input.BankName?.Trim();
        var number = input.BankAccountNo?.Trim();
        if (name?.Length > 200) throw new RequestValidationException("bankName", "Bank name is too long.");
        if (number?.Length > FundAccount.BankAccountNoMaxLength || number?.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not (' ' or '-')) == true)
            throw new RequestValidationException("bankAccountNo", "Bank account number contains invalid characters.");
        return input with { BankName = string.IsNullOrEmpty(name) ? null : name, BankAccountNo = string.IsNullOrEmpty(number) ? null : number };
    }

    private static void Apply(FundAccount x, FundAccountInput input) =>
        (x.FundType, x.BankName, x.BankAccountNo, x.LedgerAccountId) =
        (input.FundType.ToString(), input.BankName, input.BankAccountNo, input.LedgerAccountId);
}

public sealed class FundSetupAccountUsage(MyErpDbContext db) : IAccountUsage
{
    public async Task<bool> IsInUseAsync(string accountCode, CancellationToken token = default)
    {
        var account = await db.Set<Account>().AsNoTracking().Where(x => x.Code == accountCode).Select(x => new { x.Id }).SingleOrDefaultAsync(token);
        return account is not null &&
            (await db.Set<FundAccount>().AnyAsync(x => x.LedgerAccountId == account.Id, token) ||
             await db.Set<IncomeExpenseCategory>().AnyAsync(x => x.LedgerAccountId == account.Id, token));
    }
}

public sealed class FundAccountLookup(MyErpDbContext db) : IFundAccountLookup
{
    public async Task<FundAccountInfo?> FindAsync(string code, CancellationToken token = default)
    {
        PaymentTermLookup.RequireCompany(db);
        var normalized = code.Trim().ToUpperInvariant();
        var item = await db.Set<FundAccount>().AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, token);
        if (item is null) return null;
        var currency = await db.Set<Currency>().Where(x => x.Id == item.CurrencyId).Select(x => x.Code).SingleAsync(token);
        var ledger = item.LedgerAccountId is null ? null : await db.Set<Account>().Where(x => x.Id == item.LedgerAccountId).Select(x => x.Code).SingleAsync(token);
        return new(item.Code, item.Name, Enum.Parse<FundAccountType>(item.FundType), currency, item.BankName, item.BankAccountNo, ledger, item.IsActive);
    }

    public async Task<FundAccountInfo> RequireActiveAsync(string code, CancellationToken token = default)
    {
        var item = await FindAsync(code, token) ?? throw new NotFoundException("MD.FUND_ACCOUNT.NOT_FOUND", "Fund account", code);
        return item.IsActive ? item : throw new BusinessRuleException("MD.FUND_ACCOUNT.INACTIVE", "Fund account is inactive.");
    }
}

public sealed class IncomeExpenseCategoryLookup(MyErpDbContext db) : IIncomeExpenseCategoryLookup
{
    public async Task<IncomeExpenseCategoryInfo?> FindAsync(string code, CancellationToken token = default)
    {
        PaymentTermLookup.RequireCompany(db);
        var normalized = code.Trim().ToUpperInvariant();
        var item = await db.Set<IncomeExpenseCategory>().AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, token);
        if (item is null) return null;
        var ledger = item.LedgerAccountId is null ? null : await db.Set<Account>().Where(x => x.Id == item.LedgerAccountId).Select(x => x.Code).SingleAsync(token);
        return new(item.Code, item.Name, Enum.Parse<IncomeExpenseDirection>(item.Direction), ledger, item.IsActive);
    }

    public async Task<IncomeExpenseCategoryInfo> RequireActiveAsync(string code, IncomeExpenseDirection direction, CancellationToken token = default)
    {
        var item = await FindAsync(code, token) ?? throw new NotFoundException("MD.INCOME_EXPENSE_CATEGORY.NOT_FOUND", "Income expense category", code);
        if (!item.IsActive) throw new BusinessRuleException("MD.INCOME_EXPENSE_CATEGORY.INACTIVE", "Category is inactive.");
        return item.Direction == direction ? item : throw new BusinessRuleException("MD.INCOME_EXPENSE_CATEGORY.DIRECTION_MISMATCH", "Category direction differs from document.");
    }
}
