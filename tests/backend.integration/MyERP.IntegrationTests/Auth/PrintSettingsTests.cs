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
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Settings;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-PRINT-001～003：列印設定參數的宣告、保存與 IPrintSettingsReader 讀取（ADR-027，myerp_test + 本機 Redis）。</summary>
public sealed class PrintSettingsTests(TestDatabase database) : IAsyncLifetime
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
    public void PrintParametersAreDeclaredInPrintGroup()
    {
        var print = new SystemParameterDefinitions().Parameters.Where(p => p.Group == SystemParameterDefinitions.PrintGroup).ToDictionary(p => p.Key);

        Assert.Equal(
            [
                SystemParameterDefinitions.PrintCompanyName, SystemParameterDefinitions.PrintAddress, SystemParameterDefinitions.PrintPostalCode,
                SystemParameterDefinitions.PrintPhone, SystemParameterDefinitions.PrintFax, SystemParameterDefinitions.PrintPurchaseOrderFooter,
                SystemParameterDefinitions.PrintSalesDeliveryFooter, SystemParameterDefinitions.PrintShowLogo, SystemParameterDefinitions.PrintShowStamp,
            ],
            print.Keys);
        Assert.All(print.Values, p => Assert.Equal("SYS", p.Module));
        Assert.Equal((ParameterType.Text, 100, ""), (print[SystemParameterDefinitions.PrintCompanyName].Type, print[SystemParameterDefinitions.PrintCompanyName].MaxLength, print[SystemParameterDefinitions.PrintCompanyName].DefaultValue));
        Assert.Equal(200, print[SystemParameterDefinitions.PrintAddress].MaxLength);
        Assert.Equal(10, print[SystemParameterDefinitions.PrintPostalCode].MaxLength);
        Assert.Equal(30, print[SystemParameterDefinitions.PrintPhone].MaxLength);
        Assert.Equal(30, print[SystemParameterDefinitions.PrintFax].MaxLength);
        Assert.Equal(500, print[SystemParameterDefinitions.PrintPurchaseOrderFooter].MaxLength);
        Assert.Equal(500, print[SystemParameterDefinitions.PrintSalesDeliveryFooter].MaxLength);
        Assert.Equal((ParameterType.Boolean, "false"), (print[SystemParameterDefinitions.PrintShowLogo].Type, print[SystemParameterDefinitions.PrintShowLogo].DefaultValue));
        Assert.Equal((ParameterType.Boolean, "false"), (print[SystemParameterDefinitions.PrintShowStamp].Type, print[SystemParameterDefinitions.PrintShowStamp].DefaultValue));
    }

    [Fact]
    public async Task PrintSettingsAreSavedThroughParameterApiWithLengthChecks()
    {
        var company = await CreateCompanyAsync("Print Co");
        var admin = await CreateRoleAsync(true);
        var (adminId, token) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, admin);
        using var client = factory.CreateClient();

        var tooLong = await SendAsync(client, HttpMethod.Put, Path, token, company, new
        {
            items = new object[]
            {
                new { key = SystemParameterDefinitions.PrintPhone, value = "02-1234-5678", version = 0 },
                new { key = SystemParameterDefinitions.PrintPostalCode, value = new string('9', 11), version = 0 },
                new { key = SystemParameterDefinitions.PrintPurchaseOrderFooter, value = new string('x', 501), version = 0 },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var errors = (await ReadAsync(tooLong)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("items[1].value", out _));
        Assert.True(errors.TryGetProperty("items[2].value", out _));
        Assert.False(errors.TryGetProperty("items[0].value", out _));

        var footer = "Payment within 30 days.\nThank you.";
        var saved = await SendAsync(client, HttpMethod.Put, Path, token, company, new
        {
            items = new object[]
            {
                new { key = SystemParameterDefinitions.PrintAddress, value = "  1 Example Road, Taipei  ", version = 0 },
                new { key = SystemParameterDefinitions.PrintSalesDeliveryFooter, value = footer, version = 0 },
                new { key = SystemParameterDefinitions.PrintShowStamp, value = "TRUE", version = 0 },
            },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var list = await ReadAsync(saved);
        Assert.Equal("1 Example Road, Taipei", Find(list, SystemParameterDefinitions.PrintAddress).GetProperty("value").GetString());
        Assert.Equal(footer, Find(list, SystemParameterDefinitions.PrintSalesDeliveryFooter).GetProperty("value").GetString());
        Assert.Equal("true", Find(list, SystemParameterDefinitions.PrintShowStamp).GetProperty("value").GetString());
        Assert.Equal("print", Find(list, SystemParameterDefinitions.PrintShowStamp).GetProperty("group").GetString());
    }

    [Fact]
    public async Task ReaderReturnsCompanySettingsWithCompanyNameFallback()
    {
        var plain = await CreateCompanyAsync("Plain Company Ltd");
        var custom = await CreateCompanyAsync("Custom Company Ltd");
        await using (var db = database.CreateRawContext())
        {
            foreach (var (key, value) in new[]
                     {
                         (SystemParameterDefinitions.PrintCompanyName, "Custom Printing Name"),
                         (SystemParameterDefinitions.PrintAddress, "8 Harbour Road"),
                         (SystemParameterDefinitions.PrintPostalCode, "999077"),
                         (SystemParameterDefinitions.PrintPhone, "+852 2345 6789"),
                         (SystemParameterDefinitions.PrintFax, "+852 2345 6780"),
                         (SystemParameterDefinitions.PrintPurchaseOrderFooter, "PO footer"),
                         (SystemParameterDefinitions.PrintSalesDeliveryFooter, "Delivery footer"),
                         (SystemParameterDefinitions.PrintShowLogo, "true"),
                     })
            {
                await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO sys_parameter (company_id, param_key, param_value, created_at, created_by, version) VALUES ({custom}, {key}, {value}, SYSDATETIME(), 0, 1)", Ct);
            }
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var registered = scope.ServiceProvider.GetRequiredService<IPrintSettingsReader>();
        await Assert.ThrowsAsync<ForbiddenException>(() => registered.GetAsync(Ct));

        Assert.Equal(new PrintSettings("Plain Company Ltd", "", "", "", "", "", "", false, false), await ReadAsync(scope, plain));
        Assert.Equal(
            new PrintSettings("Custom Printing Name", "8 Harbour Road", "999077", "+852 2345 6789", "+852 2345 6780", "PO footer", "Delivery footer", true, false),
            await ReadAsync(scope, custom));
    }

    private async Task<PrintSettings> ReadAsync(AsyncServiceScope scope, long companyId)
    {
        await using var context = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new SystemOrganizationDbModel()], new FixedCompany(companyId));
        var catalog = scope.ServiceProvider.GetRequiredService<ParameterCatalog>();
        return await new PrintSettingsReader(context, new SystemParameterReader(context, catalog)).GetAsync(Ct);
    }

    private static JsonElement Find(JsonElement list, string key) =>
        list.EnumerateArray().Single(p => p.GetProperty("key").GetString() == key);

    private static string NewCode() => "R" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private async Task<long> CreateCompanyAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(NewCode(), name, Ct);
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
        var username = "prt" + Guid.NewGuid().ToString("N")[..20];
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

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
