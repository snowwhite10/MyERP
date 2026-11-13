using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// 本檔的原始 SQL 片段全由測試常數與 Random 產生的數值組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-FI-002a1：銷售應收來源與信用分配的真實 SQL 結構契約（BR-FI-008、009、011、014；FI-SALES-006、009–011、017–018）。
/// 只驗證表、鍵、檢查與不可變觸發器；過帳、鎖與冪等交易由 WU-FI-002a2／002a3 驗收。
/// </summary>
public sealed class FinanceSalesSourceSchemaTests(TestDatabase database)
{
    private static readonly int[] ConstraintErrors = [547, 2601, 2627, 51024];

    private const string SourceColumns =
        "company_id,customer_id,source_type,source_id,source_version,fingerprint,gl_journal_id,business_date,currency,base_currency,exchange_rate,net_amount,tax_amount,gross_amount,base_net_amount,base_tax_amount,base_gross_amount,created_at,created_by,version";

    private const string AllocationColumns =
        "company_id,credit_source_id,receivable_id,invoice_line_id,quantity,net_amount,tax_amount,gross_amount,base_net_amount,base_tax_amount,base_gross_amount,created_at,created_by,version";

    [Fact]
    public async Task SalesSourceTablesHavePermanentKeysForeignKeysAndLookupIndexes()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        foreach (var table in new[] { "ar_sales_source", "ar_credit_allocation" })
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE object_id = OBJECT_ID({"dbo." + table})").SingleAsync(token));

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN (OBJECT_ID('dbo.ar_sales_source'), OBJECT_ID('dbo.ar_credit_allocation'))").ToListAsync(token);
        Assert.Contains("ux_ar_sales_source_company_id_source_type_source_id", indexes);
        Assert.Contains("ix_ar_sales_source_company_id_customer_id_business_date", indexes);
        Assert.Contains("ux_ar_sales_source_gl_journal_id", indexes);
        Assert.Contains("ux_ar_credit_allocation_credit_source_id_invoice_line_id", indexes);
        Assert.Contains("ix_ar_credit_allocation_company_id_receivable_id", indexes);
        Assert.Contains("ix_ar_credit_allocation_company_id_invoice_line_id", indexes);

        var fks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('dbo.ar_sales_source'), OBJECT_ID('dbo.ar_credit_allocation'))").ToListAsync(token);
        Assert.Contains("fk_ar_sales_source_ar_credit_key_company_id_customer_id", fks);
        Assert.Contains("fk_ar_sales_source_gl_journal_company_id_gl_journal_id", fks);
        Assert.Contains("fk_ar_credit_allocation_ar_sales_source_company_id_credit_source_id", fks);
        Assert.Contains("fk_ar_credit_allocation_ar_posted_receivable_company_id_receivable_id", fks);

        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(OBJECT_NAME(c.object_id), '.', c.name, ':', t.name, ':', c.precision, ':', c.scale) AS [Value] FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id IN (OBJECT_ID('dbo.ar_sales_source'), OBJECT_ID('dbo.ar_credit_allocation')) AND c.name IN ('gross_amount','base_gross_amount','exchange_rate','quantity')").ToListAsync(token);
        Assert.Contains("ar_sales_source.gross_amount:decimal:19:4", columns);
        Assert.Contains("ar_sales_source.base_gross_amount:decimal:19:4", columns);
        Assert.Contains("ar_sales_source.exchange_rate:decimal:19:8", columns);
        Assert.Contains("ar_credit_allocation.quantity:decimal:19:6", columns);
        Assert.Contains("ar_credit_allocation.base_gross_amount:decimal:19:4", columns);
        // 來源無軟刪除欄位：唯一鍵永久佔用，歷史不能釋放來源鍵（FI-SALES-010）。
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id IN (OBJECT_ID('dbo.ar_sales_source'), OBJECT_ID('dbo.ar_credit_allocation')) AND name = 'is_deleted'").SingleAsync(token));
    }

    [Fact]
    public async Task SourceKeyIsPermanentAndComponentTotalsMustReconcile()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(93000000, 94000000), token);
        var invoice = await InsertSourceAsync(db, fixture, "Invoice", fixture.JournalA, 100m, 5m, token);
        Assert.True(invoice > 0);

        // 同公司同類型同來源 ID（即使版本或指紋不同）不得再保存。
        await AssertRejectedAsync(db, 
            $"INSERT dbo.ar_sales_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Customer},'Invoice',{fixture.SourceId},2,'changed',{fixture.JournalB},'2026-03-31','USD','TWD',32.5,100,5,105,3250,162.5,3412.5,'2026-01-01',1,1)", token);
        // 一張傳票只能屬於一個銷售來源。
        await AssertRejectedAsync(db, 
            $"INSERT dbo.ar_sales_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Customer},'Return',{fixture.SourceId + 1},1,'other',{fixture.JournalA},'2026-03-31','USD','TWD',32.5,100,5,105,3250,162.5,3412.5,'2026-01-01',1,1)", token);
        Assert.Equal("100.0000|5.0000|105.0000|3250.0000|162.5000|3412.5000|32.50000000", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(net_amount,'|',tax_amount,'|',gross_amount,'|',base_net_amount,'|',base_tax_amount,'|',base_gross_amount,'|',exchange_rate) AS [Value] FROM dbo.ar_sales_source WHERE id={invoice}").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }

    [Theory]
    [InlineData("'Order'", "100,5,105,3250,162.5,3412.5", "32.5", "'USD','TWD'")]       // 類型不在 Invoice／Return
    [InlineData("'Invoice'", "100,5,106,3250,162.5,3412.5", "32.5", "'USD','TWD'")]     // 原幣 net+tax≠gross
    [InlineData("'Invoice'", "100,5,105,3250,162.5,3412.6", "32.5", "'USD','TWD'")]     // 本位幣 net+tax≠gross
    [InlineData("'Invoice'", "0,0,0,0,0,0", "32.5", "'USD','TWD'")]                      // 零金額
    [InlineData("'Invoice'", "110,-5,105,3575,-162.5,3412.5", "32.5", "'USD','TWD'")]   // 負稅額
    [InlineData("'Invoice'", "100,5,105,3250,162.5,3412.5", "0", "'USD','TWD'")]         // 匯率非正
    [InlineData("'Invoice'", "100,5,105,3250,162.5,3412.5", "32.5", "'usd','TWD'")]      // 幣別格式
    [InlineData("'Invoice'", "100,5,105,3250,162.5,3412.5", "32.5", "'USD','TW'")]       // 本位幣格式
    public async Task SqlRejectsInvalidSourceRows(string type, string amounts, string rate, string currencies)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(94000000, 95000000), token);
        await AssertRejectedAsync(db, 
            $"INSERT dbo.ar_sales_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Customer},{type},{fixture.SourceId},1,'fp',{fixture.JournalA},'2026-03-31',{currencies},{rate},{amounts},'2026-01-01',1,1)", token);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task SqlRejectsBlankIdentityMissingCreditKeyAndForeignCompanyJournal()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(95000000, 96000000), token);
        var other = await SeedAsync(db, fixture.Company + 1, token);
        const string amounts = "'USD','TWD',32.5,100,5,105,3250,162.5,3412.5,'2026-01-01',1,1";
        // 空白指紋、零版本、零來源 ID。
        foreach (var (version, fingerprint, sourceId) in new[] { (1, "'  '", fixture.SourceId), (0, "'fp'", fixture.SourceId), (1, "'fp'", 0L) })
            await AssertRejectedAsync(db, 
                $"INSERT dbo.ar_sales_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Customer},'Invoice',{sourceId},{version},{fingerprint},{fixture.JournalA},'2026-03-31',{amounts})", token);
        // 客戶沒有 FI 信用鍵（未經鎖協定）不得保存。
        await AssertRejectedAsync(db, 
            $"INSERT dbo.ar_sales_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Customer + 7},'Invoice',{fixture.SourceId},1,'fp',{fixture.JournalA},'2026-03-31',{amounts})", token);
        // 不得連到其他公司的傳票。
        await AssertRejectedAsync(db, 
            $"INSERT dbo.ar_sales_source ({SourceColumns}) VALUES ({fixture.Company},{fixture.Customer},'Invoice',{fixture.SourceId},1,'fp',{other.JournalA},'2026-03-31',{amounts})", token);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task CreditAllocationIsUniquePerLineSameCompanyAndReconciled()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(96000000, 97000000), token);
        var other = await SeedAsync(db, fixture.Company + 1, token);
        var credit = await InsertSourceAsync(db, fixture, "Return", fixture.JournalB, 40m, 2m, token);
        var receivable = await InsertReceivableAsync(db, fixture, token);
        var foreignReceivable = await InsertReceivableAsync(db, other, token);
        var line = Random.Shared.NextInt64(10000000, 90000000);

        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.ar_credit_allocation ({AllocationColumns}) VALUES ({fixture.Company},{credit},{receivable},{line},1.234567,40,2,42,1300,65,1365,'2026-01-01',1,1)", token);
        Assert.Equal("1.234567|1365.0000", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(quantity,'|',base_gross_amount) AS [Value] FROM dbo.ar_credit_allocation WHERE credit_source_id={credit}").SingleAsync(token));

        // 同一信用來源不得重複分配同一發票行（BR-FI-011）。
        await AssertRejectedAsync(db, 
            $"INSERT dbo.ar_credit_allocation ({AllocationColumns}) VALUES ({fixture.Company},{credit},{receivable},{line},1,40,2,42,1300,65,1365,'2026-01-01',1,1)", token);
        // 跨公司應收不得分配。
        await AssertRejectedAsync(db, 
            $"INSERT dbo.ar_credit_allocation ({AllocationColumns}) VALUES ({fixture.Company},{credit},{foreignReceivable},{line + 1},1,40,2,42,1300,65,1365,'2026-01-01',1,1)", token);
        foreach (var values in new[]
                 {
                     $"{line + 2},0,40,2,42,1300,65,1365",         // 數量為零
                     $"{line + 3},-1,40,2,42,1300,65,1365",        // 數量為負
                     $"{line + 4},1,40,2,43,1300,65,1365",         // 原幣不平
                     $"{line + 5},1,40,2,42,1300,65,1366",         // 本位幣不平
                     $"{line + 6},1,0,0,0,0,0,0",                  // 零金額
                     $"0,1,40,2,42,1300,65,1365",                  // 發票行 ID 非正
                 })
            await AssertRejectedAsync(db, 
                $"INSERT dbo.ar_credit_allocation ({AllocationColumns}) VALUES ({fixture.Company},{credit},{receivable},{values},'2026-01-01',1,1)", token);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task SavedSourceAndAllocationAreImmutable()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // THROW 會中止交易，所以每個被拒的修改各用一個獨立交易。
        var fixture = await SeedAsync(db, Random.Shared.NextInt64(97000000, 98000000), token);
        var invoice = await InsertSourceAsync(db, fixture, "Invoice", fixture.JournalA, 100m, 5m, token);
        var updateError = await AssertRejectedAsync(db, 
            $"UPDATE dbo.ar_sales_source SET fingerprint='forged' WHERE id={invoice}", token);
        Assert.Equal(51024, updateError.Number);
        await transaction.RollbackAsync(token);

        await using var second = await db.Database.BeginTransactionAsync(token);
        var again = await SeedAsync(db, Random.Shared.NextInt64(98000000, 99000000), token);
        var source = await InsertSourceAsync(db, again, "Invoice", again.JournalA, 10m, 0m, token);
        var deleteError = await AssertRejectedAsync(db, $"DELETE dbo.ar_sales_source WHERE id={source}", token);
        Assert.Equal(51024, deleteError.Number);
        await second.RollbackAsync(token);

        await using var third = await db.Database.BeginTransactionAsync(token);
        var allocationFixture = await SeedAsync(db, Random.Shared.NextInt64(99000000, 99900000), token);
        var thirdCredit = await InsertSourceAsync(db, allocationFixture, "Return", allocationFixture.JournalB, 40m, 2m, token);
        var thirdReceivable = await InsertReceivableAsync(db, allocationFixture, token);
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.ar_credit_allocation ({AllocationColumns}) VALUES ({allocationFixture.Company},{thirdCredit},{thirdReceivable},78,1,40,2,42,1300,65,1365,'2026-01-01',1,1)", token);
        var allocationError = await AssertRejectedAsync(db, 
            $"UPDATE dbo.ar_credit_allocation SET quantity=2 WHERE credit_source_id={thirdCredit}", token);
        Assert.Equal(51024, allocationError.Number);
        await third.RollbackAsync(token);

        await using var fourth = await db.Database.BeginTransactionAsync(token);
        var deleteFixture = await SeedAsync(db, Random.Shared.NextInt64(99900000, 99990000), token);
        var fourthCredit = await InsertSourceAsync(db, deleteFixture, "Return", deleteFixture.JournalB, 40m, 2m, token);
        var fourthReceivable = await InsertReceivableAsync(db, deleteFixture, token);
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT dbo.ar_credit_allocation ({AllocationColumns}) VALUES ({deleteFixture.Company},{fourthCredit},{fourthReceivable},79,1,40,2,42,1300,65,1365,'2026-01-01',1,1)", token);
        var allocationDelete = await AssertRejectedAsync(db, 
            $"DELETE dbo.ar_credit_allocation WHERE credit_source_id={fourthCredit}", token);
        Assert.Equal(51024, allocationDelete.Number);
        await fourth.RollbackAsync(token);
    }

    /// <summary>只接受約束拒絕（547 檢查／外鍵、2601／2627 唯一、51024 不可變）；表不存在（208）等錯誤不算通過。</summary>
    private static async Task<SqlException> AssertRejectedAsync(Microsoft.EntityFrameworkCore.DbContext db, string sql, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, token));
        Assert.Contains(error.Number, ConstraintErrors);
        return error;
    }

    private sealed record Fixture(long Company, long Customer, long SourceId, long JournalA, long JournalB);

    /// <summary>建立同一公司的期間、FI 信用鍵與兩張已過帳傳票（各有來源鍵），供來源列引用。</summary>
    private static async Task<Fixture> SeedAsync(Microsoft.EntityFrameworkCore.DbContext db, long company, CancellationToken token)
    {
        var customer = Random.Shared.NextInt64(10000000, 90000000);
        var sourceId = Random.Shared.NextInt64(10000000, 90000000);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_period (company_id,start_date,end_date,status,created_at,created_by,version) VALUES ({company},'2026-03-01','2026-03-31','Open','2026-01-01',1,1)", token);
        var period = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={company}").SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ar_credit_key (company_id,customer_id,created_at,created_by,version) VALUES ({company},{customer},'2026-01-01',1,1)", token);
        var journals = new List<long>();
        foreach (var (type, id) in new[] { ("Invoice", sourceId), ("Return", sourceId + 1) })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({company},'SD',{type},{id},1,'fp','2026-01-01',1,1)", token);
            var key = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={company} AND source_type={type} AND source_id={id}").SingleAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({company},{period},{key},'2026-03-31','TWD','Posted',10,10,'2026-01-01',1,1)", token);
            journals.Add(await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_journal WHERE company_id={company} AND source_key_id={key}").SingleAsync(token));
        }

        return new Fixture(company, customer, sourceId, journals[0], journals[1]);
    }

    private static async Task<long> InsertSourceAsync(Microsoft.EntityFrameworkCore.DbContext db, Fixture fixture, string type, long journal, decimal net, decimal tax, CancellationToken token)
    {
        var sourceId = type == "Invoice" ? fixture.SourceId : fixture.SourceId + 1;
        var gross = net + tax;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT dbo.ar_sales_source (company_id,customer_id,source_type,source_id,source_version,fingerprint,gl_journal_id,business_date,currency,base_currency,exchange_rate,net_amount,tax_amount,gross_amount,base_net_amount,base_tax_amount,base_gross_amount,created_at,created_by,version) VALUES ({fixture.Company},{fixture.Customer},{type},{sourceId},1,'fp',{journal},'2026-03-31','USD','TWD',32.5,{net},{tax},{gross},{net * 32.5m},{tax * 32.5m},{gross * 32.5m},'2026-01-01',1,1)", token);
        return await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.ar_sales_source WHERE company_id={fixture.Company} AND source_type={type} AND source_id={sourceId}").SingleAsync(token);
    }

    private static async Task<long> InsertReceivableAsync(Microsoft.EntityFrameworkCore.DbContext db, Fixture fixture, CancellationToken token)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT dbo.ar_posted_receivable (company_id,customer_id,source_type,source_id,base_currency,original_base_amount,open_base_amount,posting_date,created_at,created_by,version) VALUES ({fixture.Company},{fixture.Customer},'Invoice',{fixture.SourceId},'TWD',3412.5,3412.5,'2026-03-31','2026-01-01',1,1)", token);
        return await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.ar_posted_receivable WHERE company_id={fixture.Company} AND source_type='Invoice' AND source_id={fixture.SourceId}").SingleAsync(token);
    }
}
