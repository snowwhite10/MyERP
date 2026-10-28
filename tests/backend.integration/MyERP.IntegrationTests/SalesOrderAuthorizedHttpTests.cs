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

namespace MyERP.IntegrationTests;

/// <summary>Real SYS authorization must protect each order HTTP action independently.</summary>
public sealed class SalesOrderAuthorizedHttpTests(TestDatabase database) : IAsyncLifetime
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
    public async Task ViewOnlyRoleCannotCreateEditOrDeleteOrders()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var company = await services.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "SA" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Sales HTTP auth", Ct);
        var role = await services.GetRequiredService<RoleService>().CreateAsync(
            new RoleInput("S" + Guid.NewGuid().ToString("N")[..12], "Sales viewer", null,
                DataScope.Company, false, ["SAL.ORDER.VIEW"]), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        await services.GetRequiredService<RoleService>().AssignAsync(user.Id, company.Id,
            new UserCompanyAssignment(null, null, false, true, [role.Id]), Ct);
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sal/orders"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(0, page.GetProperty("totalCount").GetInt64());
        }

        var draft = new { customerId = 21, businessDate = "2028-02-29", currencyCode = "TWD",
            lines = new[] { new { lineNo = 1, itemId = 31, quantity = 1m,
                unitPrice = 10m, discountRate = 0m, taxRateId = 41 } } };
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, "/api/v1/sal/orders", draft),
            (HttpMethod.Put, "/api/v1/sal/orders/1", new { version = 1, draft }),
            (HttpMethod.Delete, "/api/v1/sal/orders/1?version=1", null)
        })
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("COMMON.PERMISSION.DENIED", problem.GetProperty("errorCode").GetString());
        }
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
