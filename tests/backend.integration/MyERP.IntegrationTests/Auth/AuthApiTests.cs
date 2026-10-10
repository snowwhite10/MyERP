using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using Serilog.Core;
using Serilog.Events;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-AUTH-001～010：登入、Captcha、JWT、Refresh 輪替與登出（myerp_test + 本機 Redis）。</summary>
public sealed class AuthApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private AuthApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new AuthApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task LoginWithChallengeIssuesFifteenMinuteAccessTokenThatAuthenticates()
    {
        var (userId, username) = await CreateUserAsync();
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, username, Password, await NewChallengeAsync());

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = await ReadTokensAsync(login);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal(userId.ToString(System.Globalization.CultureInfo.InvariantCulture), jwt.Subject);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
        Assert.True(tokens.RefreshToken.Length >= 43);
        Assert.DoesNotContain(Password, await login.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sys/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var meResponse = await client.SendAsync(me, Ct);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var body = await meResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(userId, body.GetProperty("userId").GetInt64());
        Assert.Equal(username, body.GetProperty("username").GetString());
    }

    [Fact]
    public async Task CaptchaEndpointReturnsSvgAndChallengeIsSingleUseEvenWhenWrong()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/sys/auth/captcha", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("captchaId").GetString()));
        Assert.StartsWith("data:image/svg+xml;base64,", body.GetProperty("image").GetString(), StringComparison.Ordinal);
        Assert.Equal(5, body.GetProperty("length").GetInt32());
        Assert.False(body.TryGetProperty("code", out _));

        var (_, username) = await CreateUserAsync();
        var challenge = await NewChallengeAsync();
        var wrong = challenge with { Code = challenge.Code == "00000" ? "11111" : "00000" };
        var first = await LoginAsync(client, username, Password, wrong);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal("SYS.AUTH.CAPTCHA_INVALID", await ErrorCodeAsync(first));
        // 錯誤一次即作廢：同一題再輸入正確答案亦失敗，防止逐一猜測
        Assert.Equal("SYS.AUTH.CAPTCHA_INVALID", await ErrorCodeAsync(await LoginAsync(client, username, Password, challenge)));

        var reused = await NewChallengeAsync();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, Password, reused)).StatusCode);
        Assert.Equal("SYS.AUTH.CAPTCHA_INVALID", await ErrorCodeAsync(await LoginAsync(client, username, Password, reused)));
    }

    [Fact]
    public async Task MasterCodeWorksOnlyInDevelopmentAndWritesWarning()
    {
        var (_, username) = await CreateUserAsync();
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, username, Password, new CaptchaChallenge("not-issued", "123456", 120));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var warning = Assert.Single(factory.Sink.Events, e => e.Level == LogEventLevel.Warning &&
            e.MessageTemplate.Text.Contains("master code", StringComparison.OrdinalIgnoreCase) &&
            e.Properties.TryGetValue("Username", out var u) && u.ToString() == $"\"{username}\"");
        Assert.True(warning.Properties.ContainsKey("RemoteIp"));
        Assert.DoesNotContain(factory.Sink.Events, e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Contains(Password, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production", "123456", "0123456789abcdef0123456789abcdef", "Captcha:MasterCode")]
    [InlineData("Production", "", "", "Security:Jwt:SigningKey")]
    [InlineData("Production", "", "too-short", "Security:Jwt:SigningKey")]
    [InlineData("Development", "12", "", "Captcha:MasterCode")]
    public async Task UnsafeSecurityConfigurationRefusesStartup(string environment, string masterCode, string signingKey, string expectedKey)
    {
        var sink = new CollectingSink();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            database.UseTestDatabase(b);
            b.UseEnvironment(environment);
            b.UseSetting("Captcha:MasterCode", masterCode);
            b.UseSetting("Security:Jwt:SigningKey", signingKey);
            b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink));
        });

        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(expectedKey, ex.ToString(), StringComparison.Ordinal);
        if (expectedKey == "Captcha:MasterCode" && environment == "Production")
        {
            Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Error && e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Captcha:MasterCode", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task WrongPasswordAndUnknownUserShareGenericErrorAndLockedUserIsRejected()
    {
        var (_, username) = await CreateUserAsync();
        using var client = factory.CreateClient();

        var wrong = await LoginAsync(client, username, "Wrong pass 2026!", await NewChallengeAsync());
        var unknown = await LoginAsync(client, "nobody" + Guid.NewGuid().ToString("N"), Password, await NewChallengeAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal("SYS.AUTH.INVALID_CREDENTIALS", await ErrorCodeAsync(wrong));
        Assert.Equal("SYS.AUTH.INVALID_CREDENTIALS", await ErrorCodeAsync(unknown));

        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(client, username, "Wrong pass 2026!", await NewChallengeAsync());
        }
        var locked = await LoginAsync(client, username.ToUpperInvariant(), Password, await NewChallengeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("SYS.AUTH.INVALID_CREDENTIALS", await ErrorCodeAsync(locked));
    }

    [Fact]
    public async Task ExpiredPasswordIsRejectedBeforeTokensAreIssued()
    {
        var (_, username) = await CreateUserAsync(passwordChangedDaysAgo: 91);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, username, Password, await NewChallengeAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SYS.AUTH.PASSWORD_EXPIRED", await ErrorCodeAsync(response));
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshRotatesTokenAndOldTokenCannotBeReplayedConcurrently()
    {
        var (userId, username) = await CreateUserAsync();
        using var client = factory.CreateClient();
        var first = await ReadTokensAsync(await LoginAsync(client, username, Password, await NewChallengeAsync()));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync("/api/v1/sys/auth/refresh", new { refreshToken = first.RefreshToken }, Ct)));

        var winner = Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(attempts.Where(r => r != winner), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        var rotated = await ReadTokensAsync(winner);
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);
        Assert.Equal(userId.ToString(System.Globalization.CultureInfo.InvariantCulture), new JwtSecurityTokenHandler().ReadJwtToken(rotated.AccessToken).Subject);

        var replay = await client.PostAsJsonAsync("/api/v1/sys/auth/refresh", new { refreshToken = first.RefreshToken }, Ct);
        Assert.Equal("SYS.AUTH.REFRESH_INVALID", await ErrorCodeAsync(replay));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/sys/auth/refresh", new { refreshToken = rotated.RefreshToken }, Ct)).StatusCode);
    }

    [Fact]
    public async Task LogoutRequiresAccessTokenAndRevokesOwnRefreshTokenOnly()
    {
        var (_, username) = await CreateUserAsync();
        var (_, otherName) = await CreateUserAsync();
        using var client = factory.CreateClient();
        var mine = await ReadTokensAsync(await LoginAsync(client, username, Password, await NewChallengeAsync()));
        var other = await ReadTokensAsync(await LoginAsync(client, otherName, Password, await NewChallengeAsync()));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/sys/auth/logout", new { refreshToken = mine.RefreshToken }, Ct)).StatusCode);

        // 不得用自己的 Access Token 撤銷他人的 Refresh Token
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(client, mine.AccessToken, other.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/sys/auth/refresh", new { refreshToken = other.RefreshToken }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(client, mine.AccessToken, mine.RefreshToken)).StatusCode);
        var afterLogout = await client.PostAsJsonAsync("/api/v1/sys/auth/refresh", new { refreshToken = mine.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task InvalidRequestsAreValidatedAndTamperedTokensRejected()
    {
        using var client = factory.CreateClient();
        var missing = await client.PostAsJsonAsync("/api/v1/sys/auth/login", new { username = "", password = "", captchaId = "", captchaCode = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var badRefresh = await client.PostAsJsonAsync("/api/v1/sys/auth/refresh", new { refreshToken = "not-a-token" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, badRefresh.StatusCode);

        var (_, username) = await CreateUserAsync();
        var tokens = await ReadTokensAsync(await LoginAsync(client, username, Password, await NewChallengeAsync()));
        using var tampered = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sys/auth/me");
        tampered.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken[..^2] + "xx");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(tampered, Ct)).StatusCode);
    }

    [Fact]
    public async Task ReadinessIncludesRedis()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(long Id, string Username)> CreateUserAsync(int passwordChangedDaysAgo = 0)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SystemUserService>();
        var username = "auth" + Guid.NewGuid().ToString("N")[..20];
        var user = await service.CreateAsync(username, Password, Ct);
        if (passwordChangedDaysAgo > 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<MyERP.Shared.Infrastructure.Persistence.MyErpDbContext>();
            user.PasswordChangedAt = user.PasswordChangedAt.AddDays(-passwordChangedDaysAgo);
            await db.SaveChangesAsync(Ct);
        }
        return (user.Id, username);
    }

    private async Task<CaptchaChallenge> NewChallengeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password, CaptchaChallenge challenge) =>
        client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string accessToken, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sys/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Ct);
    }

    private static async Task<(string AccessToken, string RefreshToken)> ReadTokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private sealed class AuthApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        public CollectingSink Sink { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(Sink));
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> events = new();

        public IReadOnlyCollection<LogEvent> Events => events.ToArray();

        public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);
    }
}
