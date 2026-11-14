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
/// FI-PUR-MAP-001～005（WU-FI-003a2a，ADR-116）：PUR 採購發票科目映射的正式來源 <c>gl_purchase_invoice_account_map</c>。
/// 一間公司一列，列的 <c>version</c> 就是映射版本；<see cref="IPurchaseInvoiceAccountMapSource"/> 與 GL 的
/// <c>PUR/PurchaseInvoice</c> <see cref="IGlPostingMappingVersionSource"/> 由同一實作讀同一列，因此版本必然一致。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    // 547 檢查、2601/2627 唯一、2628/8152 超長截斷。
    private static readonly int[] PurchaseMapSqlErrors = [547, 2601, 2627, 2628, 8152];

    private static readonly string[] BlankPurchaseMapCodes =
        ["' ','5101','1151'", "'2141','','1151'", "'2141','5101','  '"];

    private const string PurchaseMapColumns =
        "company_id,payable_account_code,purchase_account_code,input_tax_account_code,created_at,created_by,version";

    private static readonly GlPostingSource PurchaseGlSource = new(1, "PUR", "PurchaseInvoice", 1, 1, "fp");

    private static async Task<PurchaseInvoiceAccountMapping> SavePurchaseMapAsync(ServiceProvider provider,
        string payable, string purchase, string tax, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var map = await db.Set<PurchaseInvoiceAccountMapping>().SingleOrDefaultAsync(token);
        if (map is null)
        {
            map = new PurchaseInvoiceAccountMapping();
            db.Add(map);
        }
        map.PayableAccountCode = payable;
        map.PurchaseAccountCode = purchase;
        map.InputTaxAccountCode = tax;
        await db.SaveChangesAsync(token);
        return map;
    }

    private static async Task<(PurchaseInvoiceAccountMap? Map, int GlVersion)> ReadPurchaseMapAsync(
        ServiceProvider provider, long companyId, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var map = await Assert.Single(sp.GetServices<IPurchaseInvoiceAccountMapSource>()).ReadAsync(companyId, token);
        var gl = GlPostingMappingVersionRules.Select(sp.GetServices<IGlPostingMappingVersionSource>(),
            PurchaseGlSource with { CompanyId = companyId });
        Assert.NotNull(gl);
        return (map, await gl!.ReadCurrentVersionAsync(companyId, token));
    }

    private static async Task AssertPurchaseMapSqlRejectedAsync(MyErpDbContext db, string sql, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("SAVE TRANSACTION pur_map_case", token);
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, token));
        Assert.Contains(ex.Number, PurchaseMapSqlErrors);
        await db.Database.ExecuteSqlRawAsync("ROLLBACK TRANSACTION pur_map_case", token);
    }

    [Fact]
    public async Task PurchaseInvoiceMapTableIsOneRowPerCompanyWithCheckedCodes()
    {
        // FI-PUR-MAP-001（REQ_DS43.194）：結構、唯一公司鍵、空白／超長科目、非正公司或版本。
        var token = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gl_purchase_invoice_account_map') AND name = 'ux_gl_purchase_invoice_account_map_company_id' AND is_unique = 1").SingleAsync(token));
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(c.name, ':', t.name, ':', c.max_length, ':', c.is_nullable) AS [Value] FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.gl_purchase_invoice_account_map') AND c.name LIKE '%account_code'").ToListAsync(token);
        Assert.Equal(["input_tax_account_code:varchar:30:0", "payable_account_code:varchar:30:0", "purchase_account_code:varchar:30:0"],
            columns.Order().ToArray());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.gl_purchase_invoice_account_map') AND name = 'tr_gl_purchase_invoice_account_map_guard'").SingleAsync(token));

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = NewCompany();
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES ({company},'2141','5101','1151','2026-11-14',1,1)", token);

        // 同公司第二列。
        await AssertPurchaseMapSqlRejectedAsync(db, $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES ({company},'2142','5102','1152','2026-11-14',1,1)", token);
        // 任一科目空白。
        foreach (var codes in BlankPurchaseMapCodes)
            await AssertPurchaseMapSqlRejectedAsync(db, $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES ({company + 1},{codes},'2026-11-14',1,1)", token);
        // 31 字元科目代碼（MD 科目代碼上限 30）。
        await AssertPurchaseMapSqlRejectedAsync(db, $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES ({company + 1},'{new string('9', 31)}','5101','1151','2026-11-14',1,1)", token);
        // 非正公司或版本；版本改回 0。
        await AssertPurchaseMapSqlRejectedAsync(db, $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES (0,'2141','5101','1151','2026-11-14',1,1)", token);
        await AssertPurchaseMapSqlRejectedAsync(db, $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES ({company + 2},'2141','5101','1151','2026-11-14',1,0)", token);
        await AssertPurchaseMapSqlRejectedAsync(db, $"UPDATE dbo.gl_purchase_invoice_account_map SET purchase_account_code='5102', version=0 WHERE company_id={company}", token);

        // 30 字元代碼可保存；版本遞增的修改可保存。
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES ({company + 3},'{new string('9', 30)}','5101','1151','2026-11-14',1,1)", token);
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.gl_purchase_invoice_account_map SET purchase_account_code='5102', version=2 WHERE company_id={company}", token);
        Assert.Equal("5102|2", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(purchase_account_code,'|',version) AS [Value] FROM dbo.gl_purchase_invoice_account_map WHERE company_id={company}").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }

    [Theory]
    [InlineData("DELETE dbo.gl_purchase_invoice_account_map WHERE company_id={0}")]
    [InlineData("UPDATE dbo.gl_purchase_invoice_account_map SET purchase_account_code='5102' WHERE company_id={0}")]
    [InlineData("UPDATE dbo.gl_purchase_invoice_account_map SET input_tax_account_code='1152', version=1 WHERE company_id={0}")]
    [InlineData("UPDATE dbo.gl_purchase_invoice_account_map SET company_id={0} + 3, version=2 WHERE company_id={0}")]
    public async Task PurchaseInvoiceMapCannotBeDeletedRewoundOrMovedToAnotherCompany(string statement)
    {
        // FI-PUR-MAP-002：刪後重建或版本不遞增會令舊版本號對應另一組科目；公司不可改（SQL 51027）。
        // 觸發器的 THROW 會終止整個交易，所以每個情況各自一個交易。
        var token = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = NewCompany();
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_purchase_invoice_account_map ({PurchaseMapColumns}) VALUES ({company},'2141','5101','1151','2026-11-14',1,1)", token);
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, statement, company), token));
        Assert.Equal(51027, ex.Number);
        await transaction.RollbackAsync(token);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.gl_purchase_invoice_account_map WHERE company_id={company}").SingleAsync(token));
    }

    [Fact]
    public async Task FinanceModuleRegistersOneRealPurchaseSourceForBothMapInterfaces()
    {
        // FI-PUR-MAP-003：正式組成只有一個 PUR/PurchaseInvoice 映射來源，兩個介面由同一型別實作；
        // 不影響 SD/Invoice 的既有來源。
        var companyId = NewCompany();
        await using var provider = Provider(companyId, mapping: null);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        Assert.IsType<PurchaseInvoiceAccountMapStore>(Assert.Single(sp.GetServices<IPurchaseInvoiceAccountMapSource>()));
        var sources = sp.GetServices<IGlPostingMappingVersionSource>().ToList();
        Assert.IsType<PurchaseInvoiceAccountMapStore>(GlPostingMappingVersionRules.Select(sources,
            PurchaseGlSource with { CompanyId = companyId }));
        Assert.IsType<SalesInvoiceAccountMapStore>(GlPostingMappingVersionRules.Select(sources,
            new GlPostingSource(companyId, "SD", "Invoice", 1, 1, "fp")));
    }

    [Fact]
    public async Task RealPurchaseSourceReadsOnlyTheCurrentCompanyRowAndBothInterfacesReportTheSameVersion()
    {
        // FI-PUR-MAP-004：無列 → null／0（封閉失敗）；新增 → 版本 1；修改 → 版本 2；其他公司的列與 SD 映射列看不到。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var otherCompany = companyId + 1;
        await using var provider = Provider(companyId, mapping: null);
        await using var other = Provider(otherCompany, mapping: null);

        Assert.Equal((null, 0), await ReadPurchaseMapAsync(provider, companyId, token));

        await SavePurchaseMapAsync(other, "2199", "5199", "1199", token);
        await SaveMapAsync(provider, "1131", "4101", "2171", token); // 同公司的 SD 映射不是採購映射
        Assert.Equal((null, 0), await ReadPurchaseMapAsync(provider, companyId, token));

        var created = await SavePurchaseMapAsync(provider, "2141", "5101", "1151", token);
        Assert.Equal((companyId, 1), (created.CompanyId, created.Version));
        Assert.Equal((new PurchaseInvoiceAccountMap(1, "2141", "5101", "1151"), 1),
            await ReadPurchaseMapAsync(provider, companyId, token));

        await SavePurchaseMapAsync(provider, "2141", "5102", "1151", token);
        Assert.Equal((new PurchaseInvoiceAccountMap(2, "2141", "5102", "1151"), 2),
            await ReadPurchaseMapAsync(provider, companyId, token));

        // 以其他公司的 ID 查詢（scope 公司不符）、非正公司 ID 也看不到。
        Assert.Equal((null, 0), await ReadPurchaseMapAsync(provider, otherCompany, token));
        Assert.Equal((null, 0), await ReadPurchaseMapAsync(provider, 0, token));
        Assert.Equal((new PurchaseInvoiceAccountMap(1, "2199", "5199", "1199"), 1),
            await ReadPurchaseMapAsync(other, otherCompany, token));
    }

    [Fact]
    public async Task BothPurchaseInterfacesReadTheLiveCommittedRowInsideTheCallerTransaction()
    {
        // FI-PUR-MAP-005：呼叫者交易內按版本 1 讀出科目後，另一連線把映射改為版本 2；GL 版本來源讀到 2（不快取），
        // 因此 GL 決策以既有規則拒絕 409 MAPPING_CHANGED，不會以舊科目過帳；讀取加入呼叫者交易而不結束它。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, mapping: null);
        await SavePurchaseMapAsync(provider, "2141", "5101", "1151", token);

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var map = await Assert.Single(sp.GetServices<IPurchaseInvoiceAccountMapSource>()).ReadAsync(companyId, token);
        Assert.Equal(1, map!.MappingVersion);
        await SavePurchaseMapAsync(provider, "2141", "5102", "1151", token);
        var gl = GlPostingMappingVersionRules.Select(sp.GetServices<IGlPostingMappingVersionSource>(),
            PurchaseGlSource with { CompanyId = companyId })!;
        Assert.Equal(2, await gl.ReadCurrentVersionAsync(companyId, token));
        Assert.NotNull(db.Database.CurrentTransaction);
        await transaction.RollbackAsync(token);
    }
}
