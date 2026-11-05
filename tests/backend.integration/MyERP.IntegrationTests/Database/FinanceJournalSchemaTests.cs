using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MyERP.IntegrationTests.Database;

/// <summary>Real SQL schema contract for permanent GL source identity and posted dates.</summary>
public sealed class FinanceJournalSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task JournalTablesHavePermanentIdentityAndPostedDateLookup()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        foreach (var table in new[] { "gl_source_key", "gl_journal", "gl_journal_line" })
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE object_id = OBJECT_ID({"dbo." + table})").SingleAsync(token));
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gl_source_key')").ToListAsync(token);
        Assert.Contains("ux_gl_source_key_company_module_type_source", indexes);
        indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gl_journal')").ToListAsync(token);
        Assert.Contains("ix_gl_journal_company_period_business_date_status", indexes);
        Assert.Contains("ux_gl_journal_source_key_id", indexes);
        indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gl_journal_line')").ToListAsync(token);
        Assert.Contains("ux_gl_journal_line_journal_id_sequence", indexes);

        var fks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('dbo.gl_journal'), OBJECT_ID('dbo.gl_journal_line'))").ToListAsync(token);
        Assert.Contains("fk_gl_journal_gl_period_company_id_period_id", fks);
        Assert.Contains("fk_gl_journal_gl_source_key_company_id_source_key_id", fks);
        Assert.Contains("fk_gl_journal_line_gl_journal_company_id_journal_id", fks);
    }

    [Fact]
    public async Task SqlRejectsInvalidSourceAndLineAmounts()
    {
        await using var db = database.CreateRawContext();
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES (1,'SD','Order',0,1,'hash','2026-01-01',1,1)", token));
    }

    [Fact]
    public async Task PermanentKeyIsUniqueAndPostedDateCanBeReadWithinPeriod()
    {
        await using var db = database.CreateRawContext();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var token = TestContext.Current.CancellationToken;
        var company = Random.Shared.NextInt64(91000000, 92000000);
        var source = Random.Shared.NextInt64(10000000, 90000000);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_period (company_id,start_date,end_date,status,created_at,created_by,version) VALUES ({company},'2026-01-01','2026-01-31','Open','2026-01-01',1,1)", token);
        var periodId = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_period WHERE company_id={company}").SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({company},'SD','Order',{source},1,'fingerprint','2026-01-01',1,1)", token);
        var keyId = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_source_key WHERE company_id={company} AND source_id={source}").SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal (company_id,period_id,source_key_id,business_date,base_currency,status,total_debit,total_credit,created_at,created_by,version) VALUES ({company},{periodId},{keyId},'2026-01-31','USD','Posted',10,10,'2026-01-01',1,1)", token);
        var journalId = await db.Database.SqlQuery<long>($"SELECT id AS [Value] FROM dbo.gl_journal WHERE company_id={company} AND source_key_id={keyId}").SingleAsync(token);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal_line (company_id,journal_id,sequence,account_id,account_code,debit,credit,created_at,created_by,version) VALUES ({company},{journalId},1,5,'1000',10,10,'2026-01-01',1,1)", token));
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal_line (company_id,journal_id,sequence,account_id,account_code,debit,credit,created_at,created_by,version) VALUES ({company},{journalId},1,5,'1000',10,0,'2026-01-01',1,1)", token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_journal_line (company_id,journal_id,sequence,account_id,account_code,debit,credit,created_at,created_by,version) VALUES ({company},{journalId},2,6,'2000',0,10,'2026-01-01',1,1)", token);
        var posted = await db.Database.SqlQuery<DateOnly>($"SELECT business_date AS [Value] FROM dbo.gl_journal WHERE company_id={company} AND period_id={periodId} AND status='Posted'").SingleAsync(token);
        Assert.Equal(new DateOnly(2026, 1, 31), posted);
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal_line WHERE company_id={company} AND journal_id={journalId}").SingleAsync(token));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.gl_source_key (company_id,source_module,source_type,source_id,source_version,fingerprint,created_at,created_by,version) VALUES ({company},'SD','Order',{source},2,'changed','2026-01-01',1,1)", token));
        await transaction.RollbackAsync(token);
    }
}
