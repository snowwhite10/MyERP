using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>收支類別與資金帳戶（ADR-041）：方向與幣別固定、銀行欄位、對應科目、使用檢查與查詢契約。</summary>
public sealed class FundSetupTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new CurrencyDbModel(), new AccountDbModel(), new FundSetupDbModel()], company);
    }

    private static IncomeExpenseCategoryService Categories(MyErpDbContext db, params string[] usedCodes) =>
        new(db, usedCodes.Length == 0 ? [] : [new FixedUsage(usedCodes)]);

    private static FundAccountService Funds(MyErpDbContext db, params string[] usedCodes) =>
        new(db, usedCodes.Length == 0 ? [] : [new FixedUsage(usedCodes)]);

    private static AccountService Accounts(MyErpDbContext db) => new(db, [new FundSetupAccountUsage(db)]);

    private static FundAccountInput Bank(string? bankName, string? accountNo, long? ledgerAccountId = null) =>
        new(FundAccountType.Bank, bankName, accountNo, ledgerAccountId);

    private static FundAccountInput Cash(long? ledgerAccountId = null) => new(FundAccountType.Cash, null, null, ledgerAccountId);

    /// <summary>建立資產、收入、成本、費用四個一級科目及各一個末級科目，回傳代碼對 Id。</summary>
    private static async Task<Dictionary<string, long>> SeedAccountsAsync(MyErpDbContext db, CancellationToken token)
    {
        var service = Accounts(db);
        var ids = new Dictionary<string, long>();
        foreach (var (root, leaf, type) in new[]
        {
            ("1", "1002", AccountType.Asset), ("4", "4301", AccountType.Revenue),
            ("5", "5401", AccountType.Cost), ("6", "6601", AccountType.Expense),
        })
        {
            ids[root] = (await service.CreateAsync(root, root, null, type, null, token)).Id;
            ids[leaf] = (await service.CreateAsync(leaf, leaf, ids[root], null, null, token)).Id;
        }
        return ids;
    }

    [Fact]
    public async Task IncomeExpenseCategoriesKeepTheirDirectionAndCheckLedgerAccounts()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(46000000, 47000000);
        await using var db = Context(companyId);
        var accounts = await SeedAccountsAsync(db, token);
        var service = Categories(db);

        var interest = await service.CreateAsync(" interest ", " Interest income ", IncomeExpenseDirection.Income, accounts["4301"], token);
        Assert.Equal(("INTEREST", "Interest income", "Income", accounts["4301"], true, 7L),
            (interest.Code, interest.Name, interest.Direction, interest.LedgerAccountId, interest.IsActive, interest.CreatedBy));
        var freight = await service.CreateAsync("FREIGHT", "Freight", IncomeExpenseDirection.Expense, accounts["5401"], token);
        var rent = await service.CreateAsync("RENT", "Rent", IncomeExpenseDirection.Expense, null, token);
        Assert.Null(rent.LedgerAccountId);
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("rent", "Again", IncomeExpenseDirection.Expense, null, token))).ErrorCode);
        Assert.Equal("direction", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("BAD", "Bad", (IncomeExpenseDirection)9, null, token))).Errors).Key);

        // 收入限收入科目，支出限費用或成本科目；必須是可過帳的末級科目。
        foreach (var (direction, account) in new[]
        {
            (IncomeExpenseDirection.Income, "6601"), (IncomeExpenseDirection.Income, "4"),
            (IncomeExpenseDirection.Expense, "4301"), (IncomeExpenseDirection.Expense, "1002"),
        })
            Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.INVALID_LEDGER_ACCOUNT", (await Assert.ThrowsAsync<BusinessRuleException>(
                () => service.CreateAsync("X" + account, "X", direction, accounts[account], token))).ErrorCode);
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.INVALID_LEDGER_ACCOUNT", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("GHOST", "Ghost", IncomeExpenseDirection.Expense, long.MaxValue, token))).ErrorCode);

        rent = await service.UpdateAsync(rent.Id, "Office rent", accounts["6601"], true, rent.Version, token);
        Assert.Equal(("Office rent", "Expense", accounts["6601"]), (rent.Name, rent.Direction, rent.LedgerAccountId));
        freight = await service.UpdateAsync(freight.Id, "Freight", null, false, freight.Version, token);
        Assert.Equal(((long?)null, false), (freight.LedgerAccountId, freight.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(rent.Id, "Stale", null, true, rent.Version - 1, token));

        Assert.Equal(["FREIGHT", "RENT"],
            (await service.ListAsync(new(), IncomeExpenseDirection.Expense, token)).Items.Select(x => x.Code));
        Assert.Equal(["INTEREST"], (await service.ListAsync(new() { Keyword = "inter" }, null, token)).Items.Select(x => x.Code));

        // 別公司看不到，也不可引用本公司的科目。
        await using var otherDb = Context(companyId + 1);
        Assert.Empty((await Categories(otherDb).ListAsync(new(), null, token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => Categories(otherDb).UpdateAsync(rent.Id, "X", null, true, rent.Version, token));
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.INVALID_LEDGER_ACCOUNT", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => Categories(otherDb).CreateAsync("RENT", "Rent", IncomeExpenseDirection.Expense, accounts["6601"], token))).ErrorCode);

        var lookup = new IncomeExpenseCategoryLookup(db);
        Assert.Equal(new IncomeExpenseCategoryInfo("INTEREST", "Interest income", IncomeExpenseDirection.Income, "4301", true),
            await lookup.FindAsync(" interest ", token));
        Assert.Null(await lookup.FindAsync("NONE", token));
        Assert.Equal("RENT", (await lookup.RequireActiveAsync("rent", IncomeExpenseDirection.Expense, token)).Code);
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.DIRECTION_MISMATCH", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => lookup.RequireActiveAsync("RENT", IncomeExpenseDirection.Income, token))).ErrorCode);
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => lookup.RequireActiveAsync("FREIGHT", IncomeExpenseDirection.Expense, token))).ErrorCode);
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => lookup.RequireActiveAsync("NONE", IncomeExpenseDirection.Expense, token))).ErrorCode);
        Assert.Null(await new IncomeExpenseCategoryLookup(otherDb).FindAsync("RENT", token));

        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => Categories(db, "RENT").DeleteAsync(rent.Id, rent.Version, token))).ErrorCode);
        await service.DeleteAsync(rent.Id, rent.Version, token);
        Assert.Null(await lookup.FindAsync("RENT", token));
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("RENT", "Rent again", IncomeExpenseDirection.Expense, null, token))).ErrorCode);

        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => Categories(noCompany).ListAsync(new(), null, token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new IncomeExpenseCategoryLookup(noCompany).FindAsync("RENT", token));
    }

    [Fact]
    public async Task FundAccountsFixTheirCurrencyAndKeepBankFieldsOnlyForBanks()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(47000000, 48000000);
        await using var db = Context(companyId);
        var accounts = await SeedAccountsAsync(db, token);
        var currencies = new CurrencyService(db);
        var twd = await currencies.CreateAsync("TWD", "New Taiwan dollar", "NT$", 0, true, token);
        var usd = await currencies.CreateAsync("USD", "US dollar", "$", 2, false, token);
        var jpy = await currencies.CreateAsync("JPY", "Yen", null, 0, false, token);
        await currencies.UpdateAsync(jpy.Id, jpy.Name, null, 0, false, false, jpy.Version, token);
        var service = Funds(db);

        var bank = await service.CreateAsync(" bot-usd ", " Bank of Taiwan USD ", usd.Id,
            Bank(" Bank of Taiwan ", " 004-123 456789 ", accounts["1002"]), token);
        Assert.Equal(("BOT-USD", "Bank", usd.Id, "Bank of Taiwan", "004-123 456789", accounts["1002"]),
            (bank.Code, bank.FundType, bank.CurrencyId, bank.BankName, bank.BankAccountNo, bank.LedgerAccountId));
        // 現金帳戶的銀行欄位忽略並存 null。
        var cash = await service.CreateAsync("CASH", "Petty cash", twd.Id, new(FundAccountType.Cash, "Ignored", "1", null), token);
        Assert.Equal(("Cash", (string?)null, (string?)null), (cash.FundType, cash.BankName, cash.BankAccountNo));
        Assert.Equal("MD.FUND_ACCOUNT.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("cash", "Again", twd.Id, Cash(), token))).ErrorCode);

        Assert.Equal("MD.FUND_ACCOUNT.INVALID_CURRENCY", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("JPY", "Yen cash", jpy.Id, Cash(), token))).ErrorCode);
        Assert.Equal("MD.FUND_ACCOUNT.INVALID_CURRENCY", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("NONE", "None", long.MaxValue, Cash(), token))).ErrorCode);
        foreach (var account in new[] { "1", "4301", "6601" })
            Assert.Equal("MD.FUND_ACCOUNT.INVALID_LEDGER_ACCOUNT", (await Assert.ThrowsAsync<BusinessRuleException>(
                () => service.CreateAsync("L" + account, "Ledger", twd.Id, Cash(accounts[account]), token))).ErrorCode);
        Assert.Equal("fundType", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("BAD", "Bad", twd.Id, new((FundAccountType)9, null, null, null), token))).Errors).Key);
        Assert.Equal("bankAccountNo", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("BAD", "Bad", twd.Id, Bank("Bank", "12#34"), token))).Errors).Key);
        Assert.Equal("bankName", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("BAD", "Bad", twd.Id, Bank(new string('B', 201), null), token))).Errors).Key);

        // 改為現金清空銀行欄位；幣別不可修改，回應仍是原幣別。
        bank = await service.UpdateAsync(bank.Id, "USD cash box", Cash(), true, bank.Version, token);
        Assert.Equal(("Cash", (string?)null, (string?)null, (long?)null, usd.Id),
            (bank.FundType, bank.BankName, bank.BankAccountNo, bank.LedgerAccountId, bank.CurrencyId));
        cash = await service.UpdateAsync(cash.Id, "Petty cash", Bank("Mega", "001", accounts["1002"]), false, cash.Version, token);
        Assert.Equal(("Bank", "Mega", "001", false), (cash.FundType, cash.BankName, cash.BankAccountNo, cash.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(cash.Id, "Stale", Cash(), true, cash.Version - 1, token));

        Assert.Equal(["CASH"], (await service.ListAsync(new(), FundAccountType.Bank, token)).Items.Select(x => x.Code));
        Assert.Equal(["CASH", "BOT-USD"], (await service.ListAsync(new() { Sort = "-code" }, null, token)).Items.Select(x => x.Code));
        await using var otherDb = Context(companyId + 1);
        Assert.Empty((await Funds(otherDb).ListAsync(new(), null, token)).Items);
        Assert.Equal("MD.FUND_ACCOUNT.INVALID_CURRENCY", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => Funds(otherDb).CreateAsync("CASH", "Cash", twd.Id, Cash(), token))).ErrorCode);

        var lookup = new FundAccountLookup(db);
        Assert.Equal(new FundAccountInfo("CASH", "Petty cash", FundAccountType.Bank, "TWD", "Mega", "001", "1002", false),
            await lookup.FindAsync(" cash ", token));
        Assert.Equal(new FundAccountInfo("BOT-USD", "USD cash box", FundAccountType.Cash, "USD", null, null, null, true),
            await lookup.RequireActiveAsync("bot-usd", token));
        Assert.Equal("MD.FUND_ACCOUNT.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => lookup.RequireActiveAsync("CASH", token))).ErrorCode);
        Assert.Equal("MD.FUND_ACCOUNT.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => lookup.RequireActiveAsync("NONE", token))).ErrorCode);
        Assert.Null(await new FundAccountLookup(otherDb).FindAsync("CASH", token));

        // 被資金帳戶使用的幣別不可刪除；被引用的資金帳戶只可停用。
        usd = (await db.Set<Currency>().AsNoTracking().SingleAsync(x => x.Id == usd.Id, token));
        Assert.Equal("MD.CURRENCY.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => currencies.DeleteAsync(usd.Id, usd.Version, token))).ErrorCode);
        Assert.Equal("MD.FUND_ACCOUNT.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => Funds(db, "BOT-USD").DeleteAsync(bank.Id, bank.Version, token))).ErrorCode);
        await service.DeleteAsync(bank.Id, bank.Version, token);
        Assert.Null(await lookup.FindAsync("BOT-USD", token));
        await currencies.DeleteAsync(usd.Id, usd.Version, token);

        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => Funds(noCompany).ListAsync(new(), null, token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new FundAccountLookup(noCompany).FindAsync("CASH", token));
    }

    [Fact]
    public async Task LedgerAccountsReferencedByFundSetupAreInUse()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(48000000, 49000000);
        await using var db = Context(companyId);
        var accounts = await SeedAccountsAsync(db, token);
        var twd = await new CurrencyService(db).CreateAsync("TWD", "New Taiwan dollar", null, 0, true, token);
        var fund = await Funds(db).CreateAsync("BANK", "Bank", twd.Id, Bank(null, null, accounts["1002"]), token);
        var category = await Categories(db).CreateAsync("TRAVEL", "Travel", IncomeExpenseDirection.Expense, accounts["6601"], token);

        var usage = new FundSetupAccountUsage(db);
        Assert.True(await usage.IsInUseAsync("1002", token));
        Assert.True(await usage.IsInUseAsync("6601", token));
        Assert.False(await usage.IsInUseAsync("4301", token));

        // AccountService 經 IAccountUsage 自動保護：不可刪除、不可改方向、不可再分子科目。
        var service = Accounts(db);
        var bankAccount = await db.Set<Account>().AsNoTracking().SingleAsync(x => x.Id == accounts["1002"], token);
        Assert.Equal("MD.ACCOUNT.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.DeleteAsync(bankAccount.Id, bankAccount.Version, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(bankAccount.Id, "Bank", BalanceDirection.Credit, true, bankAccount.Version, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.PARENT_IN_USE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("660101", "Domestic travel", accounts["6601"], null, null, token))).ErrorCode);

        // 解除引用（清空科目或刪除）後即可維護。
        await Funds(db).UpdateAsync(fund.Id, fund.Name, Bank(null, null), true, fund.Version, token);
        await Categories(db).DeleteAsync(category.Id, category.Version, token);
        Assert.False(await usage.IsInUseAsync("1002", token));
        Assert.False(await usage.IsInUseAsync("6601", token));
        await service.CreateAsync("660101", "Domestic travel", accounts["6601"], null, null, token);

        // 不是可過帳末級科目時不可設定（已有子科目）。
        Assert.Equal("MD.INCOME_EXPENSE_CATEGORY.INVALID_LEDGER_ACCOUNT", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => Categories(db).CreateAsync("TRAVEL2", "Travel", IncomeExpenseDirection.Expense, accounts["6601"], token))).ErrorCode);
    }

    [Fact]
    public async Task FundSetupTablesHaveChecksAndForeignKeyIndexes()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(49000000, 50000000);
        await using var db = Context(companyId);
        var twd = await new CurrencyService(db).CreateAsync("TWD", "New Taiwan dollar", null, 0, true, token);
        db.Add(new FundAccount { CompanyId = companyId, Code = "BAD", Name = "Bad", FundType = "Cash", CurrencyId = twd.Id, BankName = "X" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        db.ChangeTracker.Clear();
        db.Add(new IncomeExpenseCategory { CompanyId = companyId, Code = "BAD", Name = "Bad", Direction = "Both" });
        error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);

        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN " +
            "(OBJECT_ID('md_fund_account'), OBJECT_ID('md_income_expense_category'))").ToListAsync(token);
        Assert.Contains("ck_md_fund_account_fund_type", checks);
        Assert.Contains("ck_md_fund_account_bank", checks);
        Assert.Contains("ck_md_income_expense_category_direction", checks);
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN " +
            "(OBJECT_ID('md_fund_account'), OBJECT_ID('md_income_expense_category'))").ToListAsync(token);
        Assert.Contains("ux_md_fund_account_company_id_code", indexes);
        Assert.Contains("ix_md_fund_account_company_id_currency_id", indexes);
        Assert.Contains("ix_md_fund_account_company_id_ledger_account_id", indexes);
        Assert.Contains("ux_md_income_expense_category_company_id_code", indexes);
        Assert.Contains("ix_md_income_expense_category_company_id_ledger_account_id", indexes);
    }

    [Fact]
    public void EnumsTravelAsNamesInJson()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal("\"Bank\"", System.Text.Json.JsonSerializer.Serialize(FundAccountType.Bank, options));
        Assert.Equal(IncomeExpenseDirection.Expense,
            System.Text.Json.JsonSerializer.Deserialize<IncomeExpenseDirection>("\"Expense\"", options));
    }

    private sealed class FixedUsage(IEnumerable<string> codes) : IFundAccountUsage, IIncomeExpenseCategoryUsage
    {
        private readonly HashSet<string> used = [.. codes];

        public Task<bool> IsInUseAsync(string code, CancellationToken token = default) => Task.FromResult(used.Contains(code));
    }
}
