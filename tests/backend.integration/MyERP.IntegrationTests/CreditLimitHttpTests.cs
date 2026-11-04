using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// WU-FI-001b2: real SYS authorization, MD references and SQL behind /api/v1/ar/credit-limits
/// (FI-CREDIT-007/009/011/012/013/017).
/// </summary>
public sealed class CreditLimitHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Root = "/api/v1/ar/credit-limits";
    private static readonly string[] AllPermissions =
        ["AR.CREDIT.VIEW", "AR.CREDIT.CREATE", "AR.CREDIT.EDIT", "AR.CREDIT.DEACTIVATE"];
    private readonly List<HttpClient> clients = [];
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task EveryCreditLimitRouteRequiresAuthenticationAndIsInOpenApi()
    {
        // FI-CREDIT-012: anonymous callers get 401 before any FI code runs.
        using var client = factory.CreateClient();
        var requests = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, Root),
            (HttpMethod.Get, Root + "/1"),
            (HttpMethod.Post, Root),
            (HttpMethod.Put, Root + "/1"),
            (HttpMethod.Post, Root + "/1/deactivate")
        };
        foreach (var (method, path) in requests)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method != HttpMethod.Get) request.Content = JsonContent.Create(new { version = 1 });
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        var paths = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths");
        Assert.True(paths.TryGetProperty(Root, out var root));
        Assert.True(root.TryGetProperty("get", out _));
        Assert.True(root.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty(Root + "/{id}", out var item));
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(paths.TryGetProperty(Root + "/{id}/deactivate", out var deactivate));
        Assert.True(deactivate.TryGetProperty("post", out _));
    }

    [Fact]
    public async Task AuthorizedLifecyclePersistsServerCurrencyAndReportsNamedErrors()
    {
        // FI-CREDIT-007/009/011/013/017 through HTTP with a real approved customer and base currency.
        var (company, customerId) = await CompanyWithCustomerAsync();
        var client = await LoginAsync(company, AllPermissions);

        using var created = await client.SendAsync(Post(Root, new
        {
            customerId, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", amount = 50000.25m,
            companyId = company + 999, baseCurrency = "USD", status = "Deactivated"
        }), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = saved.GetProperty("id").GetInt64();
        Assert.Equal("TWD", saved.GetProperty("baseCurrency").GetString());
        Assert.Equal("Active", saved.GetProperty("status").GetString());
        Assert.Equal(1, saved.GetProperty("version").GetInt32());
        Assert.Equal(company, await ScalarAsync<long>("SELECT company_id FROM dbo.ar_credit_limit WHERE id = @id", id));

        using var detail = await client.GetAsync($"{Root}/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(50000.25m, (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("amount").GetDecimal());

        using var invalid = await client.SendAsync(Post(Root, new
        {
            customerId, effectiveFrom = "2027-02-01", effectiveTo = "2027-01-01", amount = -1m
        }), Ct);
        var errors = (await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "COMMON.REQUEST.VALIDATION_FAILED"))
            .GetProperty("errors");
        Assert.True(errors.TryGetProperty("effectiveTo", out _));
        Assert.True(errors.TryGetProperty("amount", out _));

        using var overlap = await client.SendAsync(Post(Root, new
        {
            customerId, effectiveFrom = "2026-12-31", effectiveTo = "2027-03-31", amount = 1m
        }), Ct);
        await AssertProblemAsync(overlap, HttpStatusCode.Conflict, "FI.CREDIT.LIMIT_OVERLAP");

        using var stale = await client.SendAsync(Put($"{Root}/{id}", new
        {
            version = 2, customerId, effectiveFrom = "2026-01-01", effectiveTo = "2026-06-30", amount = 1m
        }), Ct);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        Assert.Equal(50000.25m, await ScalarAsync<decimal>("SELECT amount FROM dbo.ar_credit_limit WHERE id = @id", id));

        using var updated = await client.SendAsync(Put($"{Root}/{id}", new
        {
            version = 1, customerId, effectiveFrom = "2026-01-01", effectiveTo = "2026-06-30", amount = 0m
        }), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var replaced = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(0m, replaced.GetProperty("amount").GetDecimal());
        Assert.Equal(2, replaced.GetProperty("version").GetInt32());

        using var deactivated = await client.SendAsync(Post($"{Root}/{id}/deactivate", new { version = 2 }), Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal("Deactivated", (await deactivated.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("status").GetString());
        using var again = await client.SendAsync(Post($"{Root}/{id}/deactivate", new { version = 3 }), Ct);
        await AssertProblemAsync(again, HttpStatusCode.UnprocessableEntity, "FI.CREDIT.LIMIT_NOT_ACTIVE");

        using var missing = await client.GetAsync($"{Root}/{long.MaxValue}", Ct);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "FI.CREDIT.LIMIT_NOT_FOUND");

        using var badQuery = await client.GetAsync(Root + "?from=2026-12-01&to=2026-01-01", Ct);
        await AssertProblemAsync(badQuery, HttpStatusCode.BadRequest, "COMMON.REQUEST.VALIDATION_FAILED");

        using var listed = await client.GetAsync(Root + "?status=Deactivated&customerId=" + customerId, Ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var page = await listed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt64());
        Assert.Equal(id, page.GetProperty("items")[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task EachActionNeedsItsOwnPermissionAndDeniedWritesChangeNothing()
    {
        // FI-CREDIT-012: VIEW alone cannot create, edit or deactivate; CREATE alone cannot read.
        var (company, customerId) = await CompanyWithCustomerAsync();
        var maintainer = await LoginAsync(company, AllPermissions);
        using var created = await maintainer.SendAsync(Post(Root, new
        {
            customerId, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", amount = 10m
        }), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();

        var viewer = await LoginAsync(company, ["AR.CREDIT.VIEW"]);
        using (var list = await viewer.GetAsync(Root, Ct)) Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var denied = await viewer.SendAsync(Post(Root, new
        {
            customerId, effectiveFrom = "2027-01-01", effectiveTo = "2027-12-31", amount = 1m
        }), Ct)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await viewer.SendAsync(Put($"{Root}/{id}", new
        {
            version = 1, customerId, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", amount = 99m
        }), Ct)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await viewer.SendAsync(Post($"{Root}/{id}/deactivate", new { version = 1 }), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var creator = await LoginAsync(company, ["AR.CREDIT.CREATE"]);
        using (var denied = await creator.GetAsync($"{Root}/{id}", Ct))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ar_credit_limit WHERE company_id = @id", company));
        Assert.Equal(10m, await ScalarAsync<decimal>("SELECT amount FROM dbo.ar_credit_limit WHERE id = @id", id));
        Assert.Equal(1, await ScalarAsync<int>("SELECT version FROM dbo.ar_credit_limit WHERE id = @id", id));
    }

    [Fact]
    public async Task ForeignCompanyCannotDiscoverOrChangeLimit()
    {
        // FI-CREDIT-013: another company's limit ID is 404 for read and every write; its list is empty.
        var (home, customerId) = await CompanyWithCustomerAsync();
        var (foreign, _) = await CompanyWithCustomerAsync();
        var homeClient = await LoginAsync(home, AllPermissions);
        using var created = await homeClient.SendAsync(Post(Root, new
        {
            customerId, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", amount = 10m
        }), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();

        var foreignClient = await LoginAsync(foreign, AllPermissions);
        using (var read = await foreignClient.GetAsync($"{Root}/{id}", Ct))
            await AssertProblemAsync(read, HttpStatusCode.NotFound, "FI.CREDIT.LIMIT_NOT_FOUND");
        using (var write = await foreignClient.SendAsync(Put($"{Root}/{id}", new
        {
            version = 1, customerId, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", amount = 99m
        }), Ct)) await AssertProblemAsync(write, HttpStatusCode.NotFound, "FI.CREDIT.LIMIT_NOT_FOUND");
        using (var write = await foreignClient.SendAsync(Post($"{Root}/{id}/deactivate", new { version = 1 }), Ct))
            await AssertProblemAsync(write, HttpStatusCode.NotFound, "FI.CREDIT.LIMIT_NOT_FOUND");
        using (var list = await foreignClient.GetAsync(Root, Ct))
            Assert.Equal(0, (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        // The home customer does not exist in the foreign company, so MD rejects it there.
        using (var foreignCustomer = await foreignClient.SendAsync(Post(Root, new
        {
            customerId, effectiveFrom = "2028-01-01", effectiveTo = "2028-12-31", amount = 1m
        }), Ct)) await AssertProblemAsync(foreignCustomer, HttpStatusCode.NotFound, "MD.CUSTOMER.NOT_FOUND");

        Assert.Equal(10m, await ScalarAsync<decimal>("SELECT amount FROM dbo.ar_credit_limit WHERE id = @id", id));
        Assert.Equal("Active", await ScalarAsync<string>("SELECT status FROM dbo.ar_credit_limit WHERE id = @id", id));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ar_credit_limit WHERE company_id = @id", foreign));
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, body.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        return body;
    }

    private static HttpRequestMessage Post(string path, object body) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body) };

    private static HttpRequestMessage Put(string path, object body) =>
        new(HttpMethod.Put, path) { Content = JsonContent.Create(body) };

    private async Task<(long Company, long Customer)> CompanyWithCustomerAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "CL" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Credit HTTP", Ct);
        await using var db = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new SystemOrganizationDbModel(), new CustomerDbModel(), new PaymentSetupDbModel(), new CurrencyDbModel()],
            new FixedCompany(company.Id));
        var customer = new Customer
        {
            CompanyId = company.Id, Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Credit customer", ReviewStatus = PartnerReviewStatus.Approved
        };
        db.AddRange(customer, new Currency
        {
            CompanyId = company.Id, Code = "TWD", Name = "Taiwan dollar", DecimalPlaces = 0, IsBase = true
        });
        await db.SaveChangesAsync(Ct);
        return (company.Id, customer.Id);
    }

    private async Task<HttpClient> LoginAsync(long companyId, string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("F" + Guid.NewGuid().ToString("N")[..12], "Credit role", null,
            DataScope.Company, false, permissions), Ct);
        var username = "fi" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        await roles.AssignAsync(user.Id, companyId, new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        var client = factory.CreateClient();
        clients.Add(client);
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        return client;
    }

    private async Task<T> ScalarAsync<T>(string sql, long id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
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
