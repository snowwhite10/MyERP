using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure.Seeding;
using MyERP.Modules.MasterData.Application.Seeding;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.System.Application.Seeding;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Seeding;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-GL-SEED-001～004（WU-FI-004c4a、ADR-105）：MD 幣別／科目表與 FI 期間／示範傳票種子；FI-SALES-MAP-SEED-001～002
/// （WU-FI-002a4c2、ADR-112）：銷項稅科目 2141 與 SD 發票科目映射示範列。固定時鐘 2026-11-13，
/// 經 DI 依 SYS → MD → FI 執行兩次；以 SQL 讀回驗證保存值、精確十進位、期間狀態與冪等。
/// </summary>
[Collection(SeedDataSerialGroup.Name)]
public sealed class DemoSeedTests(TestDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 13, 10, 0, 0);
    private WebApplicationFactory<Program> factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            database.UseTestDatabase(b);
            b.ConfigureServices(s => s.AddSingleton<IClock>(new FixedClock(Now)));
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task SeedCreatesChartPeriodsAndPostedJournalsThroughRulesAndSecondRunAddsNothing()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SystemDataSeeder>().SeedAsync(Ct);
        var (md, fi) = await SeedAsync();
        Assert.Equal(new MasterDataSeedResult(10, 36), md);
        // 12 期間／公司；1–9 月關閉；TW01 11 個月×2、HK01 11 個月×2＋精度傳票 1；SD 發票科目映射每公司 1 列。
        Assert.Equal(new GlDemoSeedResult(24, 45, 18, 2), fi);
        Assert.Equal((new MasterDataSeedResult(0, 0), new GlDemoSeedResult(0, 0, 0, 0)), await SeedAsync());

        await using var readScope = factory.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var companies = await db.Set<SystemCompany>().IgnoreQueryFilters()
            .Where(c => c.Code == "TW01" || c.Code == "HK01").ToDictionaryAsync(c => c.Code, c => c.Id, Ct);
        var tw = companies["TW01"];
        var hk = companies["HK01"];

        var bases = await db.Set<Currency>().IgnoreQueryFilters().Where(c => c.IsBase && (c.CompanyId == tw || c.CompanyId == hk))
            .Select(c => new { c.CompanyId, c.Code, c.DecimalPlaces }).ToListAsync(Ct);
        Assert.Equal([(tw, "TWD", 0), (hk, "HKD", 2)],
            bases.OrderBy(c => c.CompanyId == tw ? 0 : 1).Select(c => (c.CompanyId, c.Code, c.DecimalPlaces)).ToArray());
        var cash = await db.Set<Account>().IgnoreQueryFilters().SingleAsync(a => a.CompanyId == tw && a.Code == "1131", Ct);
        Assert.Equal(("應收帳款", "Asset"), (cash.Name, cash.AccountType));

        var periods = await db.Set<GlPeriod>().IgnoreQueryFilters().Where(p => p.CompanyId == tw)
            .OrderBy(p => p.StartDate).ToListAsync(Ct);
        Assert.Equal(12, periods.Count);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), GlPeriodStatus.Closed, 2),
            (periods[8].StartDate, periods[8].EndDate, periods[8].Status, periods[8].Version));
        Assert.All(periods.Skip(9), p => Assert.Equal(GlPeriodStatus.Open, p.Status));
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), (periods[1].StartDate, periods[1].EndDate));

        var twNov = await JournalAsync(db, tw, 2026111);
        Assert.Equal((new DateOnly(2026, 11, 13), "TWD", GlJournalStatus.Posted, 235795m, 235795m, periods[10].Id),
            (twNov.Journal.BusinessDate, twNov.Journal.BaseCurrency, twNov.Journal.Status, twNov.Journal.TotalDebit,
                twNov.Journal.TotalCredit, twNov.Journal.PeriodId));
        Assert.Equal([("1131", "應收帳款", 235795m, 0m, 1), ("4101", "銷貨收入", 0m, 235795m, 1)],
            twNov.Lines.Select(l => (l.AccountCode, l.AccountName, l.Debit, l.Credit, l.MappingVersion)).ToArray());
        Assert.Equal(("FI", "DemoSeed", 1, "seed-v1:235795:1131:4101"),
            (twNov.Key.SourceModule, twNov.Key.SourceType, twNov.Key.SourceVersion, twNov.Key.Fingerprint));

        var hkJan = await JournalAsync(db, hk, 2026011);
        Assert.Equal((new DateOnly(2026, 1, 15), "HKD", 26234.75m), (hkJan.Journal.BusinessDate, hkJan.Journal.BaseCurrency, hkJan.Journal.TotalDebit));
        var precision = await JournalAsync(db, hk, 2026119);
        Assert.Equal(123456789012345.67m, precision.Journal.TotalDebit);
        Assert.Equal(123456789012345.67m, precision.Journal.TotalCredit);
        Assert.Equal([("1102", 123456789012345.67m, 0m), ("3101", 0m, 123456789012345.67m)],
            precision.Lines.Select(l => (l.AccountCode, l.Debit, l.Credit)).ToArray());

        Assert.Equal(22, await db.Set<GlJournal>().IgnoreQueryFilters().CountAsync(j => j.CompanyId == tw, Ct));
        Assert.Equal(23, await db.Set<GlJournal>().IgnoreQueryFilters().CountAsync(j => j.CompanyId == hk, Ct));
        Assert.False(await db.Set<GlJournal>().IgnoreQueryFilters()
            .AnyAsync(j => (j.CompanyId == tw || j.CompanyId == hk) && j.TotalDebit != j.TotalCredit, Ct));

        // FI-SALES-MAP-SEED-001：2141 銷項稅額是負債、啟用、無子科目（可過帳）；兩間公司映射 1131／4101／2141、版本 1。
        foreach (var companyId in new[] { tw, hk })
        {
            var tax = await db.Set<Account>().IgnoreQueryFilters().SingleAsync(a => a.CompanyId == companyId && a.Code == "2141", Ct);
            Assert.Equal(("銷項稅額", "Liability", "Credit", true, false),
                (tax.Name, tax.AccountType, tax.BalanceDirection, tax.IsActive, tax.IsDeleted));
            Assert.False(await db.Set<Account>().IgnoreQueryFilters().AnyAsync(a => a.ParentId == tax.Id, Ct));
            var map = await db.Set<SalesInvoiceAccountMapping>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(m => m.CompanyId == companyId, Ct);
            Assert.Equal(("1131", "4101", "2141", 1),
                (map.ReceivableAccountCode, map.RevenueAccountCode, map.OutputTaxAccountCode, map.Version));
        }

        // FI-SALES-MAP-SEED-002：用戶改過的映射不被種子覆蓋（版本與科目都不變），重跑新增 0 列；之後還原。
        var userMap = await db.Set<SalesInvoiceAccountMapping>().IgnoreQueryFilters().SingleAsync(m => m.CompanyId == tw, Ct);
        userMap.OutputTaxAccountCode = "2171";
        using (SeedCompanyContext.Enter(tw))
            await db.SaveChangesAsync(Ct);
        Assert.Equal(2, userMap.Version);
        Assert.Equal(0, (await SeedAsync()).Item2.SalesInvoiceMaps);
        var kept = await db.Set<SalesInvoiceAccountMapping>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(m => m.CompanyId == tw, Ct);
        Assert.Equal(("1131", "4101", "2171", 2),
            (kept.ReceivableAccountCode, kept.RevenueAccountCode, kept.OutputTaxAccountCode, kept.Version));
        userMap.OutputTaxAccountCode = "2141";
        using (SeedCompanyContext.Enter(tw))
            await db.SaveChangesAsync(Ct);
    }

    [Theory]
    [InlineData("Development", 1)]
    [InlineData("Production", 0)]
    [InlineData("Staging", 0)]
    public async Task DemoMappingIsUsableOnlyInDevelopment(string environment, int expected)
    {
        // FI-GL-SEED-004：非 Development 版本 0，過帳決策拒絕（封閉失敗）。
        var mapping = new GlDemoSeedMapping(new HostingEnvironment { EnvironmentName = environment });
        Assert.Equal(("FI", "DemoSeed"), (mapping.SourceModule, mapping.SourceType));
        Assert.Equal(expected, await mapping.ReadCurrentVersionAsync(1, Ct));
    }

    [Fact]
    public async Task DemoMappingWithoutHostEnvironmentFailsClosedAndResolvesFromDi()
    {
        // FI-GL-SEED-004：只組模組、沒有主機環境的 DI 也能解析全部映射來源，示範映射為 0。
        var services = new ServiceCollection();
        services.AddScoped<MyERP.Modules.Finance.Application.IGlPostingMappingVersionSource, GlDemoSeedMapping>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mapping = Assert.Single(scope.ServiceProvider.GetServices<MyERP.Modules.Finance.Application.IGlPostingMappingVersionSource>());
        Assert.Equal(0, await mapping!.ReadCurrentVersionAsync(1, Ct));
    }

    private async Task<(MasterDataSeedResult, GlDemoSeedResult)> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var md = await scope.ServiceProvider.GetRequiredService<MasterDataDemoSeeder>().SeedAsync(Ct);
        var fi = await scope.ServiceProvider.GetRequiredService<GlDemoSeeder>().SeedAsync(Ct);
        return (md, fi);
    }

    private static async Task<(GlSourceKey Key, GlJournal Journal, List<GlJournalLine> Lines)> JournalAsync(
        MyErpDbContext db, long companyId, long sourceId)
    {
        var key = await db.Set<GlSourceKey>().IgnoreQueryFilters()
            .SingleAsync(k => k.CompanyId == companyId && k.SourceModule == "FI" && k.SourceType == "DemoSeed" && k.SourceId == sourceId, Ct);
        var journal = await db.Set<GlJournal>().IgnoreQueryFilters().SingleAsync(j => j.SourceKeyId == key.Id, Ct);
        var lines = await db.Set<GlJournalLine>().IgnoreQueryFilters().Where(l => l.JournalId == journal.Id)
            .OrderBy(l => l.Sequence).ToListAsync(Ct);
        return (key, journal, lines);
    }
}
