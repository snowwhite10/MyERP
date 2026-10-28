using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

public sealed class SalesQuotationHttpTests(TestDatabase database) : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task EveryQuotationRouteRequiresAuthenticationAndIsInOpenApi()
    {
        using var client = factory.CreateClient();
        var requests = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/api/v1/sal/quotations"),
            (HttpMethod.Get, "/api/v1/sal/quotations/1"),
            (HttpMethod.Post, "/api/v1/sal/quotations"),
            (HttpMethod.Put, "/api/v1/sal/quotations/1"),
            (HttpMethod.Delete, "/api/v1/sal/quotations/1?version=1"),
            (HttpMethod.Post, "/api/v1/sal/quotations/1/submit")
        };
        foreach (var (method, path) in requests)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post || method == HttpMethod.Put)
                request.Content = JsonContent.Create(new { version = 1 });
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        var paths = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/sal/quotations", out var root));
        Assert.True(root.TryGetProperty("get", out _));
        Assert.True(root.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/sal/quotations/{id}", out var item));
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/api/v1/sal/quotations/{id}/submit", out var submit));
        Assert.True(submit.TryGetProperty("post", out _));
    }

    [Fact]
    public async Task ViewRoleCanReadOnlyItsCompanyAndCannotMutateQuotation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var organizations = services.GetRequiredService<OrganizationService>();
        var home = await organizations.CreateCompanyAsync(
            "QH" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Quote home", Ct);
        var foreign = await organizations.CreateCompanyAsync(
            "QF" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Quote foreign", Ct);
        var roles = services.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput(
            "Q" + Guid.NewGuid().ToString("N")[..12], "Quote viewer", null,
            DataScope.Company, false, ["SAL.QUOTATION.VIEW"]), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>()
            .CreateAsync(username, "Strong pass 2026!", Ct);
        await roles.AssignAsync(user.Id, home.Id,
            new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        await roles.AssignAsync(user.Id, foreign.Id,
            new UserCompanyAssignment(null, null, false, true, [role.Id]), Ct);
        long quotationId;
        await using (var db = QuoteContext(home.Id))
        {
            var quote = new SalesQuotation
            {
                CompanyId = home.Id, DocumentNo = "HT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CustomerId = 21, CustomerCode = "C21", CustomerName = "Customer",
                BusinessDate = new DateOnly(2028, 2, 29), ExpiryDate = new DateOnly(2028, 3, 31),
                CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
                OwnerUserId = user.Id, OriginalNet = 10m, OriginalGross = 10m,
                BaseNet = 10m, BaseGross = 10m
            };
            db.Add(quote);
            await db.SaveChangesAsync(Ct);
            quotationId = quote.Id;
        }
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = "Strong pass 2026!", captchaId = challenge.Id,
                captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, long companyId, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request, Ct);
        }
        using var detail = await SendAsync(HttpMethod.Get, $"/api/v1/sal/quotations/{quotationId}", home.Id);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(quotationId, (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("id").GetInt64());
        using var foreignDetail = await SendAsync(HttpMethod.Get,
            $"/api/v1/sal/quotations/{quotationId}", foreign.Id);
        Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
        using var foreignList = await SendAsync(HttpMethod.Get, "/api/v1/sal/quotations", foreign.Id);
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.Equal(0, (await foreignList.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("totalCount").GetInt64());
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, "/api/v1/sal/quotations", new { customerId = 21 }),
            (HttpMethod.Put, $"/api/v1/sal/quotations/{quotationId}", new { version = 1 }),
            (HttpMethod.Delete, $"/api/v1/sal/quotations/{quotationId}?version=1", null),
            (HttpMethod.Post, $"/api/v1/sal/quotations/{quotationId}/submit", new { version = 1 })
        })
        {
            using var denied = await SendAsync(method, path, home.Id, body);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("COMMON.PERMISSION.DENIED",
                (await denied.Content.ReadFromJsonAsync<JsonElement>(Ct))
                    .GetProperty("errorCode").GetString());
        }
    }

    private MyErpDbContext QuoteContext(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
        [new SalesQuotationDbModel(), new SystemOrganizationDbModel()], new FixedCompany(companyId));

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
