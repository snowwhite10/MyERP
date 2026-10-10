using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Infrastructure.Web;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Shared.Infrastructure;

public static class CoreServiceCollectionExtensions
{
    public const string TimeZoneOffsetKey = "App:TimeZoneOffset";
    public const string DefaultTimeZoneOffset = "+08:00";

    /// <summary>
    /// 共用核心服務：IClock（單例，偏移讀 <c>App:TimeZoneOffset</c>，格式錯誤時啟動即失敗）、
    /// ICurrentUser / ICurrentCompany（scoped，取自 HTTP 請求）、ProblemDetails 全域例外處理。
    /// </summary>
    public static IServiceCollection AddMyErpCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var offsetText = configuration[TimeZoneOffsetKey] ?? DefaultTimeZoneOffset;
        TimeSpan offset;
        try
        {
            offset = SystemClock.ParseOffset(offsetText);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Configuration '{TimeZoneOffsetKey}' is invalid: {ex.Message}", ex);
        }

        services.AddSingleton<IClock>(new SystemClock(TimeProvider.System, offset));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ICurrentCompany, HttpCurrentCompany>();
        services.AddErpProblemDetails();
        return services;
    }
}
