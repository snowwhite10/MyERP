using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;

namespace MyERP.IntegrationTests.Security;

/// <summary>SYS-SEC-001～010：安全標頭、CORS、HTTPS/HSTS、Rate limiting、ForwardedHeaders（WU-SYS-005）。</summary>
public sealed class WebSecurityTests(TestDatabase database)
{
    private const string AllowedOrigin = "https://erp.example.com";
    private const string LoginPath = "/api/v1/sys/auth/login";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SecurityHeadersArePresentOnSuccessAndErrorResponses()
    {
        await using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/health/live", "/api/v1/not-a-route" })
        {
            var response = await client.GetAsync(path, Ct);

            Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
            Assert.Equal("DENY", Header(response, "X-Frame-Options"));
            Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
            var csp = Header(response, "Content-Security-Policy");
            Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
            Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
            // Development 不送 HSTS，避免本機瀏覽器記住 localhost 只能用 HTTPS
            Assert.Null(Header(response, "Strict-Transport-Security"));
        }
    }

    [Fact]
    public async Task CorsPreflightAllowsConfiguredOriginOnlyWithoutCredentials()
    {
        await using var factory = new SecurityApiFactory(database, settings: new() { ["Security:Cors:AllowedOrigins:0"] = AllowedOrigin });
        using var client = factory.CreateClient();

        var allowed = await client.SendAsync(Preflight(AllowedOrigin), Ct);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal(AllowedOrigin, Header(allowed, "Access-Control-Allow-Origin"));
        var allowedHeaders = Header(allowed, "Access-Control-Allow-Headers") ?? "";
        Assert.Contains("x-company-id", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("authorization", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Header(allowed, "Access-Control-Allow-Credentials"));

        var rejected = await client.SendAsync(Preflight("https://evil.example.com"), Ct);
        Assert.Null(Header(rejected, "Access-Control-Allow-Origin"));

        using var actual = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        actual.Headers.Add("Origin", AllowedOrigin);
        var response = await client.SendAsync(actual, Ct);
        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("Retry-After", Header(response, "Access-Control-Expose-Headers") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Security:Cors:AllowedOrigins:0", "*", "Security:Cors:AllowedOrigins")]
    [InlineData("Security:Cors:AllowedOrigins:0", "https://erp.example.com/app", "Security:Cors:AllowedOrigins")]
    [InlineData("Security:Cors:AllowedOrigins:0", "erp.example.com", "Security:Cors:AllowedOrigins")]
    [InlineData("Security:ForwardedHeaders:KnownProxies:0", "not-an-ip", "Security:ForwardedHeaders:KnownProxies")]
    [InlineData("Security:ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99", "Security:ForwardedHeaders:KnownNetworks")]
    [InlineData("Security:RateLimiting:Auth:PermitLimit", "0", "Security:RateLimiting:Auth")]
    public async Task InvalidSecurityConfigurationRefusesStartup(string key, string value, string expectedKey)
    {
        await using var factory = new SecurityApiFactory(database, settings: new() { [key] = value });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(expectedKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionRedirectsHttpToHttpsAndSendsHsts()
    {
        await using var factory = new SecurityApiFactory(database, "Production", new()
        {
            ["https_port"] = "443",
            ["Security:Jwt:SigningKey"] = "0123456789abcdef0123456789abcdef",
        });

        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var redirect = await http.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode);
        Assert.Equal("https://localhost/health/live", redirect.Headers.Location?.ToString());

        using var https = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(AllowedOrigin), AllowAutoRedirect = false });
        var secure = await https.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
        var hsts = Header(secure, "Strict-Transport-Security") ?? "";
        Assert.Contains("max-age=31536000", hsts, StringComparison.Ordinal);
        Assert.Contains("includeSubDomains", hsts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData("/api/v1/sys/auth/refresh")]
    public async Task AuthEndpointAllowsFiveRequestsPerMinutePerIpThenReturns429(string path)
    {
        await using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostEmptyAsync(client, path, "203.0.113.10")).StatusCode);
        }

        var limited = await PostEmptyAsync(client, path, "203.0.113.10", language: "en");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta is { TotalSeconds: > 0 and <= 60 });
        var body = await limited.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("COMMON.REQUEST.RATE_LIMITED", body.GetProperty("errorCode").GetString());
        Assert.Equal("https://myerp.local/errors/rate-limited", body.GetProperty("type").GetString());
        Assert.Equal("Too many requests", body.GetProperty("title").GetString());
        Assert.Equal("nosniff", Header(limited, "X-Content-Type-Options"));

        // 其他 IP 與同一 IP 的其他端點各自計數
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostEmptyAsync(client, path, "203.0.113.11")).StatusCode);
        var otherPath = path == LoginPath ? "/api/v1/sys/auth/refresh" : LoginPath;
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostEmptyAsync(client, otherPath, "203.0.113.10")).StatusCode);
    }

    [Fact]
    public async Task PreflightDoesNotConsumeQuotaAndRejectionIsReadableCrossOrigin()
    {
        await using var factory = new SecurityApiFactory(database, settings: new() { ["Security:Cors:AllowedOrigins:0"] = AllowedOrigin });
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var preflight = Preflight(AllowedOrigin);
            preflight.Headers.Add(RemoteIpStartupFilter.HeaderName, "203.0.113.30");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(preflight, Ct)).StatusCode);
        }

        for (var i = 0; i < 5; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostEmptyAsync(client, LoginPath, "203.0.113.30")).StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath) { Content = JsonContent.Create(new { }) };
        request.Headers.Add(RemoteIpStartupFilter.HeaderName, "203.0.113.30");
        request.Headers.Add("Origin", AllowedOrigin);
        var limited = await client.SendAsync(request, Ct);

        // 瀏覽器要讀得到 429 的 ProblemDetails 與 Retry-After，前端才能顯示「請稍後再試」
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(AllowedOrigin, Header(limited, "Access-Control-Allow-Origin"));
        Assert.Contains("Retry-After", Header(limited, "Access-Control-Expose-Headers") ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task CaptchaAllowsTenRequestsPerMinutePerIp()
    {
        await using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await GetFromAsync(client, "/api/v1/sys/auth/captcha", "203.0.113.20")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetFromAsync(client, "/api/v1/sys/auth/captcha", "203.0.113.20")).StatusCode);
    }

    [Fact]
    public async Task ForwardedForIsHonouredOnlyFromConfiguredProxy()
    {
        await using var factory = new SecurityApiFactory(database, settings: new() { ["Security:ForwardedHeaders:KnownProxies:0"] = "10.0.0.1" });
        using var client = factory.CreateClient();

        // 經設定的 Proxy：每個 X-Forwarded-For 是不同客戶端，各自計數
        for (var i = 1; i <= 6; i++)
        {
            var viaProxy = await PostEmptyAsync(client, LoginPath, "10.0.0.1", forwardedFor: $"198.51.100.{i}");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, viaProxy.StatusCode);
        }

        for (var i = 0; i < 5; i++)
        {
            await PostEmptyAsync(client, LoginPath, "10.0.0.1", forwardedFor: "198.51.100.50");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostEmptyAsync(client, LoginPath, "10.0.0.1", forwardedFor: "198.51.100.50")).StatusCode);

        // 未設定的來源偽造 X-Forwarded-For：仍以連線 IP 計數
        for (var i = 1; i <= 5; i++)
        {
            await PostEmptyAsync(client, LoginPath, "10.0.0.9", forwardedFor: $"192.0.2.{i}");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostEmptyAsync(client, LoginPath, "10.0.0.9", forwardedFor: "192.0.2.99")).StatusCode);
    }

    [Fact]
    public async Task ForwardedForIsIgnoredWhenNoProxyIsConfigured()
    {
        await using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();

        for (var i = 1; i <= 5; i++)
        {
            await PostEmptyAsync(client, LoginPath, "10.0.0.1", forwardedFor: $"198.51.100.{i}");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostEmptyAsync(client, LoginPath, "10.0.0.1", forwardedFor: "198.51.100.99")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequestsAreLimitedPerUser()
    {
        await using var factory = new SecurityApiFactory(database, settings: new() { ["Security:RateLimiting:User:PermitLimit"] = "3" });
        var first = await SignInAsync(factory);
        var second = await SignInAsync(factory);
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await MeAsync(client, first)).StatusCode);
        }

        var limited = await MeAsync(client, first);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(client, second)).StatusCode);
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, LoginPath);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-company-id,accept-language");
        return request;
    }

    private static Task<HttpResponseMessage> PostEmptyAsync(HttpClient client, string path, string remoteIp, string? forwardedFor = null, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { }) };
        request.Headers.Add(RemoteIpStartupFilter.HeaderName, remoteIp);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        return client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> GetFromAsync(HttpClient client, string path, string remoteIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(RemoteIpStartupFilter.HeaderName, remoteIp);
        return client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> MeAsync(HttpClient client, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sys/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Ct);
    }

    private static async Task<string> SignInAsync(SecurityApiFactory factory)
    {
        const string password = "Strong pass 2026!";
        await using var scope = factory.Services.CreateAsyncScope();
        var username = "sec" + Guid.NewGuid().ToString("N")[..20];
        await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, LoginPath)
        {
            Content = JsonContent.Create(new { username, password, captchaId = challenge.Id, captchaCode = challenge.Code }),
        };
        request.Headers.Add(RemoteIpStartupFilter.HeaderName, "203.0.113." + Random.Shared.Next(100, 250).ToString(CultureInfo.InvariantCulture));
        var login = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("accessToken").GetString()!;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : null;

    /// <summary>
    /// 正式限流設定的主機（UseTestDatabase 為其他測試放寬的登入限流在此還原為設定檔預設值）；
    /// 測試標頭 <see cref="RemoteIpStartupFilter.HeaderName"/> 模擬連線 IP（TestServer 沒有連線 IP）。
    /// </summary>
    private sealed class SecurityApiFactory(TestDatabase database, string environment = "Development", Dictionary<string, string?>? settings = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseEnvironment(environment);
            builder.UseSetting("App:Logging:FileEnabled", "false");
            builder.UseSetting("Security:RateLimiting:Auth:PermitLimit", "5");
            builder.UseSetting("Security:RateLimiting:Captcha:PermitLimit", "10");
            foreach (var (key, value) in settings ?? [])
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
        }
    }

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public const string HeaderName = "X-Test-Remote-Ip";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
