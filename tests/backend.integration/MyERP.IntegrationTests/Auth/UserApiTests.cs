using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-USER-010～015：用戶管理 API 的權限、公司範圍、重複帳號、密碼政策、版本、重設密碼與解鎖（myerp_test + 本機 Redis）。</summary>
public sealed class UserApiTests(TestDatabase database) : IAsyncLifetime
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
    public async Task EndpointsRequireTheirOwnPermissions()
    {
        var company = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(false, SystemPermissions.UserView);
        var (viewerId, token) = await CreateSignedInUserAsync();
        await AssignAsync(viewerId, company, viewer);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/users", null, company)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/users", token, company)).StatusCode);

        var create = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username = NewUsername(), password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(create));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{viewerId}", token, company,
            new { isActive = false, version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{viewerId}/reset-password", token, company,
            new { password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{viewerId}/unlock", token, company)).StatusCode);

        // 有 EDIT 但沒有 RESET_PASSWORD 仍不可重設密碼
        var editor = await CreateRoleAsync(false, SystemPermissions.UserView, SystemPermissions.UserEdit);
        var (editorId, editorToken) = await CreateSignedInUserAsync();
        await AssignAsync(editorId, company, editor);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{viewerId}/reset-password", editorToken, company,
            new { password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{viewerId}/unlock", editorToken, company)).StatusCode);
    }

    [Fact]
    public async Task CreatedUserBelongsToCurrentCompanyAndCanSignIn()
    {
        var company = await CreateCompanyAsync();
        var other = await CreateCompanyAsync();
        var (_, token) = await CreateAdminAsync(company);
        using var client = factory.CreateClient();

        var username = NewUsername();
        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username = $" {username} ", password = Password });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadAsync(created);
        Assert.Equal(username, body.GetProperty("username").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.False(body.GetProperty("isLocked").GetBoolean());
        AssertNoSecrets(body);
        var id = body.GetProperty("id").GetInt64();

        // 新用戶只屬於建立時的公司，以該公司為預設公司（尚無角色）
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var companies = await scope.ServiceProvider.GetRequiredService<UserAuthorizationStore>().ListCompaniesAsync(id, Ct);
            var only = Assert.Single(companies);
            Assert.Equal(company, only.Id);
            Assert.True(only.IsDefault);
        }

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/users?keyword={username.ToUpperInvariant()}", token, company));
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetInt64());
        AssertNoSecrets(item);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, Password)).StatusCode);

        // 他公司看不到、也不可修改此用戶
        var (_, otherToken) = await CreateAdminAsync(other);
        var foreignList = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/users?keyword={username}", otherToken, other));
        Assert.Equal(0, foreignList.GetProperty("totalCount").GetInt64());
        var foreign = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{id}", otherToken, other, new { isActive = false, version = body.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("SYS.USER.NOT_FOUND", await ErrorCodeAsync(foreign));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{id}/reset-password", otherToken, other,
            new { password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{id}/unlock", otherToken, other)).StatusCode);
    }

    [Fact]
    public async Task CreateRejectsDuplicateUsernameWeakPasswordAndBadUsername()
    {
        var company = await CreateCompanyAsync();
        var (adminId, token) = await CreateAdminAsync(company);
        using var client = factory.CreateClient();
        var username = NewUsername();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company,
            new { username, password = Password })).StatusCode);

        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username = username.ToUpperInvariant(), password = Password });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("SYS.USER.DUPLICATE", await ErrorCodeAsync(duplicate));

        var weak = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username = NewUsername(), password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal("COMMON.REQUEST.VALIDATION_FAILED", await ErrorCodeAsync(weak));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company,
            new { username = "a b", password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/users?sort=passwordHash", token, company)).StatusCode);

        // 用戶已在其他公司存在（例如管理員本人）→ 仍是重複帳號，不會靜默加入本公司
        var self = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/users?pageSize=200", token, company));
        Assert.Contains(self.GetProperty("items").EnumerateArray(), u => u.GetProperty("id").GetInt64() == adminId);
    }

    [Fact]
    public async Task DeactivationIsVersionedAndBlocksLogin()
    {
        var company = await CreateCompanyAsync();
        var (_, token) = await CreateAdminAsync(company);
        using var client = factory.CreateClient();
        var username = NewUsername();
        var body = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username, password = Password }));
        var id = body.GetProperty("id").GetInt64();
        var version = body.GetProperty("version").GetInt32();

        var updated = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{id}", token, company, new { isActive = false, version });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = await ReadAsync(updated);
        Assert.False(after.GetProperty("isActive").GetBoolean());
        Assert.Equal(version + 1, after.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{id}", token, company,
            new { isActive = true, version })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, Password)).StatusCode);

        // 停用用戶仍列出（可重新啟用），重新啟用後可登入
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/users?keyword={username}", token, company));
        Assert.False(Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{id}", token, company,
            new { isActive = true, version = version + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, "/api/v1/sys/users/999999999", token, company,
            new { isActive = true, version = 1 })).StatusCode);
    }

    [Fact]
    public async Task LockedUserCanBeUnlockedAndPasswordResetFollowsPolicy()
    {
        var company = await CreateCompanyAsync();
        var (_, token) = await CreateAdminAsync(company);
        using var client = factory.CreateClient();
        var username = NewUsername();
        var id = (await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username, password = Password })))
            .GetProperty("id").GetInt64();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, "Wrong password 1!")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, Password)).StatusCode);
        var locked = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/users?keyword={username}", token, company));
        Assert.True(Assert.Single(locked.GetProperty("items").EnumerateArray()).GetProperty("isLocked").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{id}/unlock", token, company)).StatusCode);
        var unlocked = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/users?keyword={username}", token, company));
        Assert.False(Assert.Single(unlocked.GetProperty("items").EnumerateArray()).GetProperty("isLocked").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, Password)).StatusCode);

        var weak = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{id}/reset-password", token, company, new { password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{id}/reset-password", token, company,
            new { password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, username, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task AdminCannotDeactivateSelfAndListSortsByUsername()
    {
        var company = await CreateCompanyAsync();
        var (adminId, token) = await CreateAdminAsync(company);
        using var client = factory.CreateClient();

        var prefix = "srt" + Guid.NewGuid().ToString("N")[..10];
        foreach (var suffix in new[] { "b", "a", "c" })
        {
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company,
                new { username = prefix + suffix, password = Password })).StatusCode);
        }
        var desc = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/users?keyword={prefix}&sort=-username&pageSize=2", token, company));
        Assert.Equal(3, desc.GetProperty("totalCount").GetInt64());
        Assert.Equal([prefix + "c", prefix + "b"], desc.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("username").GetString()));
        var page2 = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/users?keyword={prefix}&page=2&pageSize=2", token, company));
        Assert.Equal(prefix + "c", Assert.Single(page2.GetProperty("items").EnumerateArray()).GetProperty("username").GetString());

        var self = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/users?pageSize=200", token, company));
        var version = self.GetProperty("items").EnumerateArray().Single(u => u.GetProperty("id").GetInt64() == adminId).GetProperty("version").GetInt32();
        var response = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{adminId}", token, company, new { isActive = false, version });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("SYS.USER.SELF_DEACTIVATION", await ErrorCodeAsync(response));
    }

    private static void AssertNoSecrets(JsonElement user)
    {
        foreach (var name in new[] { "passwordHash", "failedLoginCount", "lockoutEnd", "normalizedUsername" })
        {
            Assert.False(user.TryGetProperty(name, out _), $"User DTO must not expose {name}.");
        }
    }

    private static string NewUsername() => "usr" + Guid.NewGuid().ToString("N")[..20];

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        return await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
    }

    private async Task<long> CreateCompanyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>()
            .CreateCompanyAsync("U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Company", Ct);
        return company.Id;
    }

    private async Task<long> CreateRoleAsync(bool fullAccess, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var role = await scope.ServiceProvider.GetRequiredService<RoleService>()
            .CreateAsync(new RoleInput("T" + Guid.NewGuid().ToString("N")[..12], "Test role", null, DataScope.Company, fullAccess, permissions), Ct);
        return role.Id;
    }

    private async Task AssignAsync(long userId, long companyId, params long[] roleIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>()
            .AssignAsync(userId, companyId, new UserCompanyAssignment(null, null, false, true, roleIds), Ct);
    }

    private async Task<(long Id, string AccessToken)> CreateAdminAsync(long companyId)
    {
        var admin = await CreateRoleAsync(true);
        var (id, token) = await CreateSignedInUserAsync();
        await AssignAsync(id, companyId, admin);
        return (id, token);
    }

    private async Task<(long Id, string AccessToken)> CreateSignedInUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var username = NewUsername();
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, username, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user.Id, (await ReadAsync(login)).GetProperty("accessToken").GetString()!);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, long? companyId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (companyId is { } id)
        {
            request.Headers.Add("X-Company-Id", id.ToString(CultureInfo.InvariantCulture));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

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
