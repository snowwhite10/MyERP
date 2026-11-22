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
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// WU-SD-006b3 (ADR-170): read-only <c>GET /api/v1/sal/credit-checks/{customerId}?orderId=</c> on the real API,
/// SYS authorization, MD customer/currency, FI credit tables and SQL. SD-CREDIT-CHECK-HTTP-001～004
/// (SD-CREDIT-003/013/014).
/// </summary>
public sealed class SalesCreditCheckHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/credit-checks";
    private static readonly DateOnly Day = new(2026, 10, 1);
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task CreditCheckRequiresAuthenticationAndAppearsInOpenApi()
    {
        // SD-CREDIT-CHECK-HTTP-001 (SD-CREDIT-013): anonymous 401; the GET documents 400/403/404/409/422.
        using var client = factory.CreateClient();
        using (var anonymous = await client.GetAsync($"{Route}/1?orderId=1", Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        var operation = (await openApi.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("paths").GetProperty("/api/v1/sal/credit-checks/{customerId}").GetProperty("get");
        var responses = operation.GetProperty("responses");
        foreach (var status in new[] { "200", "400", "403", "404", "409", "422" })
            Assert.True(responses.TryGetProperty(status, out _), $"credit check documents {status}");
    }

    [Fact]
    public async Task CreditCheckReturnsFiguresAndChangesNothing()
    {
        // SD-CREDIT-CHECK-HTTP-002 (SD-CREDIT-003): limit 1000, other Approved 300, Draft 500 / 800 candidates.
        var setup = await SetupAsync("CK", limit: 1000m);
        var approved = await SeedOrderAsync(setup, SalesOrderStatus.Approved, 300m);
        var within = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 500m);
        var over = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 800m);
        var viewer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.CREDIT.VIEW");
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, viewer);
        var before = await CountsAsync(setup);

        var ok = await OkAsync(client, token, setup, $"{Route}/{setup.CustomerId}?orderId={within}");
        Assert.Equal(("WithinLimit", "TWD", true), (ok.GetProperty("status").GetString(),
            ok.GetProperty("baseCurrency").GetString(), ok.GetProperty("requiresRecheck").GetBoolean()));
        Assert.Equal((1000m, 0m, 300m, 500m, 200m, 0m), Figures(ok));
        Assert.Equal((setup.CustomerId, within, 3), (ok.GetProperty("customerId").GetInt64(),
            ok.GetProperty("orderId").GetInt64(), ok.GetProperty("limitVersion").GetInt32()));
        Assert.Equal(JsonValueKind.Null, ok.GetProperty("reasonCode").ValueKind);
        Assert.True(DateOnly.TryParse(ok.GetProperty("evaluationDate").GetString(), CultureInfo.InvariantCulture,
            out _));

        var customerOnly = await OkAsync(client, token, setup, $"{Route}/{setup.CustomerId}");
        Assert.Equal((1000m, 0m, 300m, 0m, 700m, 0m), Figures(customerOnly));
        Assert.Equal(JsonValueKind.Null, customerOnly.GetProperty("orderId").ValueKind);

        var exceeded = await OkAsync(client, token, setup, $"{Route}/{setup.CustomerId}?orderId={over}");
        Assert.Equal(("OverLimit", "SAL.CREDIT.LIMIT_EXCEEDED"), (exceeded.GetProperty("status").GetString(),
            exceeded.GetProperty("reasonCode").GetString()));
        Assert.Equal((1000m, 0m, 300m, 800m, -100m, 100m), Figures(exceeded));

        // The Approved order itself is the candidate and is counted exactly once.
        var self = await OkAsync(client, token, setup, $"{Route}/{setup.CustomerId}?orderId={approved}");
        Assert.Equal((1000m, 0m, 0m, 300m, 700m, 0m), Figures(self));

        Assert.Equal(before, await CountsAsync(setup));
    }

    [Fact]
    public async Task UnconfirmableCreditIsNamed422AndLeavesNoFiKey()
    {
        // SD-CREDIT-CHECK-HTTP-003 (SD-CREDIT-003/005/016): no limit → 422 LIMIT_NOT_SET and the FI key the lock
        // would insert is rolled back; unapproved customer → 422 CUSTOMER_INACTIVE; unknown → 404.
        var setup = await SetupAsync("CU", limit: 1000m);
        long noLimit, pending;
        await using (var db = Context(setup.CompanyId))
        {
            var fresh = NewCustomer(setup.CompanyId, PartnerReviewStatus.Approved);
            var unapproved = NewCustomer(setup.CompanyId, PartnerReviewStatus.Pending);
            db.AddRange(fresh, unapproved);
            await db.SaveChangesAsync(Ct);
            (noLimit, pending) = (fresh.Id, unapproved.Id);
        }
        var viewer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.CREDIT.VIEW");
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, viewer);

        await AssertErrorAsync(client, token, setup, $"{Route}/{noLimit}", HttpStatusCode.UnprocessableEntity,
            "SAL.CREDIT.LIMIT_NOT_SET");
        await AssertErrorAsync(client, token, setup, $"{Route}/{pending}", HttpStatusCode.UnprocessableEntity,
            "SAL.CREDIT.CUSTOMER_INACTIVE");
        await AssertErrorAsync(client, token, setup, $"{Route}/{long.MaxValue}", HttpStatusCode.NotFound,
            "MD.CUSTOMER.NOT_FOUND");

        await using var verify = Context(setup.CompanyId);
        Assert.Equal(0, await verify.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.ar_credit_key WHERE company_id = {0} AND customer_id IN ({1}, {2})",
            setup.CompanyId, noLimit, pending).SingleAsync(Ct));
    }

    [Fact]
    public async Task OrderActionHoldersSeeOnlyTheirOrderAndHiddenIdsAre404()
    {
        // SD-CREDIT-CHECK-HTTP-004 (SD-CREDIT-013/014).
        var setup = await SetupAsync("CP", limit: 1000m);
        var other = await SetupAsync("CQ", limit: 1000m);
        var order = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 100m);
        var otherOrder = await SeedOrderAsync(other, SalesOrderStatus.Draft, 100m);
        long secondCustomerOrder;
        await using (var db = Context(setup.CompanyId))
        {
            var second = NewCustomer(setup.CompanyId, PartnerReviewStatus.Approved);
            db.Add(second);
            await db.SaveChangesAsync(Ct);
            secondCustomerOrder = await SeedOrderAsync(setup with { CustomerId = second.Id }, SalesOrderStatus.Draft, 10m);
        }

        using var client = factory.CreateClient();
        var submitter = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.SUBMIT"));
        var selfEditor = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.ORDER.EDIT"));
        var orderViewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.VIEW"));
        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.CREDIT.VIEW"));
        var selfViewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.CREDIT.VIEW"));

        // An order action holder sees its own order's check, but not the customer-level figures.
        var mine = await OkAsync(client, submitter, setup, $"{Route}/{setup.CustomerId}?orderId={order}");
        Assert.Equal(100m, mine.GetProperty("candidate").GetDecimal());
        await AssertErrorAsync(client, submitter, setup, $"{Route}/{setup.CustomerId}", HttpStatusCode.Forbidden,
            "COMMON.PERMISSION.DENIED");

        // Self scope does not cover an order owned by user 7: hidden as 404 for EDIT and for CREDIT.VIEW.
        await AssertErrorAsync(client, selfEditor, setup, $"{Route}/{setup.CustomerId}?orderId={order}",
            HttpStatusCode.NotFound, "SAL.ORDER.NOT_FOUND");
        await AssertErrorAsync(client, selfViewer, setup, $"{Route}/{setup.CustomerId}?orderId={order}",
            HttpStatusCode.NotFound, "SAL.ORDER.NOT_FOUND");

        // ORDER.VIEW is not a credit action.
        await AssertErrorAsync(client, orderViewer, setup, $"{Route}/{setup.CustomerId}?orderId={order}",
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");

        // Other company's customer/order, and an order of another customer, are hidden.
        await AssertErrorAsync(client, viewer, setup, $"{Route}/{other.CustomerId}", HttpStatusCode.NotFound,
            "MD.CUSTOMER.NOT_FOUND");
        await AssertErrorAsync(client, viewer, setup, $"{Route}/{setup.CustomerId}?orderId={otherOrder}",
            HttpStatusCode.NotFound, "SAL.ORDER.NOT_FOUND");
        await AssertErrorAsync(client, viewer, setup, $"{Route}/{setup.CustomerId}?orderId={secondCustomerOrder}",
            HttpStatusCode.NotFound, "SAL.ORDER.NOT_FOUND");

        // Bad IDs are 400 before any lookup.
        foreach (var path in new[] { $"{Route}/0", $"{Route}/{setup.CustomerId}?orderId=0", $"{Route}/{setup.CustomerId}?orderId=x" })
        {
            using var bad = await GetAsync(client, viewer, setup.CompanyId, path);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
    }

    private static (decimal, decimal, decimal, decimal, decimal, decimal) Figures(JsonElement body) =>
        (body.GetProperty("limit").GetDecimal(), body.GetProperty("postedOpenAr").GetDecimal(),
            body.GetProperty("otherCommitments").GetDecimal(), body.GetProperty("candidate").GetDecimal(),
            body.GetProperty("headroom").GetDecimal(), body.GetProperty("excess").GetDecimal());

    private static async Task<JsonElement> OkAsync(HttpClient client, string token, Setup setup, string path)
    {
        using var response = await GetAsync(client, token, setup.CompanyId, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task AssertErrorAsync(HttpClient client, string token, Setup setup, string path,
        HttpStatusCode status, string code)
    {
        using var response = await GetAsync(client, token, setup.CompanyId, path);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode")
            .GetString());
    }

    /// <summary>Rows a read-only check must never touch: FI key/limit, orders, overrides, SYS rounds, audit.</summary>
    private async Task<string> CountsAsync(Setup setup)
    {
        await using var db = Context(setup.CompanyId);
        return await db.Database.SqlQueryRaw<string>("""
            SELECT CONCAT(
              (SELECT COUNT(*) FROM dbo.ar_credit_key WHERE company_id = {0}), '|',
              (SELECT SUM(version) FROM dbo.ar_credit_key WHERE company_id = {0}), '|',
              (SELECT COUNT(*) FROM dbo.ar_credit_limit WHERE company_id = {0}), '|',
              (SELECT SUM(version) FROM dbo.sal_order WHERE company_id = {0}), '|',
              (SELECT COUNT(*) FROM dbo.sal_credit_override WHERE company_id = {0}), '|',
              (SELECT COUNT(*) FROM dbo.sys_approval_instance WHERE document_type = 'SAL.ORDER'
                 AND document_id IN (SELECT id FROM dbo.sal_order WHERE company_id = {0}))) AS [Value]
            """, setup.CompanyId).SingleAsync(Ct);
    }

    private sealed record Setup(long CompanyId, long CustomerId);

    private static Customer NewCustomer(long companyId, PartnerReviewStatus status) => new()
    {
        CompanyId = companyId, Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
        Name = "Credit check customer", ReviewStatus = status
    };

    private async Task<Setup> SetupAsync(string prefix, decimal limit)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Credit check HTTP", Ct);
        await using var db = Context(company.Id);
        var customer = NewCustomer(company.Id, PartnerReviewStatus.Approved);
        db.AddRange(customer, new Currency { CompanyId = company.Id, Code = "TWD", Name = "Taiwan dollar",
            DecimalPlaces = 0, IsBase = true });
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.ar_credit_key (company_id, customer_id, created_at, created_by, version) " +
            "VALUES ({0}, {1}, '2026-11-01', 1, 1); " +
            "INSERT dbo.ar_credit_limit (company_id, customer_id, base_currency, effective_from, effective_to, " +
            "amount, status, created_at, created_by, version) VALUES ({0}, {1}, 'TWD', '2020-01-01', '2099-12-31', " +
            "{2}, 'Active', '2026-11-01', 1, 3)", [company.Id, customer.Id, limit], Ct);
        return new Setup(company.Id, customer.Id);
    }

    private async Task<long> SeedOrderAsync(Setup setup, SalesOrderStatus status, decimal baseGross)
    {
        await using var db = Context(setup.CompanyId);
        var order = new SalesOrder { CompanyId = setup.CompanyId, OwnerUserId = 7,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerId = setup.CustomerId,
            CustomerCode = "C", CustomerName = "Credit check customer", BusinessDate = Day, CurrencyCode = "TWD",
            BaseCurrencyCode = "TWD", ExchangeRate = 1m, OriginalNet = baseGross, OriginalGross = baseGross,
            BaseNet = baseGross, BaseGross = baseGross };
        order.Lines.Add(new SalesOrderLine { CompanyId = setup.CompanyId, LineNo = 1, ItemId = 31, ItemCode = "I1",
            ItemName = "Item", Quantity = 1m, UnitPrice = baseGross, TaxRateId = 41, OriginalNet = baseGross,
            OriginalGross = baseGross, BaseNet = baseGross, BaseGross = baseGross });
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        // Lines are inserted under Draft (trigger 51004); the header then follows an allowed transition (51005).
        if (status == SalesOrderStatus.Approved)
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-10-27' WHERE id = {0}",
                [order.Id], Ct);
        return order.Id;
    }

    private async Task<string> UserAsync(long companyId, DataScope scope, params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("K" + Guid.NewGuid().ToString("N")[..12],
            "Credit check role", null, scope, false, permissions), Ct);
        var username = "crd" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.ServiceProvider.GetRequiredService<SystemUserService>()
            .CreateAsync(username, Password, Ct);
        await roles.AssignAsync(user.Id, companyId, new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        return username;
    }

    private async Task<string> LoginAsync(HttpClient client, string username)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string token, long companyId,
        string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        return await client.SendAsync(request, Ct);
    }

    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesOrderDbModel(), new SystemOrganizationDbModel(), new CustomerDbModel(), new PaymentSetupDbModel(),
            new CurrencyDbModel()],
        new FixedCompany(companyId));

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
