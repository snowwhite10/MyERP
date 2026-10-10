using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Infrastructure.Logging;
using MyERP.Shared.Infrastructure.Web;
using MyERP.Shared.Security;
using Serilog.Core;
using Serilog.Events;

namespace MyERP.IntegrationTests.Logging;

/// <summary>Serilog 整合：enricher 欄位、慢 API Warning、滾動檔案（logging.md、MASTER_PROMPT 8.1）。</summary>
public sealed class LoggingTests
{
    private const string RequestLoggerName = "Serilog.AspNetCore.RequestLoggingMiddleware";

    [Fact]
    public async Task SlowRequest_WritesWarningWithTraceUserCompany()
    {
        await using var factory = new LoggingApiFactory(slowRequestMilliseconds: 0);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(LoggingApiFactory.TestUserHeader, "42");
        request.Headers.Add(HttpCurrentCompany.HeaderName, "7");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logEvent = await factory.Sink.RequestEventAsync("/health/live");
        logEvent.Level.Should().Be(LogEventLevel.Warning);
        Scalar(logEvent, ErpLogEnricher.UserIdProperty).Should().Be(42L);
        Scalar(logEvent, ErpLogEnricher.CompanyIdProperty).Should().Be(7L);
        Scalar(logEvent, ErpLogEnricher.TraceIdProperty).Should().BeOfType<string>().Which.Should().NotBeEmpty();
        Scalar(logEvent, "StatusCode").Should().Be(200);
    }

    [Fact]
    public async Task FastRequest_IsDebugNotWarning()
    {
        await using var factory = new LoggingApiFactory(slowRequestMilliseconds: 60_000);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.Sink.RequestEventAsync("/health/live")).Level.Should().Be(LogEventLevel.Debug);
        factory.Sink.Events.Should().NotContain(e => e.Level >= LogEventLevel.Warning);
    }

    [Fact]
    public async Task AnonymousRequest_OmitsUserAndCompany()
    {
        await using var factory = new LoggingApiFactory(slowRequestMilliseconds: 0);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        var logEvent = await factory.Sink.RequestEventAsync("/health/live");
        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.UserIdProperty);
        logEvent.Properties.Should().NotContainKey(ErpLogEnricher.CompanyIdProperty);
    }

    [Fact]
    public async Task ModuleRoute_AddsModuleAndTraceIdMatchesProblemDetails()
    {
        await using var factory = new LoggingApiFactory(slowRequestMilliseconds: 0);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/sal/does-not-exist?token=secret", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var logEvent = await factory.Sink.RequestEventAsync("/api/v1/sal/does-not-exist");
        Scalar(logEvent, ErpLogEnricher.ModuleProperty).Should().Be("SD");
        Scalar(logEvent, "StatusCode").Should().Be(404);
        problem.GetProperty("traceId").GetString().Should().Contain((string)Scalar(logEvent, ErpLogEnricher.TraceIdProperty)!);
        logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Should().NotContain("secret", "query strings may carry tokens");
    }

    [Fact]
    public async Task FileSink_WritesStructuredJsonWithEnricherFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "myerp-log-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var factory = new LoggingApiFactory(slowRequestMilliseconds: 0, logDirectory: directory))
            {
                using var client = factory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
                request.Headers.Add(LoggingApiFactory.TestUserHeader, "42");
                request.Headers.Add(HttpCurrentCompany.HeaderName, "7");
                using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                await factory.Sink.RequestEventAsync("/health/live");
            }

            var files = Directory.GetFiles(directory, "app-*.log");
            files.Should().ContainSingle();
            var line = File.ReadLines(files[0]).Select(l => JsonDocument.Parse(l).RootElement)
                .Single(e => e.TryGetProperty("RequestPath", out var p) && p.GetString() == "/health/live");
            line.GetProperty("@l").GetString().Should().Be("Warning");
            line.GetProperty(ErpLogEnricher.UserIdProperty).GetInt64().Should().Be(42);
            line.GetProperty(ErpLogEnricher.CompanyIdProperty).GetInt64().Should().Be(7);
            line.GetProperty(ErpLogEnricher.TraceIdProperty).GetString().Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static object? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) ? ((ScalarValue)value).Value : null;

    /// <summary>收集日誌的主機；標頭 <c>X-Test-User</c> 模擬已登入用戶（認證於 WU-SYS-003 加入）。</summary>
    private sealed class LoggingApiFactory(int slowRequestMilliseconds, string? logDirectory = null) : WebApplicationFactory<Program>
    {
        public const string TestUserHeader = "X-Test-User";

        public CollectingSink Sink { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(ErpLoggingOptions.SlowRequestMillisecondsKey, slowRequestMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Serilog:MinimumLevel:Default", "Debug");
            builder.UseSetting(ErpLoggingOptions.FileEnabledKey, logDirectory is null ? "false" : "true");
            if (logDirectory is not null)
            {
                builder.UseSetting(ErpLoggingOptions.FilePathKey, Path.Combine(logDirectory, "app-.log"));
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILogEventSink>(Sink);
                services.AddSingleton<IStartupFilter, TestUserStartupFilter>();
                // 模擬用戶沒有資料庫記錄；本測試只驗證日誌欄位，公司存取檢查（WU-SYS-004）以替身放行
                services.AddScoped<IUserAuthorizationStore, AllowAllCompaniesStore>();
            });
        }
    }

    private sealed class AllowAllCompaniesStore : IUserAuthorizationStore
    {
        public Task<UserCompanyAuthorization?> GetAsync(long userId, long companyId, CancellationToken cancellationToken) =>
            Task.FromResult<UserCompanyAuthorization?>(new UserCompanyAuthorization(userId, companyId, null, null, false, new Dictionary<string, DataScope>()));
    }

    private sealed class TestUserStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(LoggingApiFactory.TestUserHeader, out var userId))
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "Test"));
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> events = new();

        public IReadOnlyCollection<LogEvent> Events => events.ToArray();

        public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);

        /// <summary>請求日誌在管線返回後才寫入，可能晚於用戶端收到回應，因此最多等 5 秒。</summary>
        public async Task<LogEvent> RequestEventAsync(string path)
        {
            for (var attempt = 0; attempt < 250; attempt++)
            {
                var matches = Events.Where(e => IsRequestEvent(e, path)).ToList();
                if (matches.Count > 0)
                {
                    return matches.Should().ContainSingle().Subject;
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException($"No request log for {path}.");
        }

        private static bool IsRequestEvent(LogEvent e, string path) =>
            e.Properties.TryGetValue("SourceContext", out var source) && source is ScalarValue { Value: RequestLoggerName }
            && e.Properties.TryGetValue("RequestPath", out var p) && p is ScalarValue { Value: string value } && value == path;
    }
}
