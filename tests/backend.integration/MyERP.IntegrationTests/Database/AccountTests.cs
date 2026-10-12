using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class AccountTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new AccountDbModel()], company);
    }

    private static AccountService Service(MyErpDbContext db, params string[] usedCodes) =>
        new(db, usedCodes.Length == 0 ? [] : [new FixedUsage(usedCodes)]);

    [Fact]
    public async Task AccountsAreCompanyScopedAndChildrenFollowParentCodeAndType()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(34000000, 35000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var service = Service(db);
        Assert.Equal("accountType", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("1", "Assets", null, null, null, token))).Errors).Key);
        Assert.Equal("code", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("1A", "Assets", null, AccountType.Asset, null, token))).Errors).Key);
        Assert.Equal("accountType", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("1", "Assets", null, (AccountType)99, null, token))).Errors).Key);
        var assets = await service.CreateAsync(" 1 ", " Assets ", null, AccountType.Asset, null, token);
        Assert.Equal(("1", "Assets", "Debit", 7L), (assets.Code, assets.Name, assets.BalanceDirection, assets.CreatedBy));
        var revenue = await service.CreateAsync("4", "Revenue", null, AccountType.Revenue, null, token);
        Assert.Equal("Credit", revenue.BalanceDirection);
        var cash = await service.CreateAsync("1001", "Cash", assets.Id, null, null, token);
        Assert.Equal(("Asset", "Debit", assets.Id), (cash.AccountType, cash.BalanceDirection, cash.ParentId));
        var contra = await service.CreateAsync("1602", "Accumulated depreciation", assets.Id, AccountType.Asset,
            BalanceDirection.Credit, token);
        Assert.Equal("Credit", contra.BalanceDirection);
        Assert.Equal("MD.ACCOUNT.CODE_PREFIX", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("2001", "Wrong prefix", assets.Id, null, null, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.CODE_PREFIX", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("1", "Same code", revenue.Id, null, null, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.TYPE_MISMATCH", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("1002", "Bank", assets.Id, AccountType.Liability, null, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("1001", "Again", assets.Id, null, null, token))).ErrorCode);

        var tree = await service.TreeAsync(token);
        Assert.Equal(["1", "1001", "1602", "4"], tree.Select(x => x.Account.Code));
        Assert.Equal([false, true, true, true], tree.Select(x => x.IsPostable));
        var other = Service(otherDb);
        Assert.Empty(await other.TreeAsync(token));
        Assert.Equal("MD.ACCOUNT.INVALID_PARENT", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => other.CreateAsync("1002", "Foreign", assets.Id, null, null, token))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(
            () => other.UpdateAsync(cash.Id, "X", BalanceDirection.Debit, true, cash.Version, token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(noCompany).TreeAsync(token));
    }

    [Fact]
    public async Task ActivationFollowsParentsAndOnlyLeavesCanBeDeleted()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(35000000, 36000000));
        var service = Service(db);
        var expenses = await service.CreateAsync("6", "Expenses", null, AccountType.Expense, null, token);
        var travel = await service.CreateAsync("6601", "Travel", expenses.Id, null, null, token);
        Assert.Equal("MD.ACCOUNT.HAS_ACTIVE_CHILDREN", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.UpdateAsync(expenses.Id, "Expenses", BalanceDirection.Debit, false, expenses.Version, token))).ErrorCode);
        travel = await service.UpdateAsync(travel.Id, " Travel costs ", BalanceDirection.Debit, false, travel.Version, token);
        Assert.Equal(("Travel costs", false), (travel.Name, travel.IsActive));
        Assert.False((await service.TreeAsync(token)).Single(x => x.Account.Id == travel.Id).IsPostable);
        expenses = await service.UpdateAsync(expenses.Id, "Expenses", BalanceDirection.Debit, false, expenses.Version, token);
        Assert.Equal("MD.ACCOUNT.PARENT_INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.UpdateAsync(travel.Id, "Travel", BalanceDirection.Debit, true, travel.Version, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.INVALID_PARENT", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("6602", "Meals", expenses.Id, null, null, token))).ErrorCode);
        // 停用的子科目仍可改名，上層停用不影響。
        travel = await service.UpdateAsync(travel.Id, "Travel", BalanceDirection.Credit, false, travel.Version, token);
        Assert.Equal("Credit", travel.BalanceDirection);
        await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(travel.Id, "Stale", BalanceDirection.Debit, false, travel.Version - 1, token));
        Assert.Equal("balanceDirection", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.UpdateAsync(travel.Id, "Travel", (BalanceDirection)5, false, travel.Version, token))).Errors).Key);

        Assert.Equal("MD.ACCOUNT.HAS_CHILDREN", (await Assert.ThrowsAsync<ConflictException>(
            () => service.DeleteAsync(expenses.Id, expenses.Version, token))).ErrorCode);
        await service.DeleteAsync(travel.Id, travel.Version, token);
        await service.DeleteAsync(expenses.Id, expenses.Version, token);
        Assert.Empty(await service.TreeAsync(token));
        Assert.Equal("MD.ACCOUNT.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("6", "Again", null, AccountType.Expense, null, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => service.DeleteAsync(expenses.Id, expenses.Version, token))).ErrorCode);
    }

    [Fact]
    public async Task UsedAccountsKeepDirectionCannotBeDeletedOrSplit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(36000000, 37000000));
        var setup = Service(db);
        var liabilities = await setup.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, token);
        var payable = await setup.CreateAsync("2202", "Payables", liabilities.Id, null, null, token);
        var service = Service(db, "2202");
        Assert.Equal("MD.ACCOUNT.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(payable.Id, "Payables", BalanceDirection.Debit, true, payable.Version, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.DeleteAsync(payable.Id, payable.Version, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.PARENT_IN_USE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("220201", "Supplier A", payable.Id, null, null, token))).ErrorCode);
        // 名稱與啟用狀態仍可修改；方向不變時不檢查使用。
        payable = await service.UpdateAsync(payable.Id, "Accounts payable", BalanceDirection.Credit, false, payable.Version, token);
        Assert.Equal(("Accounts payable", false), (payable.Name, payable.IsActive));
        // 未使用的上層仍可新增子科目。
        var notes = await service.CreateAsync("2201", "Notes payable", liabilities.Id, null, null, token);
        Assert.Equal("Liability", notes.AccountType);
    }

    [Fact]
    public async Task ChartOfAccountsReportsPostableAccounts()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(37000000, 38000000));
        var service = Service(db);
        var assets = await service.CreateAsync("1", "Assets", null, AccountType.Asset, null, token);
        var bank = await service.CreateAsync("1002", "Bank", assets.Id, null, null, token);
        var idle = await service.CreateAsync("1009", "Idle", assets.Id, null, null, token);
        await service.UpdateAsync(idle.Id, "Idle", BalanceDirection.Debit, false, idle.Version, token);
        var chart = new ChartOfAccounts(db);
        Assert.Null(await chart.FindAsync("9999", token));
        Assert.Equal(new AccountInfo("1002", "Bank", "1", AccountType.Asset, BalanceDirection.Debit, true, true),
            await chart.FindAsync(" 1002 ", token));
        Assert.Equal(new AccountInfo("1", "Assets", null, AccountType.Asset, BalanceDirection.Debit, true, false),
            await chart.FindAsync("1", token));
        Assert.Equal(bank.Code, (await chart.RequirePostableAsync("1002", token)).Code);
        Assert.Equal("MD.ACCOUNT.NOT_POSTABLE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => chart.RequirePostableAsync("1", token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => chart.RequirePostableAsync("1009", token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => chart.RequirePostableAsync("9999", token))).ErrorCode);
    }

    [Fact]
    public async Task AccountTableHasCompanyParentKeyAndChecks()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(38000000, 39000000);
        await using var db = Context(companyId);
        var root = await Service(db).CreateAsync("3", "Equity", null, AccountType.Equity, null, token);
        await using var otherDb = Context(companyId + 1);
        otherDb.Add(new Account { CompanyId = companyId + 1, Code = "301", Name = "Cross", ParentId = root.Id,
            AccountType = "Equity", BalanceDirection = "Credit" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => otherDb.SaveChangesAsync(token));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('md_account')").ToListAsync(token);
        Assert.Contains("ck_md_account_account_type", checks);
        Assert.Contains("ck_md_account_balance_direction", checks);
        Assert.Contains("ck_md_account_code_digits", checks);
        Assert.Contains("ck_md_account_parent", checks);
        var fk = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_account')").SingleAsync(token);
        Assert.Equal("fk_md_account_md_account_company_id_parent_id", fk);
        var stored = await db.Database.SqlQuery<string>(
            $"SELECT account_type + '/' + balance_direction AS [Value] FROM md_account WHERE id = {root.Id}").SingleAsync(token);
        Assert.Equal("Equity/Credit", stored);
    }

    [Fact]
    public async Task CodeLengthAndNameAreValidated()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(39000000, 40000000));
        var service = Service(db);
        var longest = new string('5', 30);
        var root = await service.CreateAsync(longest[..1], "Cost", null, AccountType.Cost, null, token);
        Assert.Equal("Debit", root.BalanceDirection);
        Assert.Equal(longest, (await service.CreateAsync(longest, "Deep", root.Id, null, null, token)).Code);
        Assert.Equal("code", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync(longest + "1", "Too long", null, AccountType.Cost, null, token))).Errors).Key);
        Assert.Equal("code", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync(" ", "Blank", null, AccountType.Cost, null, token))).Errors).Key);
        Assert.Equal("name", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("7", " ", null, AccountType.Expense, null, token))).Errors).Key);
        Assert.Equal("balanceDirection", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("7", "Bad", null, AccountType.Expense, (BalanceDirection)9, token))).Errors).Key);
    }

    [Fact]
    public async Task ConcurrentParentDeleteAndChildCreateNeverOrphanChildren()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(40000000, 41000000);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var setup = Context(companyId);
            var parent = await Service(setup).CreateAsync($"9{attempt}", "Parent", null, AccountType.Expense, null, token);
            await using var firstDb = Context(companyId);
            await using var secondDb = Context(companyId);
            var delete = Service(firstDb).DeleteAsync(parent.Id, parent.Version, token);
            var create = Service(secondDb).CreateAsync($"9{attempt}01", "Child", parent.Id, null, null, token);
            var results = await Task.WhenAll(Settle(delete), Settle(create));
            Assert.Equal(1, results.Count(r => r is null));
            await using var check = Context(companyId);
            var rows = await check.Set<Account>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && (x.Id == parent.Id || x.ParentId == parent.Id)).ToListAsync(token);
            var child = rows.SingleOrDefault(x => x.ParentId == parent.Id);
            Assert.True(child is null || !rows.Single(x => x.Id == parent.Id).IsDeleted);
        }
    }

    private static async Task<Exception?> Settle(Task task)
    {
        try { await task; return null; }
        catch (ErpException ex) { return ex; }
    }

    [Fact]
    public void AccountEnumsTravelAsNamesInJson()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal("\"Cost\"", System.Text.Json.JsonSerializer.Serialize(AccountType.Cost, options));
        Assert.Equal(BalanceDirection.Credit, System.Text.Json.JsonSerializer.Deserialize<BalanceDirection>("\"Credit\"", options));
    }

    private sealed class FixedUsage(IEnumerable<string> codes) : IAccountUsage
    {
        private readonly HashSet<string> used = [.. codes];

        public Task<bool> IsInUseAsync(string accountCode, CancellationToken token = default) =>
            Task.FromResult(used.Contains(accountCode));
    }
}
