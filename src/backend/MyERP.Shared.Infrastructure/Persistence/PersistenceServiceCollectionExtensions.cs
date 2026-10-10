using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Auditing;
using MyERP.Shared.Infrastructure.Auditing;

namespace MyERP.Shared.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "MyErp";

    public const string MigrationsAssemblyName = "MyERP.Migrations";

    /// <summary>
    /// SQL Server + snake_case 命名（db-conventions.md）+ 依模組組合快取模型。正式程式、設計期（dotnet ef）與測試共用，確保模型一致。
    /// </summary>
    public static TBuilder UseMyErpSqlServer<TBuilder>(this TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(MigrationsAssemblyName))
            .UseSnakeCaseNamingConvention()
            // 連線字串啟用 MARS，EF 本就不建立自動 savepoint；交易內的 SaveChanges（含稽核交易）不需每次寫 Warning（ADR-019）
            .ConfigureWarnings(w => w.Ignore(SqlServerEventId.SavepointsDisabledBecauseOfMARS))
            .ReplaceService<IModelCacheKeyFactory, ErpModelCacheKeyFactory>();
        return builder;
    }

    /// <summary>
    /// 註冊 scoped <see cref="MyErpDbContext"/> 與審計 interceptor。連線字串 <c>ConnectionStrings:MyErp</c>
    /// 在第一次解析 DbContext 時才讀取（缺少時拋出），因此不需資料庫的程序（例如健康檢查測試）可正常啟動。
    /// </summary>
    public static IServiceCollection AddMyErpPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<AuditStamper>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        // 稽核日誌 sys_audit_log（WU-SYS-007、ADR-019）：蓋章之後才記錄差異
        services.AddSingleton<IModuleDbModel, AuditLogDbModel>();
        services.AddScoped<AuditTrail>();
        services.AddScoped<IAuditTrail>(sp => sp.GetRequiredService<AuditTrail>());
        services.AddScoped<AuditTrailInterceptor>();
        services.AddDbContext<MyErpDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
            }

            options
                .UseMyErpSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>(), sp.GetRequiredService<AuditTrailInterceptor>());
        });
        return services;
    }
}
