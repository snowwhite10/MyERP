using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using MyERP.Shared.Security;

namespace MyERP.Shared.Infrastructure.Security;

/// <summary>限流政策名稱，端點以 <c>[EnableRateLimiting(RateLimitPolicies.X)]</c> 套用。已登入請求另有每用戶的全域限流。</summary>
public static class RateLimitPolicies
{
    /// <summary>登入、註冊、忘記密碼、Token 刷新：每 IP 每端點。</summary>
    public const string Auth = "auth";

    /// <summary>Captcha：每 IP。</summary>
    public const string Captcha = "captcha";

    /// <summary>匯出報表：每用戶。</summary>
    public const string Export = "export";

    /// <summary>按 IP 分區的政策；匿名端點必須套用其中之一（ArchitectureTests 檢查）。</summary>
    public static readonly IReadOnlySet<string> PerIp = new HashSet<string>(StringComparer.Ordinal) { Auth, Captcha };
}

/// <summary>
/// 安全中介層（MASTER_PROMPT 第 7 節、architecture/security.md）：ForwardedHeaders、HTTPS/HSTS、安全標頭、CORS、Rate limiting。
/// </summary>
public static partial class WebSecurityExtensions
{
    public const string CorsPolicyName = "erp-frontend";

    /// <summary>API 只回傳 JSON，不載入任何資源，也不允許被嵌入框架。前端站台的 CSP 見 architecture/security.md。</summary>
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    private static readonly string[] AllowedHeaders = ["Authorization", "Content-Type", "Accept", "Accept-Language", "X-Company-Id"];
    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly string[] ExposedHeaders = [HeaderNames.RetryAfter, HeaderNames.ContentDisposition];

    public static IServiceCollection AddMyErpWebSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = WebSecurityOptions.FromConfiguration(configuration);
        services.AddSingleton(options);

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = options.ForwardLimit;
            // 預設信任 loopback，清空後只信任設定中列明的 Proxy
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var proxy in options.KnownProxies)
            {
                o.KnownProxies.Add(proxy);
            }

            foreach (var network in options.KnownNetworks)
            {
                o.KnownIPNetworks.Add(network);
            }
        });

        services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(options.HstsMaxAgeDays);
            o.IncludeSubDomains = true;
        });

        // 沒有設定來源時不允許任何跨來源請求；Token 走 Authorization 標頭，不需要 Cookie（不 AllowCredentials）
        services.AddCors(o => o.AddPolicy(CorsPolicyName, policy => policy
            .WithOrigins([.. options.AllowedOrigins])
            .WithHeaders(AllowedHeaders)
            .WithMethods(AllowedMethods)
            .WithExposedHeaders(ExposedHeaders)
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = OnRejected;
            o.AddPolicy(RateLimitPolicies.Auth, http => FixedWindow($"auth|{ClientIp(http)}|{http.Request.Path.Value?.ToUpperInvariant()}", options.Auth));
            o.AddPolicy(RateLimitPolicies.Captcha, http => FixedWindow($"captcha|{ClientIp(http)}", options.Captcha));
            o.AddPolicy(RateLimitPolicies.Export, http => FixedWindow(
                UserId(http) is { } id ? $"export|user:{id}" : $"export|ip:{ClientIp(http)}", options.Export));
            // 已登入請求每用戶限流；匿名請求只受端點政策限制（匿名端點都必須套用按 IP 的政策）
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => UserId(http) is { } id
                ? FixedWindow($"user:{id}", options.User)
                : RateLimitPartition.GetNoLimiter("anonymous"));
        });

        return services;
    }

    /// <summary>放在管線最前面：之後的請求日誌、限流、登入記錄都使用真實客戶端 IP。未設定 Proxy 時不啟用。</summary>
    public static IApplicationBuilder UseMyErpForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.ApplicationServices.GetRequiredService<WebSecurityOptions>().TrustsProxies ? app.UseForwardedHeaders() : app;
    }

    /// <summary>安全標頭於回應開始時寫入，例外處理清除標頭後產生的錯誤回應也會帶上。</summary>
    public static IApplicationBuilder UseMyErpSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpContext)state).Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                return Task.CompletedTask;
            }, context);
            return next(context);
        });
    }

    /// <summary>非 Development：HSTS + HTTP 轉 HTTPS。Development 不啟用，避免瀏覽器記住 localhost 只能用 HTTPS。</summary>
    public static IApplicationBuilder UseMyErpTransportSecurity(this IApplicationBuilder app, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        return app;
    }

    public static IApplicationBuilder UseMyErpCors(this IApplicationBuilder app) => app.UseCors(CorsPolicyName);

    private static RateLimitPartition<string> FixedWindow(string key, RateLimitRule rule) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rule.PermitLimit,
            Window = rule.Window,
            QueueLimit = 0,
        });

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static long? UserId(HttpContext http) => http.RequestServices.GetService<ICurrentUser>()?.UserId;

    // 只設定 Retry-After 與寫日誌，不寫 Body：由外層 StatusCodePages 產生 ProblemDetails（errorCode COMMON.REQUEST.RATE_LIMITED）
    private static ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WebSecurityExtensions));
        LogRateLimited(logger, http.Request.Method, http.Request.Path.Value ?? "", ClientIp(http), UserId(http));
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded for {RequestMethod} {RequestPath} from {RemoteIp} (user {RateLimitedUserId})")]
    private static partial void LogRateLimited(ILogger logger, string requestMethod, string requestPath, string remoteIp, long? rateLimitedUserId);
}
