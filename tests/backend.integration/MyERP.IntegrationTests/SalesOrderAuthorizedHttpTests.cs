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
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
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
    public async Task CompanyScopedViewerSeesSavedOrderButForeignCompanyCannotDiscoverIt()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var organizations = services.GetRequiredService<OrganizationService>();
        var home = await organizations.CreateCompanyAsync(
            "SH" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Order home", Ct);
        var foreign = await organizations.CreateCompanyAsync(
            "SF" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Order foreign", Ct);
        var role = await services.GetRequiredService<RoleService>().CreateAsync(
            new RoleInput("S" + Guid.NewGuid().ToString("N")[..12], "Order viewer", null,
                DataScope.Company, false, ["SAL.ORDER.VIEW"]), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        var roles = services.GetRequiredService<RoleService>();
        await roles.AssignAsync(user.Id, home.Id,
            new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        await roles.AssignAsync(user.Id, foreign.Id,
            new UserCompanyAssignment(null, null, false, true, [role.Id]), Ct);
        long orderId;
        await using (var db = OrderContext(home.Id))
        {
            var order = new SalesOrder
            {
                CompanyId = home.Id, DocumentNo = "HT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CustomerId = 21, CustomerCode = "C21", CustomerName = "Customer",
                BusinessDate = new DateOnly(2028, 2, 29), CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
                ExchangeRate = 1m, OwnerUserId = user.Id,
                OriginalNet = 10m, OriginalGross = 10m, BaseNet = 10m, BaseGross = 10m
            };
            db.Add(order);
            await db.SaveChangesAsync(Ct);
            orderId = order.Id;
        }
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;

        async Task<HttpResponseMessage> ReadAsync(long companyId, string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
            return await client.SendAsync(request, Ct);
        }
        using var homeDetail = await ReadAsync(home.Id, $"/api/v1/sal/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, homeDetail.StatusCode);
        Assert.Equal(orderId, (await homeDetail.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("id").GetInt64());
        using var foreignDetail = await ReadAsync(foreign.Id, $"/api/v1/sal/orders/{orderId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
        using var foreignList = await ReadAsync(foreign.Id, "/api/v1/sal/orders");
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.Equal(0, (await foreignList.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("totalCount").GetInt64());
    }

    private MyErpDbContext OrderContext(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
        [new SalesOrderDbModel(), new SystemOrganizationDbModel()], new FixedCompany(companyId));

    [Fact]
    public async Task EditAndDeleteUseTheirOwnSelfScopeEvenWhenViewCanSeeOrder()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var company = await services.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "SS" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Sales scope", Ct);
        var roles = services.GetRequiredService<RoleService>();
        var viewer = await roles.CreateAsync(new RoleInput(
            "V" + Guid.NewGuid().ToString("N")[..12], "Order viewer", null,
            DataScope.Company, false, ["SAL.ORDER.VIEW"]), Ct);
        var editor = await roles.CreateAsync(new RoleInput(
            "E" + Guid.NewGuid().ToString("N")[..12], "Own order editor", null,
            DataScope.Self, false, ["SAL.ORDER.EDIT", "SAL.ORDER.DELETE"]), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        await roles.AssignAsync(user.Id, company.Id,
            new UserCompanyAssignment(null, null, true, true, [viewer.Id, editor.Id]), Ct);

        long foreignOwnerOrderId;
        long ownOrderId;
        await using (var db = OrderContext(company.Id))
        {
            SalesOrder NewOrder(long owner) => new()
            {
                CompanyId = company.Id, DocumentNo = "HT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CustomerId = 21, CustomerCode = "C21", CustomerName = "Customer",
                BusinessDate = new DateOnly(2028, 2, 29), CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
                ExchangeRate = 1m, OwnerUserId = owner,
                Version = 1,
                OriginalNet = 10m, OriginalGross = 10m, BaseNet = 10m, BaseGross = 10m
            };
            var foreignOwnerOrder = NewOrder(user.Id + 1);
            var ownOrder = NewOrder(user.Id);
            db.AddRange(foreignOwnerOrder, ownOrder);
            await db.SaveChangesAsync(Ct);
            foreignOwnerOrderId = foreignOwnerOrder.Id;
            ownOrderId = ownOrder.Id;
        }

        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;

        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request, Ct);
        }

        using var visible = await SendAsync(HttpMethod.Get, $"/api/v1/sal/orders/{foreignOwnerOrderId}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        var draft = new { customerId = 21, businessDate = "2028-02-29", currencyCode = "TWD",
            lines = new[] { new { lineNo = 1, itemId = 31, quantity = 1m,
                unitPrice = 10m, discountRate = 0m, taxRateId = 41 } } };
        using var hiddenEdit = await SendAsync(HttpMethod.Put, $"/api/v1/sal/orders/{foreignOwnerOrderId}",
            new { version = 1, draft });
        Assert.Equal(HttpStatusCode.NotFound, hiddenEdit.StatusCode);
        Assert.Equal("SAL.ORDER.NOT_FOUND", (await hiddenEdit.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errorCode").GetString());
        using var hiddenDelete = await SendAsync(HttpMethod.Delete,
            $"/api/v1/sal/orders/{foreignOwnerOrderId}?version=1");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDelete.StatusCode);
        Assert.Equal("SAL.ORDER.NOT_FOUND", (await hiddenDelete.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errorCode").GetString());
        using var ownDelete = await SendAsync(HttpMethod.Delete,
            $"/api/v1/sal/orders/{ownOrderId}?version=1");
        Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);

        await using var verify = OrderContext(company.Id);
        Assert.False((await verify.Set<SalesOrder>().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == foreignOwnerOrderId, Ct)).IsDeleted);
        Assert.True((await verify.Set<SalesOrder>().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == ownOrderId, Ct)).IsDeleted);
    }

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
