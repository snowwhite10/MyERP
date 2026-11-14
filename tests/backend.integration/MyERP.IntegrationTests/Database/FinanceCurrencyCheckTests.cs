using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-FI-001f：舊 FI 幣別檢查（ar_credit_limit、ar_posted_receivable、gl_journal）在資料庫預設不分大小寫定序下
/// 必須只接受三個大寫 ASCII 字母（FI-CUR-001～004，BR-FI-148，ADR-106／ADR-114）。
/// 全部寫入在交易內完成並回滾，不留資料。
/// </summary>
public sealed class FinanceCurrencyCheckTests(TestDatabase database)
{
    // 非 ASCII（例如 Ä）在寫入 char(3) 前已被 SQL Server 以代碼頁 950 best-fit 轉成 A，檢查約束看不到原字元；
    // 這是欄位型別轉換而非檢查定序問題，由幣別主檔與 API 驗證負責（ADR-114）。
    private static readonly string[] InvalidCodes = ["twd", "Twd", "TWd", "T1D", "TW ", "T-D"];

    [Fact]
    public async Task CreditLimitCurrencyAcceptsUppercaseOnly()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var (company, customer) = await SeedCreditKeyAsync(db, token);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT dbo.ar_credit_limit (company_id,customer_id,base_currency,effective_from,effective_to,amount,status,created_at,created_by,version) VALUES ({company},{customer},'TWD','2026-01-01','2026-12-31',100,'Active','2026-01-01',1,1)", token);
        foreach (var code in InvalidCodes)
        {
            await AssertCheckRejectedAsync("ck_ar_credit_limit_currency", () => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT dbo.ar_credit_limit (company_id,customer_id,base_currency,effective_from,effective_to,amount,status,created_at,created_by,version) VALUES ({company},{customer},{code},'2026-01-01','2026-12-31',100,'Active','2026-01-01',1,1)", token));
        }

        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_credit_limit WHERE company_id={company}").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task PostedReceivableCurrencyAcceptsUppercaseOnly()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var (company, customer) = await SeedCreditKeyAsync(db, token);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT dbo.ar_posted_receivable (company_id,customer_id,source_type,source_id,base_currency,original_base_amount,open_base_amount,posting_date,created_at,created_by,version) VALUES ({company},{customer},'Invoice',1,'HKD',10,10,'2026-03-31','2026-01-01',1,1)", token);
        var sourceId = 2L;
        foreach (var code in InvalidCodes)
        {
            var id = sourceId++;
            await AssertCheckRejectedAsync("ck_ar_posted_receivable_currency", () => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT dbo.ar_posted_receivable (company_id,customer_id,source_type,source_id,base_currency,original_base_amount,open_base_amount,posting_date,created_at,created_by,version) VALUES ({company},{customer},'Invoice',{id},{code},10,10,'2026-03-31','2026-01-01',1,1)", token));
        }

        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ar_posted_receivable WHERE company_id={company}").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task JournalCurrencyAcceptsUppercaseOnly()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var company = Random.Shared.NextInt64(310000000, 320000000);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_period (company_id,start_date,end_date,status,created_at,created_by,version) VALUES ({company},'2026-03-01','2026-03-31','Open','2026-01-01',1,1)", token);
        var period = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={company}").SingleAsync(token);

        // 每次嘗試使用自己的來源鍵，令被拒原因只可能是幣別檢查，而不是一張來源鍵只能有一張傳票。
        string[] codes = ["USD", .. InvalidCodes];
        for (var i = 0; i < codes.Length; i++)
        {
            var sourceId = i + 1L;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({company},'SD','Invoice',{sourceId},1,'fp','2026-01-01',1,1)", token);
            var key = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={company} AND source_id={sourceId}").SingleAsync(token);
            var code = codes[i];
            Task<int> Insert() => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({company},{period},{key},'2026-03-31',{code},'Posted',10,10,'2026-01-01',1,1)", token);
            if (i == 0)
            {
                Assert.Equal(1, await Insert());
            }
            else
            {
                await AssertCheckRejectedAsync("ck_gl_journal_currency", Insert);
            }
        }

        Assert.Equal("USD", await db.Database.SqlQuery<string>($"SELECT base_currency AS [Value] FROM dbo.gl_journal WHERE company_id={company}").SingleAsync(token));
        await transaction.RollbackAsync(token);
    }

    [Fact]
    public async Task OldCurrencyChecksUseBinaryCollationAndAreTrusted()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        var rows = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] + '|' + CAST([is_not_trusted] AS varchar(1)) + '|' + [definition] AS [Value] FROM sys.check_constraints WHERE [name] IN ('ck_ar_credit_limit_currency','ck_ar_posted_receivable_currency','ck_gl_journal_currency')")
            .ToListAsync(token);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Contains("|0|", row, StringComparison.Ordinal);
            Assert.Contains("Latin1_General_BIN", row, StringComparison.Ordinal);
        });
    }

    private static async Task<(long Company, long Customer)> SeedCreditKeyAsync(DbContext db, CancellationToken token)
    {
        var company = Random.Shared.NextInt64(300000000, 310000000);
        var customer = Random.Shared.NextInt64(10000000, 90000000);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ar_credit_key (company_id,customer_id,created_at,created_by,version) VALUES ({company},{customer},'2026-01-01',1,1)", token);
        return (company, customer);
    }

    /// <summary>只接受指定檢查約束（547）的拒絕；其他錯誤（外鍵、唯一鍵、表不存在）不算通過。</summary>
    private static async Task AssertCheckRejectedAsync(string constraint, Func<Task<int>> insert)
    {
        var error = await Assert.ThrowsAsync<SqlException>(insert);
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message, StringComparison.Ordinal);
    }
}
