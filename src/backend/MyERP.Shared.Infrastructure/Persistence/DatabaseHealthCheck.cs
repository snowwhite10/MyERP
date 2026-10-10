using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// 就緒檢查（<c>/health/ready</c>）：資料庫可連線。回應只有狀態文字，不含例外內容（避免洩漏連線資訊）。
/// DbContext 在檢查內才解析：連線字串未設定時建立 DbContext 即拋出，若以建構子注入，例外會在檢查外拋出而回 500 而非 503。
/// 不使用 HealthChecks.EntityFrameworkCore 套件（不在 ADR-001 清單）。
/// </summary>
public sealed class DatabaseHealthCheck(IServiceProvider services) : IHealthCheck
{
    public const string Name = "database";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        MyErpDbContext db;
        try
        {
            db = services.GetRequiredService<MyErpDbContext>();
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy("Database is not configured.", ex);
        }

        return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database is unreachable.");
    }
}
