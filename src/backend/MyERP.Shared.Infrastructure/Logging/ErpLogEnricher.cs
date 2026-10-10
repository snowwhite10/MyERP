using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Modules;
using MyERP.Shared.Security;
using Serilog.Core;
using Serilog.Events;

namespace MyERP.Shared.Infrastructure.Logging;

/// <summary>
/// 每筆日誌加上 <c>TraceId</c>、<c>UserId</c>、<c>CompanyId</c>、<c>Module</c>（MASTER_PROMPT 8.1）。
/// 在寫日誌當下才讀取，因此認證中介層（WU-SYS-003）設定的用戶也會出現在其後的日誌；已有同名屬性時不覆寫。
/// </summary>
public sealed class ErpLogEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    public const string TraceIdProperty = "TraceId";
    public const string UserIdProperty = "UserId";
    public const string CompanyIdProperty = "CompanyId";
    public const string ModuleProperty = "Module";

    private const string ModuleNamespacePrefix = "MyERP.Modules.";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var http = accessor.HttpContext;

        // 與 ProblemDetails 的 traceId（W3C traceparent，含本值）對得上；沒有 Activity 時退回請求識別碼
        var traceId = Activity.Current?.TraceId.ToHexString() ?? http?.TraceIdentifier;
        if (traceId is not null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(TraceIdProperty, traceId));
        }

        if (http is not null)
        {
            var (userId, companyId) = ReadContext(http);
            if (userId is not null)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(UserIdProperty, userId.Value));
            }

            if (companyId is not null)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(CompanyIdProperty, companyId.Value));
            }
        }

        var sourceContext = logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
            && value is ScalarValue { Value: string text }
                ? text
                : null;
        var module = ResolveModule(http?.Request.Path ?? PathString.Empty, sourceContext);
        if (module is not null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(ModuleProperty, module));
        }
    }

    /// <summary>
    /// 模組代碼：先看路由 <c>/api/v{n}/{前綴}/…</c>（api-conventions.md 第 2 節），再看記錄者命名空間 <c>MyERP.Modules.{專案}</c>；
    /// 都不符合時回傳 null（例如 /health 或共用基礎設施）。
    /// </summary>
    public static string? ResolveModule(PathString path, string? sourceContext)
    {
        var fromRoute = ModuleFromRoute(path);
        if (fromRoute is not null)
        {
            return fromRoute;
        }

        if (sourceContext is null || !sourceContext.StartsWith(ModuleNamespacePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var module in ErpModuleCatalog.All)
        {
            var project = module.ProjectName;
            if (sourceContext.StartsWith(project, StringComparison.Ordinal)
                && (sourceContext.Length == project.Length || sourceContext[project.Length] == '.'))
            {
                return module.Code;
            }
        }

        return null;
    }

    private static string? ModuleFromRoute(PathString path)
    {
        if (!path.HasValue)
        {
            return null;
        }

        var segments = path.Value!.Split('/', 4, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || segments[1].Length < 2
            || char.ToLowerInvariant(segments[1][0]) != 'v'
            || !segments[1][1..].All(char.IsAsciiDigit))
        {
            return null;
        }

        return ErpModuleCatalog.FindByPrefix(segments[2])?.Code;
    }

    private static (long? UserId, long? CompanyId) ReadContext(HttpContext http)
    {
        try
        {
            var services = http.RequestServices;
            if (services is null)
            {
                return (null, null);
            }

            return (services.GetService<ICurrentUser>()?.UserId, services.GetService<ICurrentCompany>()?.CompanyId);
        }
        catch (ObjectDisposedException)
        {
            // 請求範圍已結束（例如回應完成後的背景日誌）：不加用戶 / 公司
            return (null, null);
        }
    }
}
