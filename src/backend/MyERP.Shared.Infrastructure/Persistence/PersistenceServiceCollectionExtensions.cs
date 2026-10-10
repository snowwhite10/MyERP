using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyERP.Shared.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "MyErp";

    /// <summary>SQL Server + snake_case 命名（db-conventions.md）。正式程式與測試共用，確保模型一致。</summary>
    public static TBuilder UseMyErpSqlServer<TBuilder>(this TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly("MyERP.Migrations"))
            .UseSnakeCaseNamingConvention();
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
        services.AddDbContext<MyErpDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
            }

            options
                .UseMyErpSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        return services;
    }
}
