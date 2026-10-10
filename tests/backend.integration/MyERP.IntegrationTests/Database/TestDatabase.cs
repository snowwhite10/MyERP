using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

[assembly: AssemblyFixture(typeof(MyERP.IntegrationTests.Database.TestDatabase))]

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// 整合測試資料庫 <c>myerp_test</c>（db-conventions.md 第 10 節）：每次測試執行前重建一次——DROP → 以正式主機（模組組合與執行期一致）套用全部 migration
/// → 建立測試模組的 <c>tst_*</c> 表（不屬於 migration）。測試只能連到 <c>myerp_test</c>。
/// 連線字串可用環境變數 <c>MYERP_TEST_CONNECTION</c> 覆寫（資料庫名稱仍必須是 myerp_test）。
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    public const string DatabaseName = "myerp_test";

    private const string DefaultConnectionString =
        @"Server=localhost\MSSQLSERVER2022;Database=myerp_test;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;MultipleActiveResultSets=True;Connection Timeout=30;";

    public TestDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("MYERP_TEST_CONNECTION") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : DefaultConnectionString;
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.InitialCatalog, DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Integration tests must use database '{DatabaseName}', not '{builder.InitialCatalog}'.");
        }

        ConnectionString = builder.ConnectionString;
    }

    public string ConnectionString { get; }

    public async ValueTask InitializeAsync()
    {
        // 正式主機的模型（不加測試模組），Migrate 才不會因模型與 snapshot 不同而失敗
        await using (var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => UseTestDatabase(b)))
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            await db.Database.EnsureDeletedAsync().ConfigureAwait(false);
            SqlConnection.ClearAllPools();
            await db.Database.MigrateAsync().ConfigureAwait(false);
        }

        await using var testTables = CreateContext(new FixedCompany(null));
        await testTables.GetService<IRelationalDatabaseCreator>().CreateTablesAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>令主機改連 myerp_test。</summary>
    public IWebHostBuilder UseTestDatabase(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}", ConnectionString);
    }

    /// <summary>只含測試模組的 DbContext（與正式模型分開快取，見 ErpModelCacheKeyFactory）。</summary>
    public MyErpDbContext CreateContext(ICurrentCompany company, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(ConnectionString).AddInterceptors(interceptors).Options,
            [new TestModuleDbModel()],
            company);

    /// <summary>不含任何模組的 DbContext，用於執行原生 SQL。</summary>
    public MyErpDbContext CreateRawContext() =>
        new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(ConnectionString).Options, [], new FixedCompany(null));
}
