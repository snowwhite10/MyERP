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
using MyERP.Modules.System.Application.Parameters;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Settings;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-PARAM-002～004：系統參數 API 的權限、驗證、版本、公司隔離、稽核與 ISystemParameters 讀取（myerp_test + 本機 Redis）。</summary>
public sealed class ParameterApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Path = "/api/v1/sys/parameters";
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task ParametersRequirePermissionsAndAreSavedPerCompany()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(false, SystemPermissions.ParameterView);
        var admin = await CreateRoleAsync(true);
        var (viewerId, viewerToken) = await CreateSignedInUserAsync();
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        await AssignAsync(viewerId, companyA, viewer);
        await AssignAsync(adminId, companyA, admin);
        await AssignAsync(adminId, companyB, admin);
        using var client = factory.CreateClient();
        var change = new { items = new[] { new { key = SystemParameterDefinitions.QuantityDecimals, value = "3", version = 0 } } };

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, Path, null, companyA)).StatusCode);
        var denied = await SendAsync(client, HttpMethod.Put, Path, viewerToken, companyA, change);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));

        // 未修改過：預設值、版本 0
        var initial = Find(await ReadAsync(await SendAsync(client, HttpMethod.Get, Path + "?module=SYS", viewerToken, companyA)), SystemParameterDefinitions.QuantityDecimals);
        Assert.Equal("2", initial.GetProperty("value").GetString());
        Assert.True(initial.GetProperty("isDefault").GetBoolean());
        Assert.Equal(0, initial.GetProperty("version").GetInt32());
        Assert.Equal("WholeNumber", initial.GetProperty("type").GetString());
        Assert.Equal("precision", initial.GetProperty("group").GetString());
        Assert.Equal(0m, initial.GetProperty("minimum").GetDecimal());
        Assert.Equal(4m, initial.GetProperty("maximum").GetDecimal());

        var saved = await SendAsync(client, HttpMethod.Put, Path, adminToken, companyA, change);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var updated = Find(await ReadAsync(saved), SystemParameterDefinitions.QuantityDecimals);
        Assert.Equal("3", updated.GetProperty("value").GetString());
        Assert.False(updated.GetProperty("isDefault").GetBoolean());
        Assert.Equal(1, updated.GetProperty("version").GetInt32());

        // 舊版本（含「以為尚未修改」的 0）回 409
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Put, Path, adminToken, companyA, change)).StatusCode);

        // 另一間公司仍為預設值，可獨立修改
        var other = Find(await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, adminToken, companyB)), SystemParameterDefinitions.QuantityDecimals);
        Assert.Equal("2", other.GetProperty("value").GetString());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, Path, adminToken, companyB,
            new { items = new[] { new { key = SystemParameterDefinitions.QuantityDecimals, value = "0", version = 0 } } })).StatusCode);

        // 值與目前相同：不寫入、不遞增版本
        var same = Find(await ReadAsync(await SendAsync(client, HttpMethod.Put, Path, adminToken, companyA,
            new { items = new[] { new { key = SystemParameterDefinitions.QuantityDecimals, value = " 3 ", version = 1 } } })), SystemParameterDefinitions.QuantityDecimals);
        Assert.Equal(1, same.GetProperty("version").GetInt32());

        await using var db = database.CreateRawContext();
        var rows = await db.Database.SqlQuery<string>(
            $"SELECT param_value AS [Value] FROM sys_parameter WHERE company_id IN ({companyA}, {companyB}) ORDER BY company_id").ToListAsync(Ct);
        Assert.Equal(["3", "0"], rows);
        var audits = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM sys_audit_log WHERE table_name = 'sys_parameter' AND company_id = {companyA}").SingleAsync(Ct);
        Assert.Equal(1, audits);
    }

    [Fact]
    public async Task InvalidItemsAreAllReportedAndNothingIsSaved()
    {
        var company = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(true);
        var (adminId, token) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, admin);
        using var client = factory.CreateClient();

        var invalid = await SendAsync(client, HttpMethod.Put, Path, token, company, new
        {
            items = new object[]
            {
                new { key = SystemParameterDefinitions.UnitPriceDecimals, value = "5", version = 0 },
                new { key = SystemParameterDefinitions.QuantityDecimals, value = "9", version = 0 },
                new { key = "SYS.UNKNOWN", value = "1", version = 0 },
                new { key = SystemParameterDefinitions.UnitPriceDecimals, value = "3", version = 0 },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await ReadAsync(invalid)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("items[1].value", out _));
        Assert.True(errors.TryGetProperty("items[2].key", out _));
        Assert.True(errors.TryGetProperty("items[3].key", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Put, Path, token, company, new { items = Array.Empty<object>() })).StatusCode);

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, token, company));
        Assert.Equal("4", Find(list, SystemParameterDefinitions.UnitPriceDecimals).GetProperty("value").GetString());

        // 鍵大小寫不分；設回預設值且尚未保存過時不建立資料列
        var saved = await ReadAsync(await SendAsync(client, HttpMethod.Put, Path, token, company, new
        {
            items = new object[]
            {
                new { key = "sys.unit_price_decimals", value = "6", version = 0 },
                new { key = SystemParameterDefinitions.QuantityDecimals, value = "2", version = 0 },
            },
        }));
        Assert.Equal("6", Find(saved, SystemParameterDefinitions.UnitPriceDecimals).GetProperty("value").GetString());
        Assert.Equal(0, Find(saved, SystemParameterDefinitions.QuantityDecimals).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task ModulesReadCompanyValuesThroughSystemParameters()
    {
        var company = await CreateCompanyAsync();
        await using (var db = database.CreateRawContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO sys_parameter (company_id, param_key, param_value, created_at, created_by, version) VALUES ({company}, {SystemParameterDefinitions.UnitPriceDecimals}, '5', SYSDATETIME(), 0, 1)", Ct);
            // 舊資料不合目前規則時退回預設值
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO sys_parameter (company_id, param_key, param_value, created_at, created_by, version) VALUES ({company}, {SystemParameterDefinitions.QuantityDecimals}, '8', SYSDATETIME(), 0, 1)", Ct);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var registered = scope.ServiceProvider.GetRequiredService<ISystemParameters>();
        Assert.IsType<SystemParameterReader>(registered);
        // 沒有公司上下文時拒絕
        await Assert.ThrowsAsync<ForbiddenException>(() => registered.GetIntegerAsync(SystemParameterDefinitions.UnitPriceDecimals, Ct));

        var catalog = scope.ServiceProvider.GetRequiredService<ParameterCatalog>();
        await using var context = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new SystemOrganizationDbModel()], new FixedCompany(company));
        var parameters = new SystemParameterReader(context, catalog);
        Assert.Equal(5, await parameters.GetIntegerAsync(SystemParameterDefinitions.UnitPriceDecimals, Ct));
        Assert.Equal(2, await parameters.GetIntegerAsync(SystemParameterDefinitions.QuantityDecimals, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => parameters.GetAsync("SYS.UNKNOWN", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => parameters.GetBooleanAsync(SystemParameterDefinitions.QuantityDecimals, Ct));
    }

    private static JsonElement Find(JsonElement list, string key) =>
        list.EnumerateArray().Single(p => p.GetProperty("key").GetString() == key);

    private static string NewCode() => "P" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

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
        var username = "par" + Guid.NewGuid().ToString("N")[..20];
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
