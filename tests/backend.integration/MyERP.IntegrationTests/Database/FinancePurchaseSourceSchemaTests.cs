using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// 本檔的原始 SQL 片段全由測試常數與 Random 產生的數值組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-FI-003a1：PUR 採購發票應付來源與已過帳應付的真實 SQL 結構契約（BR-FI-016、017、019、022；FI-PUR-004、006–008、014、016、023）。
/// 只驗證表、鍵、檢查與觸發器；過帳、鎖、冪等與回滾由 WU-FI-003a2／003a3 驗收。
/// </summary>
public sealed class FinancePurchaseSourceSchemaTests(TestDatabase database)
{
    private static readonly int[] ConstraintErrors = [547, 2601, 2627, 51026];

    private const string SourceColumns =
        "company_id,supplier_id,source_type,source_id,source_version,fingerprint,gl_journal_id,business_date,currency,base_currency,exchange_rate,net_amount,tax_amount,gross_amount,base_net_amount,base_tax_amount,base_gross_amount,created_at,created_by,version";

    private const string PayableColumns =
        "company_id,supplier_id,purchase_source_id,posting_date,currency,base_currency,original_amount,open_amount,original_base_amount,open_base_amount,created_at,created_by,version";

    [Fact]
    public async Task PurchaseSourceTablesHavePermanentKeysForeignKeysAndLookupIndexes()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        foreach (var table in new[] { "ap_purchase_source", "ap_posted_payable" })
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE object_id = OBJECT_ID({"dbo." + table})").SingleAsync(token));

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN (OBJECT_ID('dbo.ap_purchase_source'), OBJECT_ID('dbo.ap_posted_payable'))").ToListAsync(token);
        Assert.Contains("ux_ap_purchase_source_company_id_source_type_source_id", indexes);
        Assert.Contains("ux_ap_purchase_source_gl_journal_id", indexes);
        Assert.Contains("ix_ap_purchase_source_company_id_supplier_id_business_date", indexes);
        Assert.Contains("ux_ap_posted_payable_company_id_purchase_source_id", indexes);
        Assert.Contains("ix_ap_posted_payable_company_id_supplier_id_posting_date_id", indexes);

        var fks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('dbo.ap_purchase_source'), OBJECT_ID('dbo.ap_posted_payable'))").ToListAsync(token);
        Assert.Contains("fk_ap_purchase_source_gl_journal_company_id_gl_journal_id", fks);
        Assert.Contains("fk_ap_posted_payable_ap_purchase_source_company_id_purchase_source_id", fks);

        var triggers = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.triggers WHERE parent_id IN (OBJECT_ID('dbo.ap_purchase_source'), OBJECT_ID('dbo.ap_posted_payable'))").ToListAsync(token);
        Assert.Contains("tr_ap_purchase_source_immutable", triggers);
        Assert.Contains("tr_ap_posted_payable_origin", triggers);

        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(OBJECT_NAME(c.object_id), '.', c.name, ':', t.name, ':', c.precision, ':', c.scale) AS [Value] FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id IN (OBJECT_ID('dbo.ap_purchase_source'), OBJECT_ID('dbo.ap_posted_payable')) AND c.name IN ('gross_amount','base_gross_amount','exchange_rate','open_amount','open_base_amount')").ToListAsync(token);
        Assert.Contains("ap_purchase_source.gross_amount:decimal:19:4", columns);
        Assert.Contains("ap_purchase_source.base_gross_amount:decimal:19:4", columns);
        Assert.Contains("ap_purchase_source.exchange_rate:decimal:19:8", columns);
        Assert.Contains("ap_posted_payable.open_amount:decimal:19:4", columns);
        Assert.Contains("ap_posted_payable.open_base_amount:decimal:19:4", columns);
        // 無軟刪除欄位：來源鍵永久佔用，PUR 刪除或軟刪除歷史都不能釋放（FI-PUR-008、023）。
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id IN (OBJECT_ID('dbo.ap_purchase_source'), OBJECT_ID('dbo.ap_posted_payable')) AND name = 'is_deleted'").SingleAsync(token));
    }

    [Fact]
    public async Task SourceKeyIsPermanentAndOneSourcePerJournal()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(83000000, 84000000), token);
        var source = await InsertSourceAsync(db, fixture, fixture.JournalA, fixture.SourceId, 100m, 5m, token);
        Assert.True(source > 0);

        // 同公司同類型同來源 ID（即使版本或指紋不同）不得再保存（BR-FI-016、FI-PUR-008）。
        await AssertRejectedAsync(db,
            $"INSERT dbo.ap_purchase_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Supplier},'PurchaseInvoice',{fixture.SourceId},2,'changed',{fixture.JournalB},'2026-03-31','USD','TWD',32.5,100,5,105,3250,162.5,3412.5,'2026-01-01',1,1)", token);
        // 一張傳票只能屬於一個採購來源。
        await AssertRejectedAsync(db,
            $"INSERT dbo.ap_purchase_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Supplier},'PurchaseInvoice',{fixture.SourceId + 1},1,'other',{fixture.JournalA},'2026-03-31','USD','TWD',32.5,100,5,105,3250,162.5,3412.5,'2026-01-01',1,1)", token);
        Assert.Equal("100.0000|5.0000|105.0000|3250.0000|162.5000|3412.5000|32.50000000", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(net_amount,'|',tax_amount,'|',gross_amount,'|',base_net_amount,'|',base_tax_amount,'|',base_gross_amount,'|',exchange_rate) AS [Value] FROM dbo.ap_purchase_source WHERE id={source}").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }

    [Theory]
    [InlineData("'PurchaseReturn'", "100,5,105,3250,162.5,3412.5", "32.5", "'USD','TWD'")]     // 類型只接受 PurchaseInvoice（銷退須另行規範，BR-FI-022）
    [InlineData("'PurchaseInvoice'", "100,5,106,3250,162.5,3412.5", "32.5", "'USD','TWD'")]   // 原幣 net+tax≠gross
    [InlineData("'PurchaseInvoice'", "100,5,105,3250,162.5,3412.6", "32.5", "'USD','TWD'")]   // 本位幣 net+tax≠gross
    [InlineData("'PurchaseInvoice'", "0,0,0,0,0,0", "32.5", "'USD','TWD'")]                    // 零金額
    [InlineData("'PurchaseInvoice'", "110,-5,105,3575,-162.5,3412.5", "32.5", "'USD','TWD'")] // 負稅額
    [InlineData("'PurchaseInvoice'", "100,5,105,3250,162.5,3412.5", "0", "'USD','TWD'")]       // 匯率非正
    [InlineData("'PurchaseInvoice'", "100,5,105,3250,162.5,3412.5", "32.5", "'usd','TWD'")]    // 幣別小寫
    [InlineData("'PurchaseInvoice'", "100,5,105,3250,162.5,3412.5", "32.5", "'USD','TW'")]     // 本位幣格式
    public async Task SqlRejectsInvalidSourceRows(string type, string amounts, string rate, string currencies)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(84000000, 85000000), token);
        await AssertRejectedAsync(db,
            $"INSERT dbo.ap_purchase_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Supplier},{type},{fixture.SourceId},1,'fp',{fixture.JournalA},'2026-03-31',{currencies},{rate},{amounts},'2026-01-01',1,1)", token);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task SqlRejectsBlankIdentityAndForeignCompanyJournal()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(85000000, 86000000), token);
        var other = await SeedAsync(db, fixture.Company + 1, token);
        const string amounts = "'USD','TWD',32.5,100,5,105,3250,162.5,3412.5,'2026-01-01',1,1";
        // 空白指紋、零版本、零來源 ID、零供應商。
        foreach (var (supplier, version, fingerprint, sourceId) in new[]
                 {
                     (fixture.Supplier, 1, "'  '", fixture.SourceId), (fixture.Supplier, 0, "'fp'", fixture.SourceId),
                     (fixture.Supplier, 1, "'fp'", 0L), (0L, 1, "'fp'", fixture.SourceId),
                 })
            await AssertRejectedAsync(db,
                $"INSERT dbo.ap_purchase_source ({SourceColumns}) VALUES ({fixture.Company},{supplier},'PurchaseInvoice',{sourceId},{version},{fingerprint},{fixture.JournalA},'2026-03-31',{amounts})", token);
        // 不得連到其他公司的傳票（FI-PUR-014）。
        await AssertRejectedAsync(db,
            $"INSERT dbo.ap_purchase_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Supplier},'PurchaseInvoice',{fixture.SourceId},1,'fp',{other.JournalA},'2026-03-31',{amounts})", token);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task PayableMustMatchItsSourceOriginAndStayWithinOriginal()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(86000000, 87000000), token);
        var other = await SeedAsync(db, fixture.Company + 1, token);
        var source = await InsertSourceAsync(db, fixture, fixture.JournalA, fixture.SourceId, 100m, 5m, token);
        var second = await InsertSourceAsync(db, fixture, fixture.JournalB, fixture.SourceId + 1, 40m, 0m, token);
        var foreignSource = await InsertSourceAsync(db, other, other.JournalA, other.SourceId, 100m, 5m, token);
        var s = fixture.Supplier;

        // 每個來源最多一筆應付；原始與未付金額等於來源總額（BR-FI-019）。
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.ap_posted_payable ({PayableColumns}) VALUES ({fixture.Company},{s},{source},'2026-03-31','USD','TWD',105,105,3412.5,3412.5,'2026-01-01',1,1)", token);
        Assert.Equal("105.0000|105.0000|3412.5000|3412.5000", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(original_amount,'|',open_amount,'|',original_base_amount,'|',open_base_amount) AS [Value] FROM dbo.ap_posted_payable WHERE purchase_source_id={source}").SingleAsync(token));
        await AssertRejectedAsync(db,
            $"INSERT dbo.ap_posted_payable ({PayableColumns}) VALUES ({fixture.Company},{s},{source},'2026-03-31','USD','TWD',105,105,3412.5,3412.5,'2026-01-01',1,1)", token);
        // 跨公司來源。
        await AssertRejectedAsync(db,
            $"INSERT dbo.ap_posted_payable ({PayableColumns}) VALUES ({fixture.Company},{s},{foreignSource},'2026-03-31','USD','TWD',105,105,3412.5,3412.5,'2026-01-01',1,1)", token);
        // 第二個來源的應付保存後未付可為零（已全數核銷），原幣與本位幣同時為零。
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.ap_posted_payable ({PayableColumns}) VALUES ({fixture.Company},{s},{second},'2026-03-31','USD','TWD',40,0,1300,0,'2026-01-01',1,1)", token);
        // 與來源不一致的來源、檢查與觸發器情境見下列 Theory（觸發器 THROW 會中止交易，各用獨立交易）。
        await transaction.RollbackAsync(token);
    }

    [Theory]
    [InlineData("41,41,1300,1300", "'USD'", "'2026-03-31'", 0, 51026)]   // 原幣總額不符
    [InlineData("40,40,1301,1301", "'USD'", "'2026-03-31'", 0, 51026)]   // 本位幣總額不符
    [InlineData("40,40,1300,1300", "'EUR'", "'2026-03-31'", 0, 51026)]   // 幣別不符
    [InlineData("40,40,1300,1300", "'USD'", "'2026-03-30'", 0, 51026)]   // 日期不符
    [InlineData("40,40,1300,1300", "'USD'", "'2026-03-31'", 1, 51026)]   // 供應商不符
    [InlineData("40,40,1300,1300", "'usd'", "'2026-03-31'", 0, 547)]     // 幣別小寫（檢查約束先於觸發器）
    public async Task PayableOriginMismatchWithSourceIsRejected(string amounts, string currency, string date, long supplierOffset, int expected)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(87000000, 88000000), token);
        var source = await InsertSourceAsync(db, fixture, fixture.JournalA, fixture.SourceId, 40m, 0m, token);
        var error = await AssertRejectedAsync(db,
            $"INSERT dbo.ap_posted_payable ({PayableColumns}) VALUES ({fixture.Company},{fixture.Supplier + supplierOffset},{source},{date},{currency},'TWD',{amounts},'2026-01-01',1,1)", token);
        Assert.Equal(expected, error.Number);
        await transaction.RollbackAsync(token);
    }

    [Theory]
    [InlineData("40,41,1300,1300")]  // 未付大於原始
    [InlineData("40,-1,1300,1300")]  // 未付為負
    [InlineData("40,0,1300,1300")]   // 原幣已清、本位幣未清
    [InlineData("40,40,1300,0")]     // 本位幣已清、原幣未清
    public async Task PayableOpenAmountsAreBoundedAndConsistent(string amounts)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(88000000, 89000000), token);
        var source = await InsertSourceAsync(db, fixture, fixture.JournalA, fixture.SourceId, 40m, 0m, token);
        var error = await AssertRejectedAsync(db,
            $"INSERT dbo.ap_posted_payable ({PayableColumns}) VALUES ({fixture.Company},{fixture.Supplier},{source},'2026-03-31','USD','TWD',{amounts},'2026-01-01',1,1)", token);
        Assert.Equal(547, error.Number);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task SavedSourceIsImmutableAndPayableOnlyChangesOpenAmounts()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;

        // THROW 會中止交易，所以每個被拒的修改各用一個獨立交易。
        await using (var first = await db.Database.BeginTransactionAsync(token))
        {
            var fixture = await SeedAsync(db, Random.Shared.NextInt64(89000000, 89200000), token);
            var source = await InsertSourceAsync(db, fixture, fixture.JournalA, fixture.SourceId, 100m, 5m, token);
            Assert.Equal(51026, (await AssertRejectedAsync(db, $"UPDATE dbo.ap_purchase_source SET fingerprint='forged' WHERE id={source}", token)).Number);
            await first.RollbackAsync(token);
        }

        await using (var second = await db.Database.BeginTransactionAsync(token))
        {
            var fixture = await SeedAsync(db, Random.Shared.NextInt64(89200000, 89400000), token);
            var source = await InsertSourceAsync(db, fixture, fixture.JournalA, fixture.SourceId, 100m, 5m, token);
            Assert.Equal(51026, (await AssertRejectedAsync(db, $"DELETE dbo.ap_purchase_source WHERE id={source}", token)).Number);
            await second.RollbackAsync(token);
        }

        await using (var third = await db.Database.BeginTransactionAsync(token))
        {
            var (_, payable) = await SeedPayableAsync(db, Random.Shared.NextInt64(89400000, 89600000), token);
            // 之後的付款核銷只減少未付金額並遞增版本（BR-FI-022）：允許。
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE dbo.ap_posted_payable SET open_amount=5, open_base_amount=162.5, version=version+1, updated_at='2026-04-01', updated_by=1 WHERE id={payable}", token);
            Assert.Equal("5.0000|162.5000|2", await db.Database.SqlQueryRaw<string>(
                $"SELECT CONCAT(open_amount,'|',open_base_amount,'|',version) AS [Value] FROM dbo.ap_posted_payable WHERE id={payable}").SingleAsync(token));
            await third.RollbackAsync(token);
        }

        foreach (var forged in new[]
                 {
                     "original_amount=106", "original_base_amount=3445", "supplier_id=supplier_id+1", "currency='EUR'",
                     "base_currency='USD'", "posting_date='2026-03-30'",
                 })
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var (_, payable) = await SeedPayableAsync(db, Random.Shared.NextInt64(89600000, 89800000), token);
            var error = await AssertRejectedAsync(db, $"UPDATE dbo.ap_posted_payable SET {forged}, open_amount=open_amount WHERE id={payable}", token);
            Assert.Equal(51026, error.Number);
            await tx.RollbackAsync(token);
        }

        await using (var last = await db.Database.BeginTransactionAsync(token))
        {
            var (_, payable) = await SeedPayableAsync(db, Random.Shared.NextInt64(89800000, 90000000), token);
            Assert.Equal(51026, (await AssertRejectedAsync(db, $"DELETE dbo.ap_posted_payable WHERE id={payable}", token)).Number);
            await last.RollbackAsync(token);
        }
    }

    /// <summary>只接受約束拒絕（547 檢查／外鍵、2601／2627 唯一、51026 觸發器）；表不存在（208）等錯誤不算通過。</summary>
    private static async Task<SqlException> AssertRejectedAsync(DbContext db, string sql, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, token));
        Assert.Contains(error.Number, ConstraintErrors);
        return error;
    }

    private sealed record Fixture(long Company, long Supplier, long SourceId, long JournalA, long JournalB);

    /// <summary>建立同一公司的期間與兩張已過帳傳票（各有 PUR 來源鍵），供來源列引用。</summary>
    private static async Task<Fixture> SeedAsync(DbContext db, long company, CancellationToken token)
    {
        var supplier = Random.Shared.NextInt64(10000000, 90000000);
        var sourceId = Random.Shared.NextInt64(10000000, 90000000);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_period (company_id,start_date,end_date,status,created_at,created_by,version) VALUES ({company},'2026-03-01','2026-03-31','Open','2026-01-01',1,1)", token);
        var period = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={company}").SingleAsync(token);
        var journals = new List<long>();
        foreach (var id in new[] { sourceId, sourceId + 1 })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({company},'PUR','PurchaseInvoice',{id},1,'fp','2026-01-01',1,1)", token);
            var key = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={company} AND source_module='PUR' AND source_id={id}").SingleAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({company},{period},{key},'2026-03-31','TWD','Posted',10,10,'2026-01-01',1,1)", token);
            journals.Add(await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_journal WHERE company_id={company} AND source_key_id={key}").SingleAsync(token));
        }

        return new Fixture(company, supplier, sourceId, journals[0], journals[1]);
    }

    private static async Task<long> InsertSourceAsync(DbContext db, Fixture fixture, long journal, long sourceId, decimal net, decimal tax, CancellationToken token)
    {
        var gross = net + tax;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT dbo.ap_purchase_source (company_id,supplier_id,source_type,source_id,source_version,fingerprint,gl_journal_id,business_date,currency,base_currency,exchange_rate,net_amount,tax_amount,gross_amount,base_net_amount,base_tax_amount,base_gross_amount,created_at,created_by,version) VALUES ({fixture.Company},{fixture.Supplier},'PurchaseInvoice',{sourceId},1,'fp',{journal},'2026-03-31','USD','TWD',32.5,{net},{tax},{gross},{net * 32.5m},{tax * 32.5m},{gross * 32.5m},'2026-01-01',1,1)", token);
        return await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.ap_purchase_source WHERE company_id={fixture.Company} AND source_type='PurchaseInvoice' AND source_id={sourceId}").SingleAsync(token);
    }

    private static async Task<(Fixture Fixture, long Payable)> SeedPayableAsync(DbContext db, long company, CancellationToken token)
    {
        var fixture = await SeedAsync(db, company, token);
        var source = await InsertSourceAsync(db, fixture, fixture.JournalA, fixture.SourceId, 100m, 5m, token);
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.ap_posted_payable ({PayableColumns}) VALUES ({fixture.Company},{fixture.Supplier},{source},'2026-03-31','USD','TWD',105,105,3412.5,3412.5,'2026-01-01',1,1)", token);
        return (fixture, await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.ap_posted_payable WHERE purchase_source_id={source}").SingleAsync(token));
    }
}
