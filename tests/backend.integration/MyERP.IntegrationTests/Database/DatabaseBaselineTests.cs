using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Migrations;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>資料庫基線（WU-P0-008）：migration 已全部套用、定序、SP 部署。</summary>
public sealed class DatabaseBaselineTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<T> ScalarAsync<T>(MyErpDbContext db, FormattableString sql) =>
        await db.Database.SqlQuery<T>(sql).SingleAsync(Ct);

    [Fact]
    public async Task Migrations_AllApplied_IncludingCoreBaseline()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => database.UseTestDatabase(b));
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();

        (await db.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(Ct)).Should().Contain(m => m.EndsWith("_Core_Baseline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProductionModel_HasNoChangesMissingFromMigrations()
    {
        // 模組改了實體卻沒有新增 migration 時失敗
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => database.UseTestDatabase(b));
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();

        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task Database_UsesConfiguredCollation()
    {
        await using var db = database.CreateRawContext();

        var collation = await ScalarAsync<string>(db, $"SELECT CONVERT(NVARCHAR(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS Value");

        collation.Should().Be(MyErpDbContext.DatabaseCollation);
    }

    [Fact]
    public async Task Database_Collation_IsCaseInsensitiveAndSupportsSupplementaryCharacters()
    {
        await using var db = database.CreateRawContext();

        (await ScalarAsync<int>(db, $"SELECT CASE WHEN N'abc' = N'ABC' THEN 1 ELSE 0 END AS Value")).Should().Be(1);
        // _SC 定序下補充字元（U+20BB7 𠮷）長度為 1
        (await ScalarAsync<int>(db, $"SELECT LEN(N'𠮷') AS Value")).Should().Be(1);
    }

    [Fact]
    public async Task EveryEmbeddedProcedure_IsDeployed()
    {
        await using var db = database.CreateRawContext();
        var procedures = ProcedureScripts.All();

        procedures.Should().NotBeEmpty();
        foreach (var path in procedures)
        {
            var name = "dbo." + path.Split('/')[^1];
            var exists = await ScalarAsync<int>(db, $"SELECT CASE WHEN OBJECT_ID({name}, N'P') IS NULL THEN 0 ELSE 1 END AS Value");
            exists.Should().Be(1, $"{path} must be deployed by a migration");
        }
    }

    [Fact]
    public async Task DatabaseInfoProcedure_ReturnsCompanyDatabaseAndCollation()
    {
        await using var db = database.CreateRawContext();

        var info = await db.Database
            .SqlQuery<DatabaseInfo>($"EXEC dbo.usp_sys_database_info @company_id = {42L}")
            .ToListAsync(Ct);

        info.Should().ContainSingle().Which.Should().Be(new DatabaseInfo(42, TestDatabase.DatabaseName, MyErpDbContext.DatabaseCollation));
    }

    private sealed record DatabaseInfo(long CompanyId, string DatabaseName, string CollationName);
}
