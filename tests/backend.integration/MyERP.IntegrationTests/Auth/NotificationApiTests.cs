using System.Globalization;
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
using MyERP.Modules.System.Application.Notifications;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-NOTIFY-006：通知收件箱 HTTP 端點的認證、權限、本人與公司隔離、錯誤碼及分頁驗證（myerp_test + 本機 Redis）。</summary>
public sealed class NotificationApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Path = "/api/v1/sys/notifications";
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task InboxEndpointsRequirePermissionsAndOnlyTouchOwnNotifications()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var reader = await CreateRoleAsync(SystemPermissions.NotificationView, SystemPermissions.NotificationRead);
        var viewOnly = await CreateRoleAsync(SystemPermissions.NotificationView);
        var (ownerId, ownerToken) = await CreateSignedInUserAsync();
        var (otherId, otherToken) = await CreateSignedInUserAsync();
        await AssignAsync(ownerId, companyA, reader);
        await AssignAsync(ownerId, companyB, reader);
        await AssignAsync(otherId, companyA, viewOnly);
        var first = await PublishAsync(companyA, ownerId, "first");
        var second = await PublishAsync(companyA, ownerId, "second");
        var others = await PublishAsync(companyA, otherId, "others");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, Path, null, companyA)).StatusCode);
        var denied = await SendAsync(client, HttpMethod.Post, $"{Path}/{others}/read", otherToken, companyA);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));

        var page = await ReadAsync(await SendAsync(client, HttpMethod.Get, Path + "?pageSize=1", ownerToken, companyA));
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        var top = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(second, top.GetProperty("id").GetInt64());
        Assert.Equal("/sys/approvals", top.GetProperty("targetPath").GetString());
        Assert.Equal(JsonValueKind.Null, top.GetProperty("readAt").ValueKind);
        Assert.Equal(2, (await ReadAsync(await SendAsync(client, HttpMethod.Get, Path + "/unread-count", ownerToken, companyA))).GetProperty("count").GetInt32());
        Assert.Equal(0, (await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, ownerToken, companyB))).GetProperty("totalCount").GetInt32());

        var notFound = await SendAsync(client, HttpMethod.Post, $"{Path}/{others}/read", ownerToken, companyA);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("SYS.NOTIFICATION.NOT_FOUND", await ErrorCodeAsync(notFound));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"{Path}/{first}/read", ownerToken, companyB)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"{Path}/{first}/read", ownerToken, companyA)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"{Path}/{first}/read", ownerToken, companyA)).StatusCode);
        var unread = await ReadAsync(await SendAsync(client, HttpMethod.Get, Path + "?unreadOnly=true", ownerToken, companyA));
        Assert.Equal(second, Assert.Single(unread.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt64());

        var readAll = await SendAsync(client, HttpMethod.Post, Path + "/read-all", ownerToken, companyA);
        Assert.Equal(1, (await ReadAsync(readAll)).GetProperty("updatedCount").GetInt32());
        Assert.Equal(0, (await ReadAsync(await SendAsync(client, HttpMethod.Post, Path + "/read-all", ownerToken, companyA))).GetProperty("updatedCount").GetInt32());
        Assert.Equal(1, (await ReadAsync(await SendAsync(client, HttpMethod.Get, Path + "/unread-count", otherToken, companyA))).GetProperty("count").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, Path + "?pageSize=201", ownerToken, companyA)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, Path + "?sort=title", ownerToken, companyA)).StatusCode);
    }

    private async Task<long> PublishAsync(long companyId, long recipient, string sourceKey)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new SystemOrganizationDbModel()], new FixedCompany(companyId));
        var publisher = new NotificationPublisher(db, scope.ServiceProvider.GetRequiredService<IClock>());
        return await publisher.PublishAsync(new NotificationMessage(recipient, "approval", "Review " + sourceKey, "Body", "/sys/approvals", sourceKey), Ct);
    }

    private async Task<long> CreateCompanyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>()
            .CreateCompanyAsync("N" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Company", Ct);
        return company.Id;
    }

    private async Task<long> CreateRoleAsync(params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var role = await scope.ServiceProvider.GetRequiredService<RoleService>()
            .CreateAsync(new RoleInput("T" + Guid.NewGuid().ToString("N")[..12], "Test role", null, DataScope.Company, false, permissions), Ct);
        return role.Id;
    }

    private async Task AssignAsync(long userId, long companyId, params long[] roleIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>()
            .AssignAsync(userId, companyId, new UserCompanyAssignment(null, null, false, true, roleIds), Ct);
    }

    private async Task<(long Id, string AccessToken)> CreateSignedInUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var username = "ntf" + Guid.NewGuid().ToString("N")[..20];
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user.Id, (await ReadAsync(login)).GetProperty("accessToken").GetString()!);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, long companyId)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
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
