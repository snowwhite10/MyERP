using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// 本檔的原始 SQL 片段全由測試常數與 Random 產生的數值組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-FI-005a1：銷售收款與應收核銷已過帳來源的真實 SQL 結構契約（BR-FI-033～037、040；FI-AR-COLL-008、010、018、020、026）。
/// 只驗證表、鍵、檢查與觸發器；過帳服務、鎖序、冪等與回滾由 WU-FI-005a2／005a3 驗收。
/// </summary>
public sealed class FinanceCollectionSchemaTests(TestDatabase database)
{
    private static readonly int[] ConstraintErrors = [547, 2601, 2627, 51029];

    private const string ReceiptColumns =
        "company_id,customer_id,source_type,source_id,source_version,fingerprint,gl_journal_id,business_date,bank_account_id,bank_reference,base_currency,amount,unapplied_amount,created_at,created_by,version";

    private const string SettlementColumns =
        "company_id,customer_id,source_type,source_id,source_version,fingerprint,gl_journal_id,receipt_id,business_date,base_currency,total_amount,created_at,created_by,version";

    private const string AllocationColumns =
        "company_id,settlement_id,receipt_id,receivable_id,amount,created_at,created_by,version";

    private static readonly string[] Tables = ["ar_collection_receipt", "ar_collection_settlement", "ar_collection_allocation"];

    [Fact]
    public async Task CollectionTablesHavePermanentKeysForeignKeysAndLookupIndexes()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        foreach (var table in Tables)
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE object_id = OBJECT_ID({"dbo." + table})").SingleAsync(token));
        const string objects = "(OBJECT_ID('dbo.ar_collection_receipt'), OBJECT_ID('dbo.ar_collection_settlement'), OBJECT_ID('dbo.ar_collection_allocation'))";

        var indexes = await db.Database.SqlQueryRaw<string>($"SELECT name AS [Value] FROM sys.indexes WHERE object_id IN {objects}").ToListAsync(token);
        foreach (var name in new[]
                 {
                     "ux_ar_collection_receipt_company_id_source_type_source_id", "ux_ar_collection_receipt_gl_journal_id",
                     "ux_ar_collection_receipt_company_id_bank_account_id_bank_reference", "ix_ar_collection_receipt_company_id_customer_id_business_date",
                     "ux_ar_collection_settlement_company_id_source_type_source_id", "ux_ar_collection_settlement_gl_journal_id",
                     "ix_ar_collection_settlement_company_id_receipt_id", "ix_ar_collection_settlement_company_id_customer_id",
                     "ux_ar_collection_allocation_company_id_settlement_id_receivable_id", "ix_ar_collection_allocation_company_id_receipt_id",
                     "ix_ar_collection_allocation_company_id_receivable_id",
                 })
            Assert.Contains(name, indexes);

        var fks = await db.Database.SqlQueryRaw<string>($"SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN {objects}").ToListAsync(token);
        foreach (var name in new[]
                 {
                     "fk_ar_collection_receipt_ar_credit_key_company_id_customer_id", "fk_ar_collection_receipt_gl_journal_company_id_gl_journal_id",
                     "fk_ar_collection_settlement_ar_credit_key_company_id_customer_id", "fk_ar_collection_settlement_gl_journal_company_id_gl_journal_id",
                     "fk_ar_collection_settlement_ar_collection_receipt_company_id_receipt_id",
                     "fk_ar_collection_allocation_ar_collection_settlement_company_id_settlement_id",
                     "fk_ar_collection_allocation_ar_collection_receipt_company_id_receipt_id",
                     "fk_ar_collection_allocation_ar_posted_receivable_company_id_receivable_id",
                 })
            Assert.Contains(name, fks);

        var triggers = await db.Database.SqlQueryRaw<string>($"SELECT name AS [Value] FROM sys.triggers WHERE parent_id IN {objects}").ToListAsync(token);
        Assert.Contains("tr_ar_collection_receipt_origin", triggers);
        Assert.Contains("tr_ar_collection_settlement_immutable", triggers);
        Assert.Contains("tr_ar_collection_allocation_immutable", triggers);

        var columns = await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(OBJECT_NAME(c.object_id), '.', c.name, ':', t.name, ':', c.max_length, ':', c.precision, ':', c.scale) AS [Value] FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id IN {objects} AND c.name IN ('amount','unapplied_amount','total_amount','bank_reference','base_currency','business_date')").ToListAsync(token);
        Assert.Contains("ar_collection_receipt.amount:decimal:9:19:4", columns);
        Assert.Contains("ar_collection_receipt.unapplied_amount:decimal:9:19:4", columns);
        Assert.Contains("ar_collection_receipt.bank_reference:nvarchar:100:0:0", columns);
        Assert.Contains("ar_collection_receipt.base_currency:char:3:0:0", columns);
        Assert.Contains("ar_collection_receipt.business_date:date:3:10:0", columns);
        Assert.Contains("ar_collection_settlement.total_amount:decimal:9:19:4", columns);
        Assert.Contains("ar_collection_allocation.amount:decimal:9:19:4", columns);
        // 無軟刪除欄位：已過帳收款與核銷的來源鍵、銀行參考永久佔用（BR-FI-033、FI-AR-COLL-008、010）。
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id IN {objects} AND name = 'is_deleted'").SingleAsync(token));
    }

    [Fact]
    public async Task ReceiptSourceKeyJournalAndBankReferenceArePermanentlyUnique()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, Random.Shared.NextInt64(90000000, 90100000), token);
        var other = await SeedAsync(db, f.Company + 1, token);
        var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-001", 1000m, token);
        Assert.Equal("1000.0000|1000.0000|TWD", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(amount,'|',unapplied_amount,'|',base_currency) AS [Value] FROM dbo.ar_collection_receipt WHERE id={receipt}").SingleAsync(token));

        // 同公司同來源 ID 即使版本或指紋不同也不能再保存（BR-FI-033、FI-AR-COLL-010）。
        await AssertRejectedAsync(db, ReceiptSql(f, f.Journals[1], f.SourceId, "BR-002", "1000,1000", version: 2, fingerprint: "changed"), token);
        // 一張傳票只屬於一筆收款。
        await AssertRejectedAsync(db, ReceiptSql(f, f.Journals[0], f.SourceId + 1, "BR-002", "1000,1000"), token);
        // 同公司同銀行同參考重複（預設定序不分大小寫）（FI-AR-COLL-008）。
        await AssertRejectedAsync(db, ReceiptSql(f, f.Journals[1], f.SourceId + 1, "BR-001", "500,500"), token);
        await AssertRejectedAsync(db, ReceiptSql(f, f.Journals[1], f.SourceId + 1, "br-001", "500,500"), token);
        // 不同銀行或不同公司的相同參考互不影響。
        await InsertReceiptAsync(db, f, f.Journals[1], f.SourceId + 1, "BR-001", 500m, token, bank: f.Bank + 1);
        await InsertReceiptAsync(db, other, other.Journals[0], f.SourceId, "BR-001", 500m, token, bank: f.Bank);
        await transaction.RollbackAsync(token);
    }

    [Theory]
    [InlineData("'SalesReceipt'", "'BR-1'", "1000,1000", "'TWD'", 1L, 547)]   // 類型只接受 SalesCollection
    [InlineData("'SalesCollection'", "'BR-1'", "0,0", "'TWD'", 1L, 547)]     // 零金額
    [InlineData("'SalesCollection'", "'BR-1'", "-5,-5", "'TWD'", 1L, 547)]   // 負金額
    [InlineData("'SalesCollection'", "'BR-1'", "100,101", "'TWD'", 1L, 547)] // 未核銷大於金額
    [InlineData("'SalesCollection'", "'BR-1'", "100,-1", "'TWD'", 1L, 547)]  // 未核銷為負
    [InlineData("'SalesCollection'", "'BR-1'", "100,40", "'TWD'", 1L, 51029)] // 過帳時未核銷必須等於金額（BR-FI-034）
    [InlineData("'SalesCollection'", "'BR-1'", "100,100", "'twd'", 1L, 547)] // 幣別小寫
    [InlineData("'SalesCollection'", "'   '", "100,100", "'TWD'", 1L, 547)]  // 空白銀行參考
    [InlineData("'SalesCollection'", "'BR-1'", "100,100", "'TWD'", 0L, 547)] // 銀行 ID 非正
    public async Task SqlRejectsInvalidReceiptRows(string type, string reference, string amounts, string currency, long bankFactor, int expected)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, Random.Shared.NextInt64(90100000, 90200000), token);
        var error = await AssertRejectedAsync(db,
            $"INSERT dbo.ar_collection_receipt ({ReceiptColumns}) VALUES ({f.Company},{f.Customer},{type},{f.SourceId},1,'fp',{f.Journals[0]},'2026-03-31',{f.Bank * bankFactor},{reference},{currency},{amounts},'2026-01-01',1,1)", token);
        Assert.Equal(expected, error.Number);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task SqlRejectsBlankReceiptIdentityUnknownCustomerAndForeignJournal()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, Random.Shared.NextInt64(90200000, 90300000), token);
        var other = await SeedAsync(db, f.Company + 1, token);
        await AssertRejectedAsync(db, ReceiptSql(f, f.Journals[0], f.SourceId, "BR-1", "100,100", fingerprint: "  "), token);
        await AssertRejectedAsync(db, ReceiptSql(f, f.Journals[0], f.SourceId, "BR-1", "100,100", version: 0), token);
        await AssertRejectedAsync(db, ReceiptSql(f, f.Journals[0], 0, "BR-1", "100,100"), token);
        // 客戶必須有同公司的信用鍵（收款與信用決策共用鎖目標，BR-FI-036）。
        await AssertRejectedAsync(db, ReceiptSql(f with { Customer = f.SecondCustomer + 1 }, f.Journals[0], f.SourceId, "BR-1", "100,100"), token);
        // 不得連到其他公司的傳票（FI-AR-COLL-015）。
        await AssertRejectedAsync(db, ReceiptSql(f, other.Journals[0], f.SourceId, "BR-1", "100,100"), token);
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task ReceiptOnlyAllowsUnappliedAmountToDecrease()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;

        await using (var allowed = await db.Database.BeginTransactionAsync(token))
        {
            var f = await SeedAsync(db, Random.Shared.NextInt64(90300000, 90310000), token);
            var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-1", 1000m, token);
            // 核銷提交時只減少未核銷並遞增版本（BR-FI-034、037）；可減至零（全數核銷）。
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE dbo.ar_collection_receipt SET unapplied_amount=400, version=version+1, updated_at='2026-04-01', updated_by=1 WHERE id={receipt}", token);
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE dbo.ar_collection_receipt SET unapplied_amount=0, version=version+1, updated_at='2026-04-02', updated_by=1 WHERE id={receipt}", token);
            Assert.Equal("1000.0000|0.0000|3", await db.Database.SqlQueryRaw<string>(
                $"SELECT CONCAT(amount,'|',unapplied_amount,'|',version) AS [Value] FROM dbo.ar_collection_receipt WHERE id={receipt}").SingleAsync(token));
            await allowed.RollbackAsync(token);
        }

        // THROW 會中止交易，所以每個被拒的修改各用一個獨立交易。
        foreach (var forged in new[]
                 {
                     "amount=999, unapplied_amount=999", "customer_id=customer_id", "source_version=2", "fingerprint='forged'",
                     "gl_journal_id=gl_journal_id", "business_date='2026-03-30'", "bank_account_id=bank_account_id+1",
                     "bank_reference='BR-X'", "source_id=source_id+1", "unapplied_amount=500, version=version+1",
                 })
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var f = await SeedAsync(db, Random.Shared.NextInt64(90310000, 90400000), token);
            var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-1", 1000m, token);
            await db.Database.ExecuteSqlRawAsync($"UPDATE dbo.ar_collection_receipt SET unapplied_amount=400 WHERE id={receipt}", token);
            var target = forged.StartsWith("customer_id", StringComparison.Ordinal) ? $"customer_id={f.SecondCustomer}"
                : forged.StartsWith("gl_journal_id", StringComparison.Ordinal) ? $"gl_journal_id={f.Journals[1]}" : forged;
            var error = await AssertRejectedAsync(db, $"UPDATE dbo.ar_collection_receipt SET {target} WHERE id={receipt}", token);
            Assert.Equal(51029, error.Number);
            await tx.RollbackAsync(token);
        }

        await using (var delete = await db.Database.BeginTransactionAsync(token))
        {
            var f = await SeedAsync(db, Random.Shared.NextInt64(90400000, 90410000), token);
            var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-1", 1000m, token);
            Assert.Equal(51029, (await AssertRejectedAsync(db, $"DELETE dbo.ar_collection_receipt WHERE id={receipt}", token)).Number);
            await delete.RollbackAsync(token);
        }
    }

    [Fact]
    public async Task SettlementMatchesItsReceiptAndIsPermanent()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            var f = await SeedAsync(db, Random.Shared.NextInt64(90410000, 90500000), token);
            var other = await SeedAsync(db, f.Company + 1, token);
            var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-1", 1000m, token);
            var foreignReceipt = await InsertReceiptAsync(db, other, other.Journals[0], other.SourceId, "BR-1", 1000m, token);
            var settlement = await InsertSettlementAsync(db, f, f.Journals[1], receipt, f.SourceId, 300m, token);
            Assert.True(settlement > 0);
            // 同公司同來源 ID 永久唯一；一張傳票只屬於一筆核銷。
            await AssertRejectedAsync(db, SettlementSql(f, f.Journals[2], receipt, f.SourceId, "300", version: 2), token);
            await AssertRejectedAsync(db, SettlementSql(f, f.Journals[1], receipt, f.SourceId + 1, "300"), token);
            // 其他公司的收款不可作為來源（複合外鍵）；零或負總額被拒。
            await AssertRejectedAsync(db, SettlementSql(f, f.Journals[2], foreignReceipt, f.SourceId + 1, "300"), token);
            await AssertRejectedAsync(db, SettlementSql(f, f.Journals[2], receipt, f.SourceId + 1, "0"), token);
            await AssertRejectedAsync(db, SettlementSql(f, f.Journals[2], receipt, f.SourceId + 1, "-1"), token);
            // 同一收款可有多筆核銷（部分核銷後以餘額再核銷，BR-FI-034）。
            await InsertSettlementAsync(db, f, f.Journals[2], receipt, f.SourceId + 1, 200m, token);
            await tx.RollbackAsync(token);
        }

        // 與收款不同客戶或幣別（BR-FI-035），以及保存後修改／刪除，觸發器 51029 拒絕並中止交易。
        foreach (var (customerOffset, currency, mutation) in new[]
                 {
                     (1, "TWD", ""), (0, "USD", ""), (0, "TWD", "UPDATE dbo.ar_collection_settlement SET fingerprint='x' WHERE id={0}"),
                     (0, "TWD", "UPDATE dbo.ar_collection_settlement SET total_amount=1 WHERE id={0}"),
                     (0, "TWD", "DELETE dbo.ar_collection_settlement WHERE id={0}"),
                 })
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var f = await SeedAsync(db, Random.Shared.NextInt64(90500000, 90600000), token);
            var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-1", 1000m, token);
            SqlException error;
            if (mutation.Length == 0)
            {
                var customer = customerOffset == 0 ? f.Customer : f.SecondCustomer;
                error = await AssertRejectedAsync(db,
                    $"INSERT dbo.ar_collection_settlement ({SettlementColumns}) VALUES ({f.Company},{customer},'CollectionWriteOff',{f.SourceId},1,'fp',{f.Journals[1]},{receipt},'2026-03-31','{currency}',300,'2026-01-01',1,1)", token);
            }
            else
            {
                var settlement = await InsertSettlementAsync(db, f, f.Journals[1], receipt, f.SourceId, 300m, token);
                error = await AssertRejectedAsync(db, string.Format(System.Globalization.CultureInfo.InvariantCulture, mutation, settlement), token);
            }

            Assert.Equal(51029, error.Number);
            await tx.RollbackAsync(token);
        }
    }

    [Fact]
    public async Task AllocationIsUniquePerSettlementAndReceivableButReceiptMayFundTheSameInvoiceAgain()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, Random.Shared.NextInt64(90600000, 90700000), token);
        var other = await SeedAsync(db, f.Company + 1, token);
        var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-1", 1000m, token);
        var first = await InsertSettlementAsync(db, f, f.Journals[1], receipt, f.SourceId, 300m, token);
        var second = await InsertSettlementAsync(db, f, f.Journals[2], receipt, f.SourceId + 1, 100m, token);

        await db.Database.ExecuteSqlRawAsync(AllocationSql(f.Company, first, receipt, f.Receivables[0], "200"), token);
        await db.Database.ExecuteSqlRawAsync(AllocationSql(f.Company, first, receipt, f.Receivables[1], "100"), token);
        // 同一核銷同一應收只可一行（BR-FI-035）。
        await AssertRejectedAsync(db, AllocationSql(f.Company, first, receipt, f.Receivables[0], "1"), token);
        // 不同核銷可再次以同一收款核銷同一發票（設計審查：唯一性按核銷＋應收，而非收款＋應收）。
        await db.Database.ExecuteSqlRawAsync(AllocationSql(f.Company, second, receipt, f.Receivables[0], "100"), token);
        // 零、負金額與其他公司的應收被拒。
        await AssertRejectedAsync(db, AllocationSql(f.Company, second, receipt, f.Receivables[1], "0"), token);
        await AssertRejectedAsync(db, AllocationSql(f.Company, second, receipt, f.Receivables[1], "-1"), token);
        await AssertRejectedAsync(db, AllocationSql(f.Company, second, receipt, other.Receivables[0], "1"), token);
        Assert.Equal("400.0000", await db.Database.SqlQueryRaw<string>(
            $"SELECT CONCAT(SUM(amount),'') AS [Value] FROM dbo.ar_collection_allocation WHERE company_id={f.Company} AND receipt_id={receipt}").SingleAsync(token));
        await tx.RollbackAsync(token);
    }

    [Theory]
    [InlineData("receipt")]      // 分配的收款必須等於其核銷的收款
    [InlineData("customer")]     // 應收必須屬於核銷的客戶
    [InlineData("currency")]     // 應收本位幣必須等於核銷幣別
    [InlineData("return")]       // 只可核銷發票應收
    [InlineData("update")]       // 已保存分配不可修改
    [InlineData("delete")]       // 已保存分配不可刪除
    public async Task AllocationMismatchOrMutationIsRejectedByTrigger(string scenario)
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var f = await SeedAsync(db, Random.Shared.NextInt64(90700000, 90800000), token);
        var receipt = await InsertReceiptAsync(db, f, f.Journals[0], f.SourceId, "BR-1", 1000m, token);
        var otherReceipt = await InsertReceiptAsync(db, f, f.Journals[2], f.SourceId + 1, "BR-2", 1000m, token);
        var settlement = await InsertSettlementAsync(db, f, f.Journals[1], receipt, f.SourceId, 300m, token);
        var sql = scenario switch
        {
            "receipt" => AllocationSql(f.Company, settlement, otherReceipt, f.Receivables[0], "100"),
            "customer" => AllocationSql(f.Company, settlement, receipt, f.SecondCustomerReceivable, "100"),
            "currency" => AllocationSql(f.Company, settlement, receipt, f.ForeignCurrencyReceivable, "100"),
            "return" => AllocationSql(f.Company, settlement, receipt, f.ReturnReceivable, "100"),
            _ => null,
        };
        if (sql is null)
        {
            await db.Database.ExecuteSqlRawAsync(AllocationSql(f.Company, settlement, receipt, f.Receivables[0], "100"), token);
            sql = scenario == "update"
                ? $"UPDATE dbo.ar_collection_allocation SET amount=50 WHERE settlement_id={settlement}"
                : $"DELETE dbo.ar_collection_allocation WHERE settlement_id={settlement}";
        }

        Assert.Equal(51029, (await AssertRejectedAsync(db, sql, token)).Number);
        await tx.RollbackAsync(token);
    }

    /// <summary>只接受約束拒絕（547 檢查／外鍵、2601／2627 唯一、51029 觸發器）；表不存在（208）等錯誤不算通過。</summary>
    private static async Task<SqlException> AssertRejectedAsync(DbContext db, string sql, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, token));
        Assert.Contains(error.Number, ConstraintErrors);
        return error;
    }

    private sealed record Fixture(
        long Company, long Customer, long SecondCustomer, long Bank, long SourceId, long[] Journals, long[] Receivables,
        long SecondCustomerReceivable, long ForeignCurrencyReceivable, long ReturnReceivable);

    private static string ReceiptSql(Fixture f, long journal, long sourceId, string reference, string amounts, int version = 1, string fingerprint = "fp") =>
        $"INSERT dbo.ar_collection_receipt ({ReceiptColumns}) VALUES ({f.Company},{f.Customer},'SalesCollection',{sourceId},{version},'{fingerprint}',{journal},'2026-03-31',{f.Bank},'{reference}','TWD',{amounts},'2026-01-01',1,1)";

    private static string SettlementSql(Fixture f, long journal, long receipt, long sourceId, string total, int version = 1) =>
        $"INSERT dbo.ar_collection_settlement ({SettlementColumns}) VALUES ({f.Company},{f.Customer},'CollectionWriteOff',{sourceId},{version},'fp',{journal},{receipt},'2026-03-31','TWD',{total},'2026-01-01',1,1)";

    private static string AllocationSql(long company, long settlement, long receipt, long receivable, string amount) =>
        $"INSERT dbo.ar_collection_allocation ({AllocationColumns}) VALUES ({company},{settlement},{receipt},{receivable},{amount},'2026-01-01',1,1)";

    private static async Task<long> InsertReceiptAsync(DbContext db, Fixture f, long journal, long sourceId, string reference, decimal amount, CancellationToken token, long? bank = null)
    {
        await db.Database.ExecuteSqlRawAsync(
            ReceiptSql(f with { Bank = bank ?? f.Bank }, journal, sourceId, reference, $"{amount},{amount}"), token);
        return await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.ar_collection_receipt WHERE company_id={f.Company} AND source_type='SalesCollection' AND source_id={sourceId}").SingleAsync(token);
    }

    private static async Task<long> InsertSettlementAsync(DbContext db, Fixture f, long journal, long receipt, long sourceId, decimal total, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync(SettlementSql(f, journal, receipt, sourceId, total.ToString(System.Globalization.CultureInfo.InvariantCulture)), token);
        return await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.ar_collection_settlement WHERE company_id={f.Company} AND source_type='CollectionWriteOff' AND source_id={sourceId}").SingleAsync(token);
    }

    /// <summary>建立同一公司的兩個客戶信用鍵、期間、三張已過帳傳票（各有 FI 來源鍵）與數筆應收。</summary>
    private static async Task<Fixture> SeedAsync(DbContext db, long company, CancellationToken token)
    {
        var customer = Random.Shared.NextInt64(10000000, 90000000);
        var secondCustomer = customer + 1;
        var bank = Random.Shared.NextInt64(10000000, 90000000);
        var sourceId = Random.Shared.NextInt64(10000000, 90000000);
        foreach (var c in new[] { customer, secondCustomer })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ar_credit_key (company_id,customer_id,created_at,created_by,version) VALUES ({company},{c},'2026-01-01',1,1)", token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_period (company_id,start_date,end_date,status,created_at,created_by,version) VALUES ({company},'2026-03-01','2026-03-31','Open','2026-01-01',1,1)", token);
        var period = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={company}").SingleAsync(token);
        var journals = new List<long>();
        foreach (var id in new[] { sourceId, sourceId + 1, sourceId + 2 })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({company},'FI','SalesCollection',{id},1,'fp','2026-01-01',1,1)", token);
            var key = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={company} AND source_module='FI' AND source_id={id}").SingleAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({company},{period},{key},'2026-03-31','TWD','Posted',10,10,'2026-01-01',1,1)", token);
            journals.Add(await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_journal WHERE company_id={company} AND source_key_id={key}").SingleAsync(token));
        }

        async Task<long> ReceivableAsync(long c, string type, long id, string currency)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT dbo.ar_posted_receivable (company_id,customer_id,source_type,source_id,base_currency,original_base_amount,open_base_amount,posting_date,created_at,created_by,version) VALUES ({company},{c},{type},{id},{currency},500,500,'2026-03-31','2026-01-01',1,1)", token);
            return await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.ar_posted_receivable WHERE company_id={company} AND source_type={type} AND source_id={id}").SingleAsync(token);
        }

        var receivables = new[] { await ReceivableAsync(customer, "Invoice", sourceId, "TWD"), await ReceivableAsync(customer, "Invoice", sourceId + 1, "TWD") };
        var secondCustomerReceivable = await ReceivableAsync(secondCustomer, "Invoice", sourceId + 2, "TWD");
        var foreignCurrencyReceivable = await ReceivableAsync(customer, "Invoice", sourceId + 3, "USD");
        var returnReceivable = await ReceivableAsync(customer, "Return", sourceId + 4, "TWD");
        return new Fixture(company, customer, secondCustomer, bank, sourceId, journals.ToArray(), receivables,
            secondCustomerReceivable, foreignCurrencyReceivable, returnReceivable);
    }
}
