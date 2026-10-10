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

/// <summary>SYS-AUDIT-010～015：稽核查詢 API、公司隔離、資料變更差異、密碼雜湊遮蔽、登入／萬用碼／登出／權限變更事件（myerp_test + 本機 Redis）。</summary>
public sealed class AuditLogApiTests(TestDatabase database) : IAsyncLifetime
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
    public async Task RequiresAuditViewPermissionAndValidatesFilters()
    {
        var company = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(false, SystemPermissions.UserView);
        var (viewerId, token) = await CreateSignedInUserAsync();
        await AssignAsync(viewerId, company, viewer);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/audit-logs", null, company)).StatusCode);
        var denied = await SendAsync(client, HttpMethod.Get, "/api/v1/sys/audit-logs", token, company);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));

        var (_, adminToken) = await CreateAdminAsync(company);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/audit-logs", adminToken, company)).StatusCode);
        foreach (var bad in new[] { "category=OTHER", "from=2026-10-12&to=2026-10-11", "sort=action", "pageSize=500" })
        {
            var response = await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/audit-logs?{bad}", adminToken, company);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{bad} should be rejected");
        }
    }

    [Fact]
    public async Task DataChangesShowFieldHistoryOnlyToTheOwningCompany()
    {
        var company = await CreateCompanyAsync();
        var other = await CreateCompanyAsync();
        var (adminId, token) = await CreateAdminAsync(company);
        var (_, otherToken) = await CreateAdminAsync(other);
        using var client = factory.CreateClient();

        var created = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/sys/branches", token, company,
            new { code = "B" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), name = "Central" }));
        var id = created.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/branches/{id}", token, company,
            new { name = "Central HQ", isActive = true, version = created.GetProperty("version").GetInt32() })).StatusCode);

        var history = await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"/api/v1/sys/audit-logs?tableName=SYS_BRANCH&recordId={id}&sort=occurredAt", token, company));
        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["CREATE", "UPDATE"], items.Select(i => i.GetProperty("action").GetString()));
        var update = items[1];
        Assert.Equal("DATA", update.GetProperty("category").GetString());
        Assert.Equal(adminId, update.GetProperty("userId").GetInt64());
        Assert.False(string.IsNullOrEmpty(update.GetProperty("username").GetString()));
        Assert.Equal(company, update.GetProperty("companyId").GetInt64());
        Assert.False(string.IsNullOrEmpty(update.GetProperty("traceId").GetString()));
        var name = update.GetProperty("changes").GetProperty("name");
        Assert.Equal("Central", name.GetProperty("old").GetString());
        Assert.Equal("Central HQ", name.GetProperty("new").GetString());
        Assert.Equal(["name"], update.GetProperty("changes").EnumerateObject().Select(p => p.Name));

        // 預設新到舊；日期範圍以 IClock 日期篩選
        var newest = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/audit-logs?tableName=sys_branch&recordId={id}", token, company));
        Assert.Equal("UPDATE", newest.GetProperty("items")[0].GetProperty("action").GetString());
        var past = await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"/api/v1/sys/audit-logs?tableName=sys_branch&recordId={id}&from=2000-01-01&to=2000-01-31", token, company));
        Assert.Equal(0, past.GetProperty("totalCount").GetInt64());

        var foreign = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/audit-logs?tableName=sys_branch&recordId={id}", otherToken, other));
        Assert.Equal(0, foreign.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task PasswordResetIsAuditedWithoutTheHash()
    {
        var company = await CreateCompanyAsync();
        var (_, token) = await CreateAdminAsync(company);
        using var client = factory.CreateClient();
        var user = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username = NewUsername(), password = Password }));
        var id = user.GetProperty("id").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/users/{id}/reset-password", token, company,
            new { password = NewPassword })).StatusCode);

        var response = await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/audit-logs?tableName=sys_user&recordId={id}&action=update", token, company);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("AQAAAA", raw, StringComparison.Ordinal);
        var body = JsonDocument.Parse(raw).RootElement;
        var hash = Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("changes").GetProperty("password_hash");
        Assert.Equal("***", hash.GetProperty("old").GetString());
        Assert.Equal("***", hash.GetProperty("new").GetString());

        var creation = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/audit-logs?tableName=sys_user&recordId={id}&action=CREATE", token, company));
        Assert.Equal("***", Assert.Single(creation.GetProperty("items").EnumerateArray())
            .GetProperty("changes").GetProperty("password_hash").GetProperty("new").GetString());
    }

    [Fact]
    public async Task LoginLogoutAndMasterCodeEventsAreVisibleToTheUsersCompany()
    {
        var company = await CreateCompanyAsync();
        var other = await CreateCompanyAsync();
        var (_, adminToken) = await CreateAdminAsync(company);
        var (_, otherToken) = await CreateAdminAsync(other);
        var username = NewUsername();
        long userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            userId = (await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateInCompanyAsync(username, Password, company, Ct)).Id;
        }

        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, "Wrong password 1!")).StatusCode);
        var unknown = "nobody" + Guid.NewGuid().ToString("N")[..12];
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, unknown, Password)).StatusCode);
        var master = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = "not-issued", captchaCode = "123456" }, Ct);
        Assert.Equal(HttpStatusCode.OK, master.StatusCode);
        var tokens = await ReadAsync(master);
        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sys/auth/logout"))
        {
            logout.Content = JsonContent.Create(new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
            logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout, Ct)).StatusCode);
        }

        var events = await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"/api/v1/sys/audit-logs?category=security&userId={userId}&sort=occurredAt", adminToken, company));
        var items = events.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["LOGIN_FAILED", "MASTER_CODE_LOGIN", "LOGIN", "LOGOUT"], items.Select(i => i.GetProperty("action").GetString()));
        Assert.All(items.Take(3), i => Assert.Equal(username, i.GetProperty("username").GetString()));
        Assert.Equal("INVALID_CREDENTIALS", items[0].GetProperty("changes").GetProperty("reason").GetString());
        Assert.All(items, i => Assert.Equal(JsonValueKind.Null, i.GetProperty("companyId").ValueKind));
        var raw = events.GetRawText();
        Assert.DoesNotContain(Password, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", raw, StringComparison.Ordinal);

        // 不存在帳號的登入失敗不屬於任何公司；其他公司看不到本公司用戶的登入
        var unknownEvents = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/audit-logs?action=LOGIN_FAILED&pageSize=200", adminToken, company));
        Assert.DoesNotContain(unknownEvents.GetProperty("items").EnumerateArray(), i => i.GetProperty("username").GetString() == unknown);
        var foreign = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/audit-logs?userId={userId}", otherToken, other));
        Assert.Equal(0, foreign.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task PermissionChangesAreSecurityEventsOfTheOperator()
    {
        var company = await CreateCompanyAsync();
        var (adminId, token) = await CreateAdminAsync(company);
        var viewer = await CreateRoleAsync(false, SystemPermissions.UserView);
        using var client = factory.CreateClient();
        var user = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/sys/users", token, company, new { username = NewUsername(), password = Password }));
        var userId = user.GetProperty("id").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{userId}/companies/{company}", token, company,
            new { branchId = (long?)null, departmentId = (long?)null, isDefault = true, isActive = true, roleIds = new[] { viewer } })).StatusCode);

        var events = await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"/api/v1/sys/audit-logs?action=PERMISSION_CHANGE&userId={adminId}", token, company));
        var change = Assert.Single(events.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("changes").GetProperty("targetUserId").GetInt64() == userId);
        Assert.Equal("SECURITY", change.GetProperty("category").GetString());
        Assert.Equal(company, change.GetProperty("companyId").GetInt64());
        Assert.Equal([viewer], change.GetProperty("changes").GetProperty("roleIds").EnumerateArray().Select(x => x.GetInt64()));

        // 同一操作的資料變更（sys_user_role）亦有記錄
        var roleRows = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/audit-logs?tableName=sys_user_role&action=CREATE&pageSize=200", token, company));
        Assert.Contains(roleRows.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("changes").GetProperty("user_id").GetProperty("new").GetInt64() == userId);
    }

    private static string NewUsername() => "aud" + Guid.NewGuid().ToString("N")[..20];

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
            .CreateCompanyAsync("A" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Company", Ct);
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
