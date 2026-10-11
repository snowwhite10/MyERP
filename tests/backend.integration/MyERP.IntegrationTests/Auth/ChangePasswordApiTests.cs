using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-AUTH-018～022：本人修改密碼、到期密碼修改、修改後撤銷 Refresh Token（myerp_test + 本機 Redis）。</summary>
public sealed class ChangePasswordApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string NewPassword = "Another pass 2027?";
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task ChangePasswordReplacesPasswordRevokesOtherSessionsAndReturnsNewTokens()
    {
        var (userId, username) = await CreateUserAsync();
        using var client = factory.CreateClient();
        var current = await ReadTokensAsync(await LoginAsync(client, username, Password));
        var otherSession = await ReadTokensAsync(await LoginAsync(client, username, Password));

        var response = await ChangeAsync(client, current.AccessToken, new { currentPassword = Password, newPassword = NewPassword, refreshToken = current.RefreshToken });

        var renewed = await ReadTokensAsync(response);
        Assert.DoesNotContain(NewPassword, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("SYS.AUTH.REFRESH_INVALID", await ErrorCodeAsync(await RefreshAsync(client, otherSession.RefreshToken)));
        Assert.Equal("SYS.AUTH.REFRESH_INVALID", await ErrorCodeAsync(await RefreshAsync(client, current.RefreshToken)));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, renewed.RefreshToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, NewPassword)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var events = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.UserId == userId && e.Action == "PASSWORD_CHANGE").ToListAsync(Ct);
        var audit = Assert.Single(events);
        Assert.Equal("SECURITY", audit.Category);
        Assert.Equal(username, audit.Username);
        Assert.DoesNotContain(NewPassword, audit.Changes ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(Password, audit.Changes ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePasswordRejectsWrongCurrentReusedAndWeakPasswords()
    {
        var (userId, username) = await CreateUserAsync();
        using var client = factory.CreateClient();
        var tokens = await ReadTokensAsync(await LoginAsync(client, username, Password));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/sys/auth/change-password",
            new { currentPassword = Password, newPassword = NewPassword }, Ct)).StatusCode);

        var wrong = await ChangeAsync(client, tokens.AccessToken, new { currentPassword = "Wrong pass 2026!", newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("SYS.AUTH.CURRENT_PASSWORD_INVALID", await ErrorCodeAsync(wrong));

        var reused = await ChangeAsync(client, tokens.AccessToken, new { currentPassword = Password, newPassword = Password });
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal("SYS.AUTH.PASSWORD_REUSED", await ErrorCodeAsync(reused));

        var weak = await ChangeAsync(client, tokens.AccessToken, new { currentPassword = Password, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var weakBody = await ReadAsync(weak);
        Assert.Equal("COMMON.REQUEST.VALIDATION_FAILED", weakBody.GetProperty("errorCode").GetString());
        Assert.True(weakBody.GetProperty("errors").TryGetProperty("newPassword", out _));

        var missing = await ChangeAsync(client, tokens.AccessToken, new { currentPassword = "", newPassword = "" });
        Assert.Equal("COMMON.REQUEST.VALIDATION_FAILED", await ErrorCodeAsync(missing));

        // 錯誤的目前密碼計入失敗次數（防止以已登入 Token 暴力猜測）；其餘驗證失敗不計
        var user = await FindUserAsync(userId);
        Assert.Equal(1, user.FailedLoginCount);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, tokens.RefreshToken)).StatusCode);

        // 目前密碼錯誤寫一筆安全稽核（與登入失敗一致），不含任何密碼
        await using var scope = factory.Services.CreateAsyncScope();
        var failed = Assert.Single(await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.UserId == userId && e.Action == "LOGIN_FAILED").ToListAsync(Ct));
        Assert.Equal("SECURITY", failed.Category);
        Assert.Equal(username, failed.Username);
        Assert.Contains("CURRENT_PASSWORD_INVALID", failed.Changes ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("Wrong pass 2026!", failed.Changes ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiveWrongCurrentPasswordsLockTheAccount()
    {
        var (userId, username) = await CreateUserAsync();
        using var client = factory.CreateClient();
        var tokens = await ReadTokensAsync(await LoginAsync(client, username, Password));

        for (var i = 0; i < 5; i++)
        {
            await ChangeAsync(client, tokens.AccessToken, new { currentPassword = "Wrong pass 2026!", newPassword = NewPassword });
        }

        Assert.NotNull((await FindUserAsync(userId)).LockoutEnd);
        var blocked = await ChangeAsync(client, tokens.AccessToken, new { currentPassword = Password, newPassword = NewPassword });
        Assert.Equal("SYS.AUTH.CURRENT_PASSWORD_INVALID", await ErrorCodeAsync(blocked));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, tokens.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ExpiredPasswordCanBeChangedWithCaptchaAndThenSignsIn()
    {
        var (userId, username) = await CreateUserAsync(passwordChangedDaysAgo: 91);
        using var client = factory.CreateClient();
        Assert.Equal("SYS.AUTH.PASSWORD_EXPIRED", await ErrorCodeAsync(await LoginAsync(client, username, Password)));

        var challenge = await NewChallengeAsync();
        var badCaptcha = await ChangeExpiredAsync(client, username, Password, NewPassword, challenge with { Code = challenge.Code == "00000" ? "11111" : "00000" });
        Assert.Equal("SYS.AUTH.CAPTCHA_INVALID", await ErrorCodeAsync(badCaptcha));

        var wrong = await ChangeExpiredAsync(client, username, "Wrong pass 2026!", NewPassword, await NewChallengeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("SYS.AUTH.INVALID_CREDENTIALS", await ErrorCodeAsync(wrong));
        var unknown = await ChangeExpiredAsync(client, "nobody" + Guid.NewGuid().ToString("N")[..12], Password, NewPassword, await NewChallengeAsync());
        Assert.Equal("SYS.AUTH.INVALID_CREDENTIALS", await ErrorCodeAsync(unknown));

        var reused = await ChangeExpiredAsync(client, username, Password, Password, await NewChallengeAsync());
        Assert.Equal("SYS.AUTH.PASSWORD_REUSED", await ErrorCodeAsync(reused));

        var changed = await ChangeExpiredAsync(client, username.ToUpperInvariant(), Password, NewPassword, await NewChallengeAsync());
        var tokens = await ReadTokensAsync(changed);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, tokens.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, NewPassword)).StatusCode);

        var user = await FindUserAsync(userId);
        Assert.Equal(0, user.FailedLoginCount);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        Assert.True(await db.Set<AuditLogEntry>().AnyAsync(e => e.UserId == userId && e.Action == "PASSWORD_CHANGE", Ct));
    }

    [Fact]
    public async Task AdministratorPasswordResetAlsoRevokesRefreshTokens()
    {
        long companyId;
        long userId;
        var username = "pwd" + Guid.NewGuid().ToString("N")[..20];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            companyId = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Company", Ct)).Id;
            userId = (await scope.ServiceProvider.GetRequiredService<SystemUserService>()
                .CreateInCompanyAsync(username, Password, companyId, Ct)).Id;
        }
        using var client = factory.CreateClient();
        var tokens = await ReadTokensAsync(await LoginAsync(client, username, Password));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemUserService>().ResetPasswordInCompanyAsync(userId, NewPassword, companyId, Ct);
        }

        Assert.Equal("SYS.AUTH.REFRESH_INVALID", await ErrorCodeAsync(await RefreshAsync(client, tokens.RefreshToken)));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, NewPassword)).StatusCode);
    }

    private async Task<(long Id, string Username)> CreateUserAsync(int passwordChangedDaysAgo = 0)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SystemUserService>();
        var username = "pwd" + Guid.NewGuid().ToString("N")[..20];
        var user = await service.CreateAsync(username, Password, Ct);
        if (passwordChangedDaysAgo > 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            user.PasswordChangedAt = user.PasswordChangedAt.AddDays(-passwordChangedDaysAgo);
            await db.SaveChangesAsync(Ct);
        }
        return (user.Id, username);
    }

    private async Task<SystemUser> FindUserAsync(long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<SystemUser>().AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }

    private async Task<CaptchaChallenge> NewChallengeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
    }

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
    {
        var challenge = await NewChallengeAsync();
        return await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
    }

    private static Task<HttpResponseMessage> ChangeExpiredAsync(
        HttpClient client, string username, string currentPassword, string newPassword, CaptchaChallenge challenge) =>
        client.PostAsJsonAsync("/api/v1/sys/auth/change-expired-password",
            new { username, currentPassword, newPassword, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/sys/auth/refresh", new { refreshToken }, Ct);

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, string accessToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sys/auth/change-password") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Ct);
    }

    private static async Task<(string AccessToken, string RefreshToken)> ReadTokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        return body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
