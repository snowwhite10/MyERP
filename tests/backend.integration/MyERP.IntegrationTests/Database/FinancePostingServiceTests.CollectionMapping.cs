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
/// FI-COLL-MAP-001～005（WU-FI-005a2a，ADR-120）：收款與核銷的 FI 科目映射 <c>gl_sales_collection_account_map</c>。
/// 一間公司一列（未核銷款暫收、應收控制），列的 <c>version</c> 就是映射版本；<see cref="ISalesCollectionAccountMapSource"/>
/// 與 GL 的 <c>FI/SalesCollection</c>、<c>FI/CollectionWriteOff</c> 兩個 <see cref="IGlPostingMappingVersionSource"/>
/// 讀同一列，因此版本必然一致。銀行借方科目來自 MD 資金帳戶的對應科目，不在本表。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    // 547 檢查、2601/2627 唯一、2628/8152 超長截斷。
    private static readonly int[] CollectionMapSqlErrors = [547, 2601, 2627, 2628, 8152];

    private static readonly string[] BlankCollectionMapCodes = ["' ','1131'", "'2191',''", "'  ','  '"];

    private const string CollectionMapColumns =
        "company_id,unapplied_cash_account_code,receivable_account_code,created_at,created_by,version";

    private static readonly GlPostingSource CollectionReceiptGlSource = new(1, "FI", "SalesCollection", 1, 1, "fp");
    private static readonly GlPostingSource CollectionWriteOffGlSource = new(1, "FI", "CollectionWriteOff", 1, 1, "fp");

    private static async Task<SalesCollectionAccountMapping> SaveCollectionMapAsync(ServiceProvider provider,
        string unapplied, string receivable, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var map = await db.Set<SalesCollectionAccountMapping>().SingleOrDefaultAsync(token);
        if (map is null)
        {
            map = new SalesCollectionAccountMapping();
            db.Add(map);
        }
        map.UnappliedCashAccountCode = unapplied;
        map.ReceivableAccountCode = receivable;
        await db.SaveChangesAsync(token);
        return map;
    }

    private static async Task<(SalesCollectionAccountMap? Map, int ReceiptVersion, int WriteOffVersion)> ReadCollectionMapAsync(
        ServiceProvider provider, long companyId, CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var map = await Assert.Single(sp.GetServices<ISalesCollectionAccountMapSource>()).ReadAsync(companyId, token);
        var sources = sp.GetServices<IGlPostingMappingVersionSource>().ToList();
        var receipt = GlPostingMappingVersionRules.Select(sources, CollectionReceiptGlSource with { CompanyId = companyId });
        var writeOff = GlPostingMappingVersionRules.Select(sources, CollectionWriteOffGlSource with { CompanyId = companyId });
        Assert.NotNull(receipt);
        Assert.NotNull(writeOff);
        return (map, await receipt!.ReadCurrentVersionAsync(companyId, token),
            await writeOff!.ReadCurrentVersionAsync(companyId, token));
    }

    private static async Task AssertCollectionMapSqlRejectedAsync(MyErpDbContext db, string sql, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("SAVE TRANSACTION coll_map_case", token);
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, token));
        Assert.Contains(ex.Number, CollectionMapSqlErrors);
        await db.Database.ExecuteSqlRawAsync("ROLLBACK TRANSACTION coll_map_case", token);
    }

    [Fact]
    public async Task CollectionMapTableIsOneRowPerCompanyWithCheckedCodes()
    {
        // FI-COLL-MAP-001（REQ_DS43.198）：結構、唯一公司鍵、空白／超長科目、兩科目相同、非正公司或版本。
        var token = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gl_sales_collection_account_map') AND name = 'ux_gl_sales_collection_account_map_company_id' AND is_unique = 1").SingleAsync(token));
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(c.name, ':', t.name, ':', c.max_length, ':', c.is_nullable) AS [Value] FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.gl_sales_collection_account_map') AND c.name LIKE '%account_code'").ToListAsync(token);
        Assert.Equal(["receivable_account_code:varchar:30:0", "unapplied_cash_account_code:varchar:30:0"],
            columns.Order().ToArray());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.gl_sales_collection_account_map') AND name = 'tr_gl_sales_collection_account_map_guard'").SingleAsync(token));

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = NewCompany();
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company},'2191','1131','2026-11-14',1,1)", token);

        // 同公司第二列。
        await AssertCollectionMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company},'2192','1132','2026-11-14',1,1)", token);
        // 任一科目空白。
        foreach (var codes in BlankCollectionMapCodes)
            await AssertCollectionMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company + 1},{codes},'2026-11-14',1,1)", token);
        // 暫收與應收控制同一科目：核銷分錄借貸同科目，暫收餘額永不清除（大小寫不同也算同一 MD 代碼）。
        await AssertCollectionMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company + 1},'1131','1131','2026-11-14',1,1)", token);
        await AssertCollectionMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company + 1},'ar-1',' AR-1','2026-11-14',1,1)", token);
        await AssertCollectionMapSqlRejectedAsync(db, $"UPDATE dbo.gl_sales_collection_account_map SET unapplied_cash_account_code='1131', version=2 WHERE company_id={company}", token);
        // 31 字元科目代碼（MD 科目代碼上限 30）。
        await AssertCollectionMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company + 1},'{new string('9', 31)}','1131','2026-11-14',1,1)", token);
        // 非正公司或版本；版本改回 0。
        await AssertCollectionMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES (0,'2191','1131','2026-11-14',1,1)", token);
        await AssertCollectionMapSqlRejectedAsync(db, $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company + 2},'2191','1131','2026-11-14',1,0)", token);
        await AssertCollectionMapSqlRejectedAsync(db, $"UPDATE dbo.gl_sales_collection_account_map SET receivable_account_code='1132', version=0 WHERE company_id={company}", token);

        // 30 字元代碼可保存；版本遞增的修改可保存。
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company + 3},'{new string('9', 30)}','1131','2026-11-14',1,1)", token);
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.gl_sales_collection_account_map SET receivable_account_code='1132', version=2 WHERE company_id={company}", token);
        Assert.Equal("1132|2", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(receivable_account_code,'|',version) AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={company}").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }

    [Theory]
    [InlineData("DELETE dbo.gl_sales_collection_account_map WHERE company_id={0}")]
    [InlineData("UPDATE dbo.gl_sales_collection_account_map SET receivable_account_code='1132' WHERE company_id={0}")]
    [InlineData("UPDATE dbo.gl_sales_collection_account_map SET unapplied_cash_account_code='2192', version=1 WHERE company_id={0}")]
    [InlineData("UPDATE dbo.gl_sales_collection_account_map SET company_id={0} + 3, version=2 WHERE company_id={0}")]
    public async Task CollectionMapCannotBeDeletedRewoundOrMovedToAnotherCompany(string statement)
    {
        // FI-COLL-MAP-002：刪後重建或版本不遞增會令舊版本號對應另一組科目；公司不可改（SQL 51030）。
        // 觸發器的 THROW 會終止整個交易，所以每個情況各自一個交易。
        var token = TestContext.Current.CancellationToken;
        await using var db = database.CreateRawContext();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = NewCompany();
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.gl_sales_collection_account_map ({CollectionMapColumns}) VALUES ({company},'2191','1131','2026-11-14',1,1)", token);
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, statement, company), token));
        Assert.Equal(51030, ex.Number);
        await transaction.RollbackAsync(token);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={company}").SingleAsync(token));
    }

    [Fact]
    public async Task FinanceModuleRegistersOneRealCollectionSourceAndBothGlVersionSources()
    {
        // FI-COLL-MAP-003：正式組成只有一個收款映射來源；FI/SalesCollection 與 FI/CollectionWriteOff 各一個 GL 版本來源；
        // 不影響 SD/Invoice、PUR/PurchaseInvoice 的既有來源。
        var companyId = NewCompany();
        await using var provider = Provider(companyId, mapping: null);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        Assert.IsType<SalesCollectionAccountMapStore>(Assert.Single(sp.GetServices<ISalesCollectionAccountMapSource>()));
        var sources = sp.GetServices<IGlPostingMappingVersionSource>().ToList();
        Assert.IsType<SalesCollectionAccountMapStore>(GlPostingMappingVersionRules.Select(sources,
            CollectionReceiptGlSource with { CompanyId = companyId }));
        Assert.IsType<CollectionWriteOffMappingVersionSource>(GlPostingMappingVersionRules.Select(sources,
            CollectionWriteOffGlSource with { CompanyId = companyId }));
        Assert.IsType<SalesInvoiceAccountMapStore>(GlPostingMappingVersionRules.Select(sources,
            new GlPostingSource(companyId, "SD", "Invoice", 1, 1, "fp")));
        Assert.IsType<PurchaseInvoiceAccountMapStore>(GlPostingMappingVersionRules.Select(sources,
            PurchaseGlSource with { CompanyId = companyId }));
    }

    [Fact]
    public async Task RealCollectionSourceReadsOnlyTheCurrentCompanyRowAndAllInterfacesReportTheSameVersion()
    {
        // FI-COLL-MAP-004：無列 → null／0（封閉失敗）；新增 → 版本 1；修改 → 版本 2；其他公司的列、同公司 SD 與採購映射列看不到。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var otherCompany = companyId + 1;
        await using var provider = Provider(companyId, mapping: null);
        await using var other = Provider(otherCompany, mapping: null);

        Assert.Equal((null, 0, 0), await ReadCollectionMapAsync(provider, companyId, token));

        await SaveCollectionMapAsync(other, "2199", "1199", token);
        await SaveMapAsync(provider, "1131", "4101", "2171", token); // 同公司的 SD 發票映射（含應收）不是收款映射
        await SavePurchaseMapAsync(provider, "2141", "5101", "1151", token);
        Assert.Equal((null, 0, 0), await ReadCollectionMapAsync(provider, companyId, token));

        var created = await SaveCollectionMapAsync(provider, "2191", "1131", token);
        Assert.Equal((companyId, 1), (created.CompanyId, created.Version));
        Assert.Equal((new SalesCollectionAccountMap(1, "2191", "1131"), 1, 1),
            await ReadCollectionMapAsync(provider, companyId, token));

        await SaveCollectionMapAsync(provider, "2192", "1131", token);
        Assert.Equal((new SalesCollectionAccountMap(2, "2192", "1131"), 2, 2),
            await ReadCollectionMapAsync(provider, companyId, token));

        // 以其他公司的 ID 查詢（scope 公司不符）、非正公司 ID 也看不到。
        Assert.Equal((null, 0, 0), await ReadCollectionMapAsync(provider, otherCompany, token));
        Assert.Equal((null, 0, 0), await ReadCollectionMapAsync(provider, 0, token));
        Assert.Equal((new SalesCollectionAccountMap(1, "2199", "1199"), 1, 1),
            await ReadCollectionMapAsync(other, otherCompany, token));
    }

    [Fact]
    public async Task CollectionInterfacesReadTheLiveCommittedRowInsideTheCallerTransaction()
    {
        // FI-COLL-MAP-005：呼叫者交易內按版本 1 讀出科目後，另一連線把映射改為版本 2；兩個 GL 版本來源都讀到 2（不快取），
        // 因此 GL 決策以既有規則拒絕 409 MAPPING_CHANGED，不會以舊科目過帳；讀取加入呼叫者交易而不結束它。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = Provider(companyId, mapping: null);
        await SaveCollectionMapAsync(provider, "2191", "1131", token);

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var map = await Assert.Single(sp.GetServices<ISalesCollectionAccountMapSource>()).ReadAsync(companyId, token);
        Assert.Equal(1, map!.MappingVersion);
        await SaveCollectionMapAsync(provider, "2192", "1131", token);
        var sources = sp.GetServices<IGlPostingMappingVersionSource>().ToList();
        Assert.Equal(2, await GlPostingMappingVersionRules.Select(sources, CollectionReceiptGlSource with { CompanyId = companyId })!
            .ReadCurrentVersionAsync(companyId, token));
        Assert.Equal(2, await GlPostingMappingVersionRules.Select(sources, CollectionWriteOffGlSource with { CompanyId = companyId })!
            .ReadCurrentVersionAsync(companyId, token));
        Assert.NotNull(db.Database.CurrentTransaction);
        await transaction.RollbackAsync(token);
    }
}
