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
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.Security;
using MyERP.Shared.Security;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Auditing;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-IPW-004～005：白名單 API 的權限／版本／重複檢查，以及實際資料的判斷。</summary>
public sealed class IpWhitelistApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task MaintenanceRequiresPermissionValidatesAndPersistsRules()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var organization = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var company = (await organization.CreateCompanyAsync("I" + Guid.NewGuid().ToString("N")[..12], "IP test", Ct)).Id;
        var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
        var admin = (await roles.CreateAsync(new RoleInput("I" + Guid.NewGuid().ToString("N")[..12], "Admin", null,
            DataScope.Company, true, []), Ct)).Id;
        var viewer = (await roles.CreateAsync(new RoleInput("I" + Guid.NewGuid().ToString("N")[..12], "Viewer", null,
            DataScope.Company, false, [SystemPermissions.IpWhitelistView]), Ct)).Id;
        var adminUser = await CreateUserAsync(scope.ServiceProvider);
        var viewerUser = await CreateUserAsync(scope.ServiceProvider);
        await roles.AssignAsync(adminUser.Id, company, new UserCompanyAssignment(null, null, false, true, [admin]), Ct);
        await roles.AssignAsync(viewerUser.Id, company, new UserCompanyAssignment(null, null, false, true, [viewer]), Ct);
        var adminToken = await LoginAsync(adminUser.Username);
        var viewerToken = await LoginAsync(viewerUser.Username);
        using var client = factory.CreateClient();
        const string path = "/api/v1/sys/ip-whitelist";

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, path, null, company)).StatusCode);
        var input = new { scope = "System", targetId = (long?)null, ipRange = "10.0.0.0/8", validFrom = (DateOnly?)null,
            validTo = (DateOnly?)null, description = "office" };
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, path, viewerToken, company, input)).StatusCode);
        var createdResponse = await SendAsync(client, HttpMethod.Post, path, adminToken, company, input);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = created.GetProperty("id").GetInt64();
        Assert.Equal("10.0.0.0/8", created.GetProperty("ipRange").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, path, adminToken, company, input)).StatusCode);
        var list = await (await SendAsync(client, HttpMethod.Get, path + "?keyword=office", viewerToken, company))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetInt64() == id);

        var invalid = await SendAsync(client, HttpMethod.Post, path, adminToken, company,
            new { scope = "System", ipRange = "10.1.2.3/8", description = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var stale = await SendAsync(client, HttpMethod.Put, $"{path}/{id}", adminToken, company,
            new { isActive = false, version = 0, description = "old" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var changed = await SendAsync(client, HttpMethod.Put, $"{path}/{id}", adminToken, company,
            new { isActive = false, version = created.GetProperty("version").GetInt32(), description = "retired" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task PolicyReadsStoredRulesAndRecordsDenial()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IpWhitelistService>();
        var created = await service.CreateAsync(new IpWhitelistInput(IpAccessScope.System, null,
            "198.51.100.0/24", null, null, "site"), Ct);
        Assert.True(created.Id > 0);
        // Development 的總開關預設關閉；單元測試已涵蓋三層匹配與中介層 403。
        var cache = scope.ServiceProvider.GetRequiredService<IpWhitelistCache>();
        cache.Invalidate();
        Assert.Equal("198.51.100.0/24", (await service.GetAsync(created.Id, Ct)).IpRange);
        await service.UpdateAsync(created.Id, new IpWhitelistUpdate(null, null, "off", false, created.Version), Ct);
        Assert.False((await service.GetAsync(created.Id, Ct)).IsActive);
    }

    [Fact]
    public async Task EnabledSystemRuleDeniesAnonymousRequestAndWritesAudit()
    {
        await using var enabledFactory = new ApiFactory(database, enabled: true);
        await using (var scope = enabledFactory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IpWhitelistService>().CreateAsync(
                new IpWhitelistInput(IpAccessScope.System, null, "203.0.113.0/24", null, null, "only office"), Ct);
        }

        using var client = enabledFactory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Ct)).StatusCode);
        var denied = await client.GetAsync("/api/v1/sys/auth/captcha", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("COMMON.REQUEST.IP_NOT_ALLOWED",
            (await denied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString());
        await using var auditScope = enabledFactory.Services.CreateAsyncScope();
        var logs = auditScope.ServiceProvider.GetRequiredService<MyErpDbContext>().Set<AuditLogEntry>();
        Assert.True(await logs.AnyAsync(x => x.Action == AuditActions.IpDenied && x.Changes != null, Ct));
    }

    private static async Task<(long Id, string Username)> CreateUserAsync(IServiceProvider services)
    {
        var username = "ip" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        return (user.Id, username);
    }

    private async Task<string> LoginAsync(string username)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        string? token, long company, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", company.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request, Ct);
    }

    private sealed class ApiFactory(TestDatabase database, bool enabled = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
            builder.UseSetting("Security:IpWhitelist:Enabled", enabled ? "true" : "false");
        }
    }
}
