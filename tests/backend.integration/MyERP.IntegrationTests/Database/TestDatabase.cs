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

    private CancellationTokenSource? _diagnosticStop;
    private Task? _diagnosticTask;

    public async ValueTask InitializeAsync()
    {
        var diagnosticPath = Environment.GetEnvironmentVariable("MYERP_TEST_THREADPOOL_LOG");
        if (!string.IsNullOrWhiteSpace(diagnosticPath))
        {
            _diagnosticStop = new CancellationTokenSource();
            _diagnosticTask = RecordThreadPoolAsync(diagnosticPath, _diagnosticStop.Token);
        }

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

    public async ValueTask DisposeAsync()
    {
        if (_diagnosticStop is null || _diagnosticTask is null) return;
        await _diagnosticStop.CancelAsync().ConfigureAwait(false);
        await _diagnosticTask.ConfigureAwait(false);
        _diagnosticStop.Dispose();
    }

    private static async Task RecordThreadPoolAsync(string path, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
            ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
            var line = $"{DateTimeOffset.UtcNow:O},workers_busy={maxWorkers - availableWorkers},workers_max={maxWorkers},io_busy={maxIo - availableIo},io_max={maxIo},pending={ThreadPool.PendingWorkItemCount},completed={ThreadPool.CompletedWorkItemCount}{Environment.NewLine}";
            await File.AppendAllTextAsync(path, line, System.Text.Encoding.UTF8, CancellationToken.None).ConfigureAwait(false);
        } while (await WaitForTickAsync(timer, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<bool> WaitForTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
    }

    /// <summary>
    /// 令主機改連 myerp_test。TestServer 沒有連線 IP，所有請求落在同一個限流分區，故同時放寬按 IP 的登入類限流；
    /// 限流本身由 WebSecurityTests 以設定檔預設值驗證。
    /// </summary>
    public IWebHostBuilder UseTestDatabase(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}", ConnectionString)
            .UseSetting("Security:RateLimiting:Auth:PermitLimit", "100000")
            .UseSetting("Security:RateLimiting:Captcha:PermitLimit", "100000");
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
