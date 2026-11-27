using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MyERP.Modules.Finance.Infrastructure.Seeding;
using MyERP.Modules.Sales.Infrastructure.Seeding;
using MyERP.Modules.System.Application.Seeding;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Seeding;
using MyERP.Shared.Numbering;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-NRSEED-001～006（WU-FI-005c3a、ADR-135）：SYS 單號規則種子契約。FI 經 <see cref="ISeedNumberRuleProvider"/>
/// 宣告 AR.COLLECTION／AR.SETTLEMENT，SYS 種子在每間種子公司建立；以 SQL 讀回驗證保存值、冪等、不覆蓋用戶修改，
/// 以單號產生服務驗證可取號；定義不符或跨模組重複時拋出且不寫入任何列。
/// </summary>
[Collection(SeedDataSerialGroup.Name)]
public sealed class NumberRuleSeedTests(TestDatabase database) : IAsyncLifetime
{
    private static readonly string[] FinanceTypes = ["AP.OTHER_PAYMENT", "AR.COLLECTION", "AR.SETTLEMENT"];
    private static readonly string[] SalesTypes = ["SAL.DELIVERY", "SAL.INVOICE", "SAL.ORDER", "SAL.QUOTATION", "SAL.RETURN"];
    private static readonly string[] SeededTypes = [.. FinanceTypes, .. SalesTypes];
    private WebApplicationFactory<Program> factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => database.UseTestDatabase(b));
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SystemDataSeeder>().SeedAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task SeedCreatesFinanceRulesPerCompanyIssuesNumbersAndSecondRunAddsNothing()
    {
        // FI-COLL-NRSEED-001：註冊為種子程序（SYS 之後、FI 之前），FI 提供者已註冊。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var seeders = scope.ServiceProvider.GetServices<IDataSeeder>().ToList();
            var numberSeeder = Assert.Single(seeders.OfType<NumberRuleDemoSeeder>());
            Assert.True(numberSeeder.Order > seeders.OfType<SystemDataSeeder>().Single().Order);
            Assert.True(numberSeeder.Order < seeders.OfType<GlDemoSeeder>().Single().Order);
            Assert.Contains(scope.ServiceProvider.GetServices<ISeedNumberRuleProvider>(), p => p is FinanceNumberRuleSeeds);
        }

        var companies = await CompaniesAsync();
        var missingBefore = await CountMissingAsync(companies.Values);
        Assert.Equal(missingBefore, (await SeedAsync()).Rules);
        Assert.Equal(0, (await SeedAsync()).Rules);

        // FI-COLL-NRSEED-002：兩間公司各一條，前綴／年月／4 位／每月重設／啟用。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            foreach (var companyId in companies.Values)
            {
                var rules = await db.Set<NumberRule>().IgnoreQueryFilters().AsNoTracking()
                    .Where(r => r.CompanyId == companyId && FinanceTypes.Contains(r.DocumentType))
                    .OrderBy(r => r.DocumentType)
                    .ToListAsync(Ct);
                Assert.Equal(
                    [
                        ("AP.OTHER_PAYMENT", "其他付款", "OP", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                        ("AR.COLLECTION", "銷售收款", "RC", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                        ("AR.SETTLEMENT", "收款核銷", "RS", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                    ],
                    rules.Select(r => (r.DocumentType, r.Name, r.Prefix, r.DateFormat, r.SequenceLength, r.ResetPeriod, r.IsActive))
                        .ToArray());
            }
        }

        // FI-COLL-NRSEED-003：種子規則可經單號產生服務取號；交易回滾不佔用流水號。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var generator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
            using var context = SeedCompanyContext.Enter(companies["TW01"]);
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            var receiptNo = await generator.NextAsync("AR.COLLECTION", new DateOnly(2026, 11, 15), Ct);
            var settlementNo = await generator.NextAsync("AR.SETTLEMENT", new DateOnly(2026, 11, 15), Ct);
            Assert.Matches("^RC202611[0-9]{4}$", receiptNo);
            Assert.Matches("^RS202611[0-9]{4}$", settlementNo);
            // WU-FI-008b1：其他付款 OP＋年月＋4 位。
            Assert.Matches("^OP202611[0-9]{4}$", await generator.NextAsync("AP.OTHER_PAYMENT", new DateOnly(2026, 11, 15), Ct));
            await transaction.RollbackAsync(Ct);
        }
    }

    [Fact]
    public async Task SeedCreatesSalesRulesPerCompanyAndTheyIssueNumbers()
    {
        // SD-PRICE-NRSEED-001（WU-SD-005b3a、ADR-166）：SD 經同一契約宣告報價 SQ、訂單 SO、出庫 DN（年月＋4 位、每月重設），
        // 每間種子公司一條；可經單號產生服務取號（Development 種子後報價／訂單不再因缺單號規則回 422）。
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Contains(scope.ServiceProvider.GetServices<ISeedNumberRuleProvider>(), p => p is SalesNumberRuleSeeds);
        await SeedAsync();
        var companies = await CompaniesAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            foreach (var companyId in companies.Values)
            {
                var rules = await db.Set<NumberRule>().IgnoreQueryFilters().AsNoTracking()
                    .Where(r => r.CompanyId == companyId && SalesTypes.Contains(r.DocumentType))
                    .OrderBy(r => r.DocumentType)
                    .ToListAsync(Ct);
                Assert.Equal(
                    [
                        ("SAL.DELIVERY", "銷售出庫", "DN", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                        ("SAL.INVOICE", "銷售發票", "SI", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                        ("SAL.ORDER", "銷售訂單", "SO", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                        ("SAL.QUOTATION", "銷售報價", "SQ", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                        ("SAL.RETURN", "銷售退貨", "SR", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly, true),
                    ],
                    rules.Select(r => (r.DocumentType, r.Name, r.Prefix, r.DateFormat, r.SequenceLength, r.ResetPeriod, r.IsActive))
                        .ToArray());
            }
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var generator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
            using var context = SeedCompanyContext.Enter(companies["TW01"]);
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            Assert.Matches("^SQ202611[0-9]{4}$", await generator.NextAsync("SAL.QUOTATION", new DateOnly(2026, 11, 22), Ct));
            Assert.Matches("^SO202611[0-9]{4}$", await generator.NextAsync("SAL.ORDER", new DateOnly(2026, 11, 22), Ct));
            Assert.Matches("^DN202611[0-9]{4}$", await generator.NextAsync("SAL.DELIVERY", new DateOnly(2026, 11, 22), Ct));
            await transaction.RollbackAsync(Ct);
        }
    }

    [Fact]
    public async Task SeedKeepsUserModifiedAndDeactivatedRules()
    {
        // FI-COLL-NRSEED-004：用戶改前綴、停用另一條後重跑 → 新增 0，保存值與版本不變；之後還原。
        await SeedAsync();
        var companies = await CompaniesAsync();
        var tw = companies["TW01"];
        var hk = companies["HK01"];
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var collection = await db.Set<NumberRule>().IgnoreQueryFilters()
            .SingleAsync(r => r.CompanyId == tw && r.DocumentType == "AR.COLLECTION", Ct);
        var settlement = await db.Set<NumberRule>().IgnoreQueryFilters()
            .SingleAsync(r => r.CompanyId == hk && r.DocumentType == "AR.SETTLEMENT", Ct);
        var (collectionPrefix, settlementActive) = (collection.Prefix, settlement.IsActive);
        collection.Prefix = "TWRC";
        using (SeedCompanyContext.Enter(tw))
            await db.SaveChangesAsync(Ct);
        settlement.IsActive = false;
        using (SeedCompanyContext.Enter(hk))
            await db.SaveChangesAsync(Ct);
        try
        {
            Assert.Equal(0, (await SeedAsync()).Rules);
            var kept = await db.Set<NumberRule>().IgnoreQueryFilters().AsNoTracking()
                .Where(r => (r.CompanyId == tw && r.DocumentType == "AR.COLLECTION")
                    || (r.CompanyId == hk && r.DocumentType == "AR.SETTLEMENT"))
                .ToDictionaryAsync(r => r.DocumentType, Ct);
            Assert.Equal(("TWRC", collection.Version), (kept["AR.COLLECTION"].Prefix, kept["AR.COLLECTION"].Version));
            Assert.Equal((false, settlement.Version), (kept["AR.SETTLEMENT"].IsActive, kept["AR.SETTLEMENT"].Version));
            Assert.Equal(1, await db.Set<NumberRule>().IgnoreQueryFilters()
                .CountAsync(r => r.CompanyId == tw && r.DocumentType == "AR.COLLECTION", Ct));
        }
        finally
        {
            collection.Prefix = collectionPrefix;
            using (SeedCompanyContext.Enter(tw))
                await db.SaveChangesAsync(Ct);
            settlement.IsActive = settlementActive;
            using (SeedCompanyContext.Enter(hk))
                await db.SaveChangesAsync(Ct);
        }
    }

    [Fact]
    public async Task InvalidOrConflictingDefinitionWritesNothing()
    {
        // FI-COLL-NRSEED-005：一個合法加一個不合法的定義 → 拋出（含模組、單據類型、欄位），合法那條也不寫入。
        var good = new TestProvider("QA", new SeedNumberRuleDefinition("QA.SEED_GOOD", "QA good", "QG"));
        var bad = new TestProvider("QB", new SeedNumberRuleDefinition("QB.SEED_BAD", "QA bad", "Q B"));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var seeder = new NumberRuleDemoSeeder(db, scope.ServiceProvider.GetRequiredService<ISeedCompanyDirectory>(),
            [good, bad], NullLogger<NumberRuleDemoSeeder>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(Ct));
        Assert.Contains("QB.SEED_BAD", error.Message, StringComparison.Ordinal);
        Assert.Contains("QB", error.Message, StringComparison.Ordinal);
        Assert.Contains("prefix", error.Message, StringComparison.Ordinal);
        Assert.False(await db.Set<NumberRule>().IgnoreQueryFilters()
            .AnyAsync(r => r.DocumentType == "QA.SEED_GOOD" || r.DocumentType == "QB.SEED_BAD", Ct));
    }

    [Fact]
    public void PlanNormalizesAndRejectsDuplicatesAndUncoveredResetPeriods()
    {
        // FI-COLL-NRSEED-006：FI 定義正規化；兩模組宣告同一類型（不分大小寫）、每月重設但只有年份、
        // 名稱空白、流水號長度超出範圍 → 拋出。
        var plan = NumberRuleDemoSeeder.BuildPlan([new FinanceNumberRuleSeeds()]);
        Assert.Equal(
            [
                ("FI", "AP.OTHER_PAYMENT", "OP", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly),
                ("FI", "AR.COLLECTION", "RC", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly),
                ("FI", "AR.SETTLEMENT", "RS", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly),
            ],
            plan.Select(r => (r.Module, r.DocumentType, r.Prefix, r.DateFormat, r.SequenceLength, r.ResetPeriod)).ToArray());

        var normalized = Assert.Single(NumberRuleDemoSeeder.BuildPlan(
            [new TestProvider("QA", new SeedNumberRuleDefinition(" qa.doc ", "  Name  ", " ab-1 "))]));
        Assert.Equal(("QA.DOC", "Name", "AB-1"), (normalized.DocumentType, normalized.Name, normalized.Prefix));

        var duplicate = Assert.Throws<InvalidOperationException>(() => NumberRuleDemoSeeder.BuildPlan(
        [
            new FinanceNumberRuleSeeds(),
            new TestProvider("SD", new SeedNumberRuleDefinition("ar.collection", "Other", "OC")),
        ]));
        Assert.Contains("AR.COLLECTION", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("FI", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("SD", duplicate.Message, StringComparison.Ordinal);

        foreach (var (definition, field) in new[]
                 {
                     (new SeedNumberRuleDefinition("QA.DOC", "Name", "Q", SeedNumberDatePart.Year), "resetPeriod"),
                     (new SeedNumberRuleDefinition("QA.DOC", "   ", "Q"), "name"),
                     (new SeedNumberRuleDefinition("QA.DOC", new string('n', 101), "Q"), "name"),
                     (new SeedNumberRuleDefinition("QA.DOC", "Name", "Q", SequenceLength: 2), "sequenceLength"),
                     (new SeedNumberRuleDefinition("QA.DOC", "Name", "Q", SequenceLength: 11), "sequenceLength"),
                     (new SeedNumberRuleDefinition("QA", "Name", "Q"), "documentType"),
                     (new SeedNumberRuleDefinition("QA.DOC", "Name", "PREFIX12345"), "prefix"),
                 })
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => NumberRuleDemoSeeder.BuildPlan([new TestProvider("QA", definition)]));
            Assert.Contains(field, error.Message, StringComparison.Ordinal);
        }
    }

    private async Task<NumberRuleSeedResult> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NumberRuleDemoSeeder>().SeedAsync(Ct);
    }

    private async Task<Dictionary<string, long>> CompaniesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var list = await scope.ServiceProvider.GetRequiredService<ISeedCompanyDirectory>().ListAsync(Ct);
        Assert.Equal(["HK01", "TW01"], list.Select(c => c.Code).ToArray());
        return list.ToDictionary(c => c.Code, c => c.Id, StringComparer.Ordinal);
    }

    private async Task<int> CountMissingAsync(IEnumerable<long> companyIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var missing = 0;
        foreach (var companyId in companyIds)
        {
            var count = await db.Set<NumberRule>().IgnoreQueryFilters()
                .CountAsync(r => r.CompanyId == companyId && SeededTypes.Contains(r.DocumentType), Ct);
            missing += SeededTypes.Length - count;
        }
        return missing;
    }

    private sealed class TestProvider(string module, params SeedNumberRuleDefinition[] rules) : ISeedNumberRuleProvider
    {
        public string Module => module;

        public IReadOnlyList<SeedNumberRuleDefinition> Rules => rules;
    }
}
