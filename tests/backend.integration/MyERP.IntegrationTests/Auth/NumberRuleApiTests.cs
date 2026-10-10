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
using MyERP.Modules.System.Application.Numbering;
using MyERP.Shared.Errors;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-NUM-008～009：單號規則 API 的權限、驗證、公司隔離、預覽與 DI 取號（myerp_test + 本機 Redis）。</summary>
public sealed class NumberRuleApiTests(TestDatabase database) : IAsyncLifetime
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
    public async Task NumberRuleEndpointsRequirePermissionsAndStayInsideTheCompany()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(false, SystemPermissions.NumberRuleView);
        var admin = await CreateRoleAsync(true);
        var (viewerId, viewerToken) = await CreateSignedInUserAsync();
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        await AssignAsync(viewerId, companyA, viewer);
        await AssignAsync(adminId, companyA, admin);
        await AssignAsync(adminId, companyB, admin);
        using var client = factory.CreateClient();
        var body = new { documentType = "sal.order", name = "Sales order", prefix = "so", dateFormat = "YearMonth", sequenceLength = 4, resetPeriod = "Monthly" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/number-rules", null, companyA)).StatusCode);
        var denied = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/number-rules", viewerToken, companyA, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));

        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/number-rules", adminToken, companyA, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = await ReadAsync(created);
        var id = rule.GetProperty("id").GetInt64();
        Assert.Equal("SAL.ORDER", rule.GetProperty("documentType").GetString());
        Assert.Equal("YearMonth", rule.GetProperty("dateFormat").GetString());
        Assert.Equal("Monthly", rule.GetProperty("resetPeriod").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/number-rules", adminToken, companyA, body)).StatusCode);
        // 另一間公司可用相同的單據類型
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/number-rules", adminToken, companyB, body)).StatusCode);

        var invalid = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/number-rules", adminToken, companyA,
            new { documentType = "SAL.QUOTE", name = "Quote", prefix = "QT", dateFormat = "Year", sequenceLength = 4, resetPeriod = "Daily" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await ReadAsync(invalid)).GetProperty("errors").TryGetProperty("resetPeriod", out _));

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/number-rules?keyword=SAL.ORDER", viewerToken, companyA));
        Assert.Equal([id], list.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetInt64()));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/number-rules/{id}", viewerToken, companyA)).StatusCode);
        var preview = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/number-rules/{id}/preview?date=2026-12-05", viewerToken, companyA));
        Assert.Equal("SO2026120001", preview.GetProperty("nextNumber").GetString());
        Assert.Equal("2026-12-05", preview.GetProperty("documentDate").GetString());

        // 他公司上下文看不到本公司規則
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/number-rules/{id}", adminToken, companyB)).StatusCode);
        var foreign = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/number-rules/{id}", adminToken, companyB,
            new { name = "Hijack", prefix = "X", dateFormat = "YearMonth", sequenceLength = 4, resetPeriod = "Monthly", isActive = false, version = 1 });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("SYS.NUMBER_RULE.NOT_FOUND", await ErrorCodeAsync(foreign));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/number-rules/{id}", viewerToken, companyA,
            new { name = "View only", prefix = "SO", dateFormat = "YearMonth", sequenceLength = 4, resetPeriod = "Monthly", isActive = true, version = 1 })).StatusCode);
    }

    [Fact]
    public async Task ModulesTakeNumbersThroughTheRegisteredGeneratorAndIssuedRulesAreLocked()
    {
        var company = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(true);
        var (adminId, token) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, admin);
        using var client = factory.CreateClient();
        var created = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/sys/number-rules", token, company,
            new { documentType = "PUR.ORDER", name = "Purchase order", prefix = "PO", dateFormat = "YearMonthDay", sequenceLength = 3, resetPeriod = "Daily" }));
        var id = created.GetProperty("id").GetInt64();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // 各模組在進程內透過 Shared 介面取號；沒有公司上下文時拒絕
            var generator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
            Assert.IsType<NumberGenerator>(generator);
            await Assert.ThrowsAsync<ForbiddenException>(() => generator.NextAsync("PUR.ORDER", new DateOnly(2026, 10, 11), Ct));
        }

        // 未取號時規則可自由修改
        var changed = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/number-rules/{id}", token, company,
            new { name = "Purchase order", prefix = "PO-", dateFormat = "YearMonthDay", sequenceLength = 4, resetPeriod = "Daily", isActive = true, version = 1 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await ReadAsync(changed)).GetProperty("isInUse").GetBoolean());

        await using (var db = database.CreateRawContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO sys_number_sequence (company_id, rule_id, period_key, current_value, created_at, created_by, version) VALUES ({company}, {id}, '20261011', 5, SYSDATETIME(), 0, 1)", Ct);
        }

        var preview = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/number-rules/{id}/preview?date=2026-10-11", token, company));
        Assert.Equal("PO-202610110006", preview.GetProperty("nextNumber").GetString());
        var locked = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/number-rules/{id}", token, company,
            new { name = "Purchase order", prefix = "PO-", dateFormat = "YearMonth", sequenceLength = 4, resetPeriod = "Monthly", isActive = true, version = 2 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, locked.StatusCode);
        Assert.Equal("SYS.NUMBER_RULE.IN_USE", await ErrorCodeAsync(locked));
        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/number-rules/{id}", token, company));
        Assert.True(detail.GetProperty("isInUse").GetBoolean());
    }

    private static string NewCode() => "N" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private async Task<long> CreateCompanyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(NewCode(), "Company", Ct);
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

    private async Task<(long Id, string AccessToken)> CreateSignedInUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var username = "num" + Guid.NewGuid().ToString("N")[..20];
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
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
