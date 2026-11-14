using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>FI-GL-USAGE-001～004：Posted 傳票分錄引用的科目由 FI 回報使用中，MD 拒絕不相容維護（BR-FI-027，ADR-039）。</summary>
public sealed class FinanceAccountUsageTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 11, 6)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new AccountDbModel(), new GlPeriodDbModel(), new GlJournalDbModel(), new SalesInvoiceAccountMapDbModel(),
                new PurchaseInvoiceAccountMapDbModel()], company);
    }

    private static long NewCompany() => Random.Shared.NextInt64(96000000, 97000000);

    /// <summary>以 SQL 直接保存一張 Posted 傳票（借 debitCode、貸 creditCode）；過帳寫入服務屬後續切片。</summary>
    private static async Task PostAsync(MyErpDbContext db, long companyId, (long Id, string Code) debit, (long Id, string Code) credit,
        CancellationToken token)
    {
        var source = Random.Shared.NextInt64(10000000, 90000000);
        await db.Database.ExecuteSqlInterpolatedAsync($"IF NOT EXISTS (SELECT 1 FROM dbo.gl_period WHERE company_id={companyId}) INSERT dbo.gl_period (company_id,start_date,end_date,status,created_at,created_by,version) VALUES ({companyId},'2026-11-01','2026-11-30','Open','2026-11-01',1,1)", token);
        var periodId = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={companyId}").SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({companyId},'SD','Invoice',{source},1,'fingerprint','2026-11-06',1,1)", token);
        var keyId = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={companyId} AND source_id={source}").SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({companyId},{periodId},{keyId},'2026-11-06','TWD','Posted',100,100,'2026-11-06',1,1)", token);
        var journalId = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_journal WHERE company_id={companyId} AND source_key_id={keyId}").SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal_line (company_id,journal_id,sequence,account_id,account_code,account_name,account_version,mapping_version,debit,credit,created_at,created_by,version) VALUES ({companyId},{journalId},1,{debit.Id},{debit.Code},N'借方',1,1,100,0,'2026-11-06',1,1)", token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal_line (company_id,journal_id,sequence,account_id,account_code,account_name,account_version,mapping_version,debit,credit,created_at,created_by,version) VALUES ({companyId},{journalId},2,{credit.Id},{credit.Code},N'貸方',1,1,0,100,'2026-11-06',1,1)", token);
    }

    [Fact]
    public async Task PostedJournalLineAccountsAreInUseAndMdRejectsIncompatibleMaintenance()
    {
        // FI-GL-USAGE-001／002
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var db = Context(companyId);
        var usage = new FinanceAccountUsage(db);
        var service = new AccountService(db, [usage]);
        var assets = await service.CreateAsync("1", "Assets", null, AccountType.Asset, null, token);
        var cash = await service.CreateAsync("1001", "Cash", assets.Id, null, null, token);
        var bank = await service.CreateAsync("1002", "Bank", assets.Id, null, null, token);
        var revenues = await service.CreateAsync("4", "Revenue", null, AccountType.Revenue, null, token);
        var sales = await service.CreateAsync("4001", "Sales", revenues.Id, null, null, token);

        Assert.False(await usage.IsInUseAsync("1001", token));
        await PostAsync(db, companyId, (cash.Id, cash.Code), (sales.Id, sales.Code), token);

        Assert.True(await usage.IsInUseAsync("1001", token));
        Assert.True(await usage.IsInUseAsync("4001", token));
        // 完全相符才算：上層代碼、前綴相同的其他代碼、未出現的科目都不是使用中。
        Assert.False(await usage.IsInUseAsync("1", token));
        Assert.False(await usage.IsInUseAsync("100", token));
        Assert.False(await usage.IsInUseAsync("10011", token));
        Assert.False(await usage.IsInUseAsync("1002", token));

        Assert.Equal("MD.ACCOUNT.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.DeleteAsync(cash.Id, cash.Version, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(sales.Id, "Sales", BalanceDirection.Debit, true, sales.Version, token))).ErrorCode);
        Assert.Equal("MD.ACCOUNT.PARENT_IN_USE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync("100101", "Petty cash", cash.Id, null, null, token))).ErrorCode);

        // 名稱與啟用狀態仍可維護（傳票保存的是永久快照）；未使用的同層科目照常刪除。
        var renamed = await service.UpdateAsync(cash.Id, "Cash on hand", BalanceDirection.Debit, false, cash.Version, token);
        Assert.Equal(("Cash on hand", false), (renamed.Name, renamed.IsActive));
        Assert.Equal("借方", await db.Database.SqlQuery<string>(
            $"SELECT account_name AS [Value] FROM dbo.gl_journal_line WHERE company_id={companyId} AND account_code='1001'").SingleAsync(token));
        await service.DeleteAsync(bank.Id, bank.Version, token);
    }

    [Fact]
    public async Task OtherCompanyPostedLinesDoNotMarkAccountInUse()
    {
        // FI-GL-USAGE-003
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var otherId = companyId + 1000000;
        await using var other = Context(otherId);
        await PostAsync(other, otherId, (5, "1001"), (6, "4001"), token);
        Assert.True(await new FinanceAccountUsage(other).IsInUseAsync("1001", token));

        await using var db = Context(companyId);
        var usage = new FinanceAccountUsage(db);
        Assert.False(await usage.IsInUseAsync("1001", token));
        var service = new AccountService(db, [usage]);
        var assets = await service.CreateAsync("1", "Assets", null, AccountType.Asset, null, token);
        var cash = await service.CreateAsync("1001", "Cash", assets.Id, null, null, token);
        var changed = await service.UpdateAsync(cash.Id, "Cash", BalanceDirection.Credit, true, cash.Version, token);
        Assert.Equal("Credit", changed.BalanceDirection);
        await service.DeleteAsync(cash.Id, changed.Version, token);
    }

    [Fact]
    public async Task BlankOrUnknownCodeIsNotInUse()
    {
        // FI-GL-USAGE-004
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var db = Context(companyId);
        await PostAsync(db, companyId, (5, "1001"), (6, "4001"), token);
        var usage = new FinanceAccountUsage(db);
        Assert.False(await usage.IsInUseAsync("", token));
        Assert.False(await usage.IsInUseAsync("   ", token));
        Assert.False(await usage.IsInUseAsync(null!, token));
        Assert.False(await usage.IsInUseAsync("9999", token));
    }

    [Fact]
    public async Task JournalLineHasCompanyAccountCodeLookupIndex()
    {
        // FI-GL-USAGE-005
        await using var db = Context(NewCompany());
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gl_journal_line')").ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("ix_gl_journal_line_company_id_account_code", indexes);
    }

    [Fact]
    public async Task PurchaseInvoiceMapAccountsAreInUseOnlyForTheirCompany()
    {
        // FI-GL-USAGE-007（WU-FI-003a2c，BR-FI-150）：採購發票科目映射的三個代碼對 MD 是使用中；他公司的映射不影響本公司。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using (var other = Context(companyId + 1000000))
        {
            other.Add(new PurchaseInvoiceAccountMapping
            {
                CompanyId = companyId + 1000000,
                PayableAccountCode = "2109",
                PurchaseAccountCode = "5119",
                InputTaxAccountCode = "1159"
            });
            await other.SaveChangesAsync(token);
        }

        await using var db = Context(companyId);
        var usage = new FinanceAccountUsage(db);
        Assert.False(await usage.IsInUseAsync("2101", token));
        db.Add(new PurchaseInvoiceAccountMapping
        {
            CompanyId = companyId,
            PayableAccountCode = "2101",
            PurchaseAccountCode = "5111",
            InputTaxAccountCode = "1151"
        });
        await db.SaveChangesAsync(token);

        foreach (var code in new[] { "2101", "5111", "1151" })
            Assert.True(await usage.IsInUseAsync(code, token), code);
        foreach (var code in new[] { "2109", "5119", "1159", "21", "51111", "1152" })
            Assert.False(await usage.IsInUseAsync(code, token), code);
    }
}
