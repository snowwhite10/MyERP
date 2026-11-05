using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace MyERP.IntegrationTests.Database;

/// <summary>Real SQL contract for the FI accounting period foundation.</summary>
public sealed class FinancePeriodSchemaTests(TestDatabase database)
{
    [Fact]
    public async Task PeriodHasInclusiveDatesStateVersionAndCompanyLookup()
    {
        await using var db = database.CreateRawContext();
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.gl_period')")
            .ToListAsync(TestContext.Current.CancellationToken);
        foreach (var column in new[] { "id", "company_id", "start_date", "end_date", "status", "version", "created_at", "created_by", "updated_at", "updated_by" })
            Assert.Contains(column, columns);

        var constraints = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.check_constraints WHERE [parent_object_id] = OBJECT_ID('dbo.gl_period')")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("ck_gl_period_dates", constraints);
        Assert.Contains("ck_gl_period_status", constraints);
        Assert.Contains("ck_gl_period_version", constraints);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.indexes WHERE [object_id] = OBJECT_ID('dbo.gl_period')")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("ix_gl_period_company_id_start_date_end_date", indexes);
    }

    [Theory]
    [InlineData("2026-02-02", "2026-02-01", "Open", 1, 1)]
    [InlineData("2026-02-01", "2026-02-02", "Draft", 1, 1)]
    [InlineData("2026-02-01", "2026-02-02", "Open", 0, 1)]
    [InlineData("2026-02-01", "2026-02-02", "Open", 1, 0)]
    public async Task SqlRejectsInvalidPeriodRows(string start, string end, string status, int version, long companyId)
    {
        await using var db = database.CreateRawContext();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT dbo.gl_period (company_id,start_date,end_date,status,version,created_at,created_by) VALUES ({companyId},{DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture)},{DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)},{status},{version},'2026-01-01',1)",
            TestContext.Current.CancellationToken));
    }
}
