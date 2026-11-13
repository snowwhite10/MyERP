using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

// 本檔的原始 SQL 片段全由測試常數與 Random 產生的公司 ID 組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-SALES-MAP-001～005（WU-FI-002a4a，ADR-109）：SD 發票科目映射的正式來源 <c>gl_sales_invoice_account_map</c>。
/// 一間公司一列，列的 <c>version</c> 就是映射版本；<see cref="ISalesInvoiceAccountMapSource"/> 與 GL 的
/// <c>SD/Invoice</c> <see cref="IGlPostingMappingVersionSource"/> 由同一實作讀同一列，因此版本必然一致。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static readonly int[] MapSqlErrors = [547, 2601, 2627];

    private static readonly string[] BlankMapCodes = ["' ','4101','2171'", "'1131','','2171'", "'1131','4101','  '"];

    private const string MapColumns =
        "company_id,receivable_account_code,revenue_account_code,output_tax_account_code,created_at,created_by,version";

    /// <summary>正式組成：不註冊測試映射，FinanceModule 的正式映射來源生效；SD 來源仍為假實作。</summary>
    private ServiceProvider RealMapProvider(long companyId, FakeInvoices invoices) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(invoices);
            services.AddScoped<IFinanceSalesInvoiceSource, FakeInvoiceSource>();
        });

    private static async Task<SalesInvoiceAccountMapping> SaveMapAsync(ServiceProvider provider, string receivable,
        string revenue, string tax, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var map = await db.Set<SalesInvoiceAccountMapping>().SingleOrDefaultAsync(token);
        if (map is null)
        {
            map = new SalesInvoiceAccountMapping();
            db.Add(map);
        }
        map.ReceivableAccountCode = receivable;
        map.RevenueAccountCode = revenue;
        map.OutputTaxAccountCode = tax;
        await db.SaveChangesAsync(token);
        return map;
    }

    private static async Task<(SalesInvoiceAccountMap? Map, int GlVersion)> ReadMapAsync(ServiceProvider provider,
        long companyId, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var map = await Assert.Single(sp.GetServices<ISalesInvoiceAccountMapSource>()).ReadAsync(companyId, token);
        var gl = GlPostingMappingVersionRules.Select(sp.GetServices<IGlPostingMappingVersionSource>(),
            new GlPostingSource(companyId, "SD", "Invoice", 1, 1, "fp"));
        Assert.NotNull(gl);
        return (map, await gl!.ReadCurrentVersionAsync(companyId, token));
    }

    private static async Task AssertMapSqlRejectedAsync(MyErpDbContext db, string sql, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("SAVE TRANSACTION map_case", token);
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, token));
        Assert.Contains(ex.Number, MapSqlErrors);
        await db.Database.ExecuteSqlRawAsync("ROLLBACK TRANSACTION map_case", token);
    }

    [Fact]
    public async Task SalesInvoiceMapTableIsOneRowPerCompanyWithCheckedCodesAndNoDeleteOrVersionRewind()
    {
        // FI-SALES-MAP-001（REQ_DS43.191）：結構、唯一公司鍵、檢查與觸發器。
        var token = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gl_sales_invoice_account_map') AND name = 'ux_gl_sales_invoice_account_map_company_id' AND is_unique = 1").SingleAsync(token));
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(c.name, ':', t.name, ':', c.max_length) AS [Value] FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.gl_sales_invoice_account_map') AND c.name LIKE '%account_code'").ToListAsync(token);
        Assert.Equal(["output_tax_account_code:varchar:30", "receivable_account_code:varchar:30", "revenue_account_code:varchar:30"],
            columns.Order().ToArray());

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = NewCompany();
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_sales_invoice_account_map ({MapColumns}) VALUES ({company},'1131','4101','2171','2026-11-14',1,1)", token);

        // 同公司第二列、空白科目、非正公司或版本。
        await AssertMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_invoice_account_map ({MapColumns}) VALUES ({company},'1132','4102','2172','2026-11-14',1,1)", token);
        foreach (var codes in BlankMapCodes)
            await AssertMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_invoice_account_map ({MapColumns}) VALUES ({company + 1},{codes},'2026-11-14',1,1)", token);
        await AssertMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_invoice_account_map ({MapColumns}) VALUES (0,'1131','4101','2171','2026-11-14',1,1)", token);
        await AssertMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_invoice_account_map ({MapColumns}) VALUES ({company + 2},'1131','4101','2171','2026-11-14',1,0)", token);
        // 版本回到 0 由檢查約束拒絕（約束錯誤不終止交易）。
        await AssertMapSqlRejectedAsync(db, $"UPDATE dbo.gl_sales_invoice_account_map SET revenue_account_code='4102', version=0 WHERE company_id={company}", token);

        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.gl_sales_invoice_account_map SET revenue_account_code='4102', version=2 WHERE company_id={company}", token);
        Assert.Equal("4102|2", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(revenue_account_code,'|',version) AS [Value] FROM dbo.gl_sales_invoice_account_map WHERE company_id={company}").SingleAsync(token));
        await transaction.RollbackAsync(token);

        // 映射不得刪除（刪後重建會令舊版本號對應另一組科目）；版本只能遞增；公司不可改。
        // 觸發器的 THROW 會終止整個交易，所以每個情況各自一個交易。
        await AssertMapTriggerRejectsAsync("DELETE dbo.gl_sales_invoice_account_map WHERE company_id={0}", token);
        await AssertMapTriggerRejectsAsync("UPDATE dbo.gl_sales_invoice_account_map SET revenue_account_code='4102' WHERE company_id={0}", token);
        await AssertMapTriggerRejectsAsync("UPDATE dbo.gl_sales_invoice_account_map SET company_id={0} + 3, version=2 WHERE company_id={0}", token);
    }

    private async Task AssertMapTriggerRejectsAsync(string statement, CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = NewCompany();
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_sales_invoice_account_map ({MapColumns}) VALUES ({company},'1131','4101','2171','2026-11-14',1,1)", token);
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, statement, company), token));
        Assert.Equal(51025, ex.Number);
        await transaction.RollbackAsync(token);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.gl_sales_invoice_account_map WHERE company_id={company}").SingleAsync(token));
    }

    [Fact]
    public async Task FinanceModuleRegistersOneRealSourceForBothMapInterfaces()
    {
        // FI-SALES-MAP-002：正式組成只有一個 SD/Invoice 映射來源（另有 FI/DemoSeed），兩個介面由同一型別實作。
        var companyId = NewCompany();
        await using var provider = RealMapProvider(companyId, new FakeInvoices());
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        Assert.IsType<SalesInvoiceAccountMapStore>(Assert.Single(sp.GetServices<ISalesInvoiceAccountMapSource>()));
        var gl = GlPostingMappingVersionRules.Select(sp.GetServices<IGlPostingMappingVersionSource>(),
            new GlPostingSource(companyId, "SD", "Invoice", 1, 1, "fp"));
        Assert.IsType<SalesInvoiceAccountMapStore>(gl);
    }

    [Fact]
    public async Task RealSourceReadsOnlyTheCurrentCompanyRowAndBothInterfacesReportTheSameVersion()
    {
        // FI-SALES-MAP-003：無列 → null／0（封閉失敗）；新增 → 版本 1；修改 → 版本 2；其他公司的列看不到。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var otherCompany = companyId + 1;
        await using var provider = RealMapProvider(companyId, new FakeInvoices());
        await using var other = RealMapProvider(otherCompany, new FakeInvoices());

        Assert.Equal((null, 0), await ReadMapAsync(provider, companyId, token));

        await SaveMapAsync(other, "1199", "4199", "2199", token);
        Assert.Equal((null, 0), await ReadMapAsync(provider, companyId, token));

        var created = await SaveMapAsync(provider, "1131", "4101", "2171", token);
        Assert.Equal((companyId, 1), (created.CompanyId, created.Version));
        Assert.Equal((new SalesInvoiceAccountMap(1, "1131", "4101", "2171"), 1),
            await ReadMapAsync(provider, companyId, token));

        await SaveMapAsync(provider, "1131", "4102", "2171", token);
        Assert.Equal((new SalesInvoiceAccountMap(2, "1131", "4102", "2171"), 2),
            await ReadMapAsync(provider, companyId, token));

        // 以其他公司的 ID 查詢（scope 公司不符）也看不到。
        Assert.Equal((null, 0), await ReadMapAsync(provider, otherCompany, token));
        Assert.Equal((new SalesInvoiceAccountMap(1, "1199", "4199", "2199"), 1),
            await ReadMapAsync(other, otherCompany, token));
    }

    [Fact]
    public async Task InvoicePostsWithTheRealMapAndSnapshotsItsVersionOrFailsClosedWithoutIt()
    {
        // FI-SALES-MAP-004：正式映射下過帳成功，傳票行映射版本＝映射列版本；無映射 422 且零列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[860] = Invoice(companyId, 860, 200m, 10m);
        await using var provider = RealMapProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);

        AssertSalesRejected(await PostInvoiceAsync(provider, Command(companyId, 860), true, token),
            422, SalesPostingErrors.MappingMissing);
        Assert.Equal((0, 0, 0, 0), await InvoiceCountsAsync(companyId, token));

        await SaveMapAsync(provider, "1101", "4101", "2171", token);
        await SaveMapAsync(provider, "1131", "4101", "2171", token); // 版本 2

        var posted = await PostInvoiceAsync(provider, Command(companyId, 860), true, token);
        Assert.Equal((SalesPostingOutcomeKind.Posted, true, 200), (posted.Kind, posted.Changed, posted.HttpStatus));
        Assert.Equal((1, 1, 1, 3), await InvoiceCountsAsync(companyId, token));
        await using var scope = provider.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<GlJournalLine>()
            .AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal([("1131", 2), ("4101", 2), ("2171", 2)],
            lines.Select(x => (x.AccountCode, x.MappingVersion)).ToArray());
    }

    [Fact]
    public async Task BothInterfacesReadTheLiveCommittedRowInsideTheCallerTransaction()
    {
        // FI-SALES-MAP-005：呼叫者交易內按版本 1 讀出科目後，另一連線把映射改為版本 2；GL 版本來源讀到 2（不快取），
        // 因此 GL 決策以既有規則拒絕 409 MAPPING_CHANGED（GL 測試已驗），不會以舊科目過帳。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var invoices = new FakeInvoices();
        invoices.Items[861] = Invoice(companyId, 861);
        await using var provider = RealMapProvider(companyId, invoices);
        await SeedInvoiceAccountsAsync(provider, companyId, token);
        await SaveMapAsync(provider, "1131", "4101", "2171", token);

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var map = await Assert.Single(sp.GetServices<ISalesInvoiceAccountMapSource>()).ReadAsync(companyId, token);
        Assert.Equal(1, map!.MappingVersion);
        await SaveMapAsync(provider, "1131", "4102", "2171", token);
        var gl = GlPostingMappingVersionRules.Select(sp.GetServices<IGlPostingMappingVersionSource>(),
            new GlPostingSource(companyId, "SD", "Invoice", 861, 1, "sha256:inv"))!;
        Assert.Equal(2, await gl.ReadCurrentVersionAsync(companyId, token));
        Assert.NotNull(db.Database.CurrentTransaction);
        await transaction.RollbackAsync(token);
    }
}
