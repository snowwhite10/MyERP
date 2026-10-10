using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace MyERP.Shared.Infrastructure.Logging;

public static class ErpLoggingExtensions
{
    /// <summary>請求日誌的訊息範本（只含路徑，不含查詢字串，避免把 Token 等參數寫入日誌）。</summary>
    public const string RequestMessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    /// <summary>
    /// 以 Serilog 取代預設日誌提供者（logging.md）：滾動檔案（CLEF JSON，保留 <see cref="ErpLoggingOptions.RetainedDays"/> 天）、
    /// 可選 Application Insights（有連線字串才啟用）、Development 主控台；每筆日誌經 <see cref="ErpLogEnricher"/> 加上
    /// TraceId / UserId / CompanyId / Module。DI 內註冊的 <see cref="ILogEventSink"/> / <see cref="ILogEventEnricher"/> 也會套用（測試用來收集日誌）。
    /// </summary>
    public static IServiceCollection AddMyErpLogging(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = ErpLoggingOptions.FromConfiguration(configuration);
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddSingleton<ILogEventEnricher, ErpLogEnricher>();

        if (options.ApplicationInsightsConnectionString is not null)
        {
            // 由容器擁有並在關機時釋放（送出緩衝中的遙測）
            services.AddSingleton(_ => new TelemetryConfiguration { ConnectionString = options.ApplicationInsightsConnectionString });
        }

        // preserveStaticLogger：不改寫全域 Log.Logger，同一程序內多個主機（整合測試）互不干擾
        services.AddSerilog(
            (provider, logger) => Configure(logger, provider, configuration, environment, options),
            preserveStaticLogger: true);
        return services;
    }

    /// <summary>請求日誌：回應時間超過門檻寫 Warning，其餘 Debug。須放在管線最外層，才記錄到 ProblemDetails 之後的最終狀態碼。</summary>
    public static IApplicationBuilder UseMyErpRequestLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetRequiredService<ErpLoggingOptions>();
        var logger = app.ApplicationServices.GetRequiredService<ILogger>();
        return app.UseSerilogRequestLogging(o =>
        {
            o.Logger = logger;
            o.MessageTemplate = RequestMessageTemplate;
            o.GetLevel = (_, elapsedMilliseconds, _) => GetRequestLevel(elapsedMilliseconds, options.SlowRequestMilliseconds);
        });
    }

    /// <summary>超過（不含等於）門檻為 Warning（MASTER_PROMPT 8.1、REQ_NF421.3），否則 Debug。例外與 5xx 由 ErpExceptionHandler 記錄 Error，這裡不重複。</summary>
    public static LogEventLevel GetRequestLevel(double elapsedMilliseconds, int slowRequestMilliseconds) =>
        elapsedMilliseconds > slowRequestMilliseconds ? LogEventLevel.Warning : LogEventLevel.Debug;

    /// <summary>相對路徑以內容根目錄為準，避免依啟動時的工作目錄寫到不同位置。</summary>
    public static string ResolveFilePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(contentRootPath, path));

    private static void Configure(
        LoggerConfiguration logger,
        IServiceProvider provider,
        IConfiguration configuration,
        IHostEnvironment environment,
        ErpLoggingOptions options)
    {
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(provider)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", environment.ApplicationName);

        if (options.ConsoleEnabled ?? environment.IsDevelopment())
        {
            logger.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Module}] {Message:lj} {{TraceId={TraceId} UserId={UserId} CompanyId={CompanyId}}}{NewLine}{Exception}",
                formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        }

        if (options.FileEnabled)
        {
            logger.WriteTo.File(
                new CompactJsonFormatter(),
                ResolveFilePath(options.FilePath, environment.ContentRootPath),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(options.RetainedDays),
                fileSizeLimitBytes: 100L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true);
        }

        var telemetry = provider.GetService<TelemetryConfiguration>();
        if (telemetry is not null)
        {
            logger.WriteTo.ApplicationInsights(telemetry, TelemetryConverter.Traces);
        }
    }
}
