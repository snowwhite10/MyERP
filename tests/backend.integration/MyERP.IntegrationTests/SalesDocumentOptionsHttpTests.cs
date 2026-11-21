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
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// WU-SD-FORM-OPTIONS (ADR-167): the customer, item and output-tax-rate choices of the quotation and order forms,
/// authorised by the quotation/order writer permissions instead of MD.*.VIEW, on real SQL and HTTP.
/// SD-DOCOPT-HTTP-001～003.
/// </summary>
public sealed class SalesDocumentOptionsHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/document-options";
    private static readonly string[] Writers =
        ["SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT", "SAL.ORDER.CREATE", "SAL.ORDER.EDIT"];
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task RoutesNeedAWriterPermissionAndAreDocumented()
    {
        // SD-DOCOPT-HTTP-001: anonymous 401; viewers, pricing users and MD viewers without a quotation/order
        // writer permission get 403; every route documents 400/401/403 in OpenAPI.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var paths = new[] { "/customers", "/items", "/output-tax-rates?businessDate=2028-06-30" };
        foreach (var path in paths)
        {
            using var anonymous = await client.GetAsync(Route + path, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        var outsider = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company,
            "SAL.QUOTATION.VIEW", "SAL.ORDER.VIEW", "SAL.PRICING.VIEW", "MD.CUSTOMER.VIEW", "MD.ITEM.VIEW"));
        foreach (var path in paths)
        {
            using var denied = await SendAsync(client, outsider, setup.CompanyId, Route + path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        openApi.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
        foreach (var path in new[] { "/customers", "/items", "/output-tax-rates" })
        {
            var responses = document.RootElement.GetProperty("paths").GetProperty(Route + path).GetProperty("get")
                .GetProperty("responses");
            foreach (var status in new[] { "200", "400", "401", "403" })
                Assert.True(responses.TryGetProperty(status, out _), path + " " + status);
        }
    }

    [Fact]
    public async Task EachWriterPermissionListsOnlySavableCustomersItemsAndTaxRates()
    {
        // SD-DOCOPT-HTTP-002: any one of the four writer permissions (no MD permission) lists exactly what the
        // quotation/order save accepts: active approved customers, active items, output/both tax rates of active codes
        // effective on the business date — never inactive, unapproved, soft-deleted, input or other-company records.
        var setup = await SetupAsync();
        var other = await SetupAsync();
        long pending, inactive, deleted, inactiveItem;
        await using (var db = Context(setup.CompanyId))
        {
            var p = NewCustomer(setup.CompanyId, "Pending customer", PartnerReviewStatus.Pending);
            var i = NewCustomer(setup.CompanyId, "Inactive customer", PartnerReviewStatus.Approved);
            i.IsActive = false;
            var d = NewCustomer(setup.CompanyId, "Deleted customer", PartnerReviewStatus.Approved);
            d.IsDeleted = true;
            var item = new Item { CompanyId = setup.CompanyId, Code = "I" + Unique(), Name = "Inactive item",
                BaseUomId = setup.UomId, IsActive = false };
            db.AddRange(p, i, d, item);
            await db.SaveChangesAsync(Ct);
            (pending, inactive, deleted, inactiveItem) = (p.Id, i.Id, d.Id, item.Id);
        }
        using var client = factory.CreateClient();
        foreach (var permission in Writers)
        {
            var token = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, permission));
            using (var customers = await SendAsync(client, token, setup.CompanyId, Route + "/customers"))
            {
                Assert.Equal(HttpStatusCode.OK, customers.StatusCode);
                var body = await customers.Content.ReadFromJsonAsync<JsonElement>(Ct);
                var rows = body.GetProperty("items").EnumerateArray().ToArray();
                Assert.Equal([setup.CustomerId], rows.Select(x => x.GetProperty("id").GetInt64()));
                Assert.Equal(setup.CustomerCode, rows[0].GetProperty("code").GetString());
                Assert.Equal("Option customer", rows[0].GetProperty("name").GetString());
                Assert.Equal(1, body.GetProperty("totalCount").GetInt64());
                foreach (var hidden in new[] { pending, inactive, deleted, other.CustomerId })
                    Assert.DoesNotContain(hidden, rows.Select(x => x.GetProperty("id").GetInt64()));
            }
            using (var items = await SendAsync(client, token, setup.CompanyId, Route + "/items"))
            {
                Assert.Equal(HttpStatusCode.OK, items.StatusCode);
                var ids = (await items.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items")
                    .EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
                Assert.Equal(setup.ItemIds, ids);
                Assert.DoesNotContain(inactiveItem, ids);
                Assert.DoesNotContain(other.ItemIds[0], ids);
            }
            using (var taxes = await SendAsync(client, token, setup.CompanyId,
                       Route + "/output-tax-rates?businessDate=2028-06-30"))
            {
                Assert.Equal(HttpStatusCode.OK, taxes.StatusCode);
                var body = await taxes.Content.ReadFromJsonAsync<JsonElement>(Ct);
                var rows = body.GetProperty("items").EnumerateArray().ToArray();
                Assert.Equal([setup.BothRateId, setup.OutputRateId], rows.Select(x => x.GetProperty("id").GetInt64()));
                Assert.Equal(["BOTH", "OUT"], rows.Select(x => x.GetProperty("code").GetString()));
                Assert.Equal("Output tax", rows[1].GetProperty("name").GetString());
                Assert.Equal(5m, rows[1].GetProperty("ratePercent").GetDecimal());
                Assert.Equal(2, body.GetProperty("totalCount").GetInt64());
            }
        }
    }

    [Fact]
    public async Task SearchPagingDatesAndInvalidRequests()
    {
        // SD-DOCOPT-HTTP-003: keyword matches code or name, paging totals are exact, only code ordering is accepted;
        // the tax list follows the business date (effective-to is inclusive, the next day picks the new rate) and a
        // missing or invalid businessDate is a 400 on that field.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.ORDER.CREATE"));

        using (var page = await SendAsync(client, token, setup.CompanyId, Route + "/items?page=2&pageSize=1"))
        {
            var body = await page.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(2, body.GetProperty("totalCount").GetInt64());
            Assert.Equal([setup.ItemIds[1]], body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
        }
        using (var search = await SendAsync(client, token, setup.CompanyId, Route + "/items?keyword=second"))
        {
            var ids = (await search.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items")
                .EnumerateArray().Select(x => x.GetProperty("id").GetInt64());
            Assert.Equal([setup.ItemIds[1]], ids);
        }
        using (var search = await SendAsync(client, token, setup.CompanyId,
                   Route + "/customers?keyword=" + setup.CustomerCode[2..8]))
            Assert.Equal(1, (await search.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        using (var sort = await SendAsync(client, token, setup.CompanyId, Route + "/customers?sort=name"))
            Assert.Equal(HttpStatusCode.BadRequest, sort.StatusCode);

        async Task<long[]> RatesOn(string query)
        {
            using var response = await SendAsync(client, token, setup.CompanyId, Route + "/output-tax-rates?" + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("id").GetInt64()).ToArray();
        }
        Assert.Equal([setup.BothRateId, setup.OutputRateId], await RatesOn("businessDate=2028-12-31"));
        Assert.Equal([setup.BothRateId, setup.NextOutputRateId], await RatesOn("businessDate=2029-01-01"));
        Assert.Empty(await RatesOn("businessDate=2027-12-31"));
        Assert.Equal([setup.OutputRateId], await RatesOn("businessDate=2028-06-30&keyword=output"));
        Assert.Equal([setup.OutputRateId], await RatesOn("businessDate=2028-06-30&keyword=out&page=1&pageSize=1"));
        using (var paged = await SendAsync(client, token, setup.CompanyId,
                   Route + "/output-tax-rates?businessDate=2028-06-30&page=2&pageSize=1"))
        {
            var body = await paged.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(2, body.GetProperty("totalCount").GetInt64());
            Assert.Equal([setup.OutputRateId], body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
        }
        foreach (var bad in new[] { "", "businessDate=2028-02-30", "businessDate=abc" })
        {
            using var invalid = await SendAsync(client, token, setup.CompanyId, Route + "/output-tax-rates?" + bad);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.EnumerateObject().Any(x => x.Name.Equals("businessDate", StringComparison.OrdinalIgnoreCase)), bad);
        }
    }

    private sealed record Setup(long CompanyId, long UomId, long CustomerId, string CustomerCode, long[] ItemIds,
        long OutputRateId, long NextOutputRateId, long BothRateId);

    private static string Unique() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static Customer NewCustomer(long companyId, string name, PartnerReviewStatus status) => new()
    {
        CompanyId = companyId, Code = "C" + Unique(), Name = name, ReviewStatus = status
    };

    private async Task<Setup> SetupAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "DO" + Unique(), "Document options", Ct);
        await using var db = Context(company.Id);
        var customer = NewCustomer(company.Id, "Option customer", PartnerReviewStatus.Approved);
        customer.Code = "A" + customer.Code[1..];
        var uom = new UnitOfMeasure { CompanyId = company.Id, Code = "U" + Unique(), Name = "Unit" };
        var output = new TaxCode { CompanyId = company.Id, Code = "OUT", Name = "Output tax", TaxType = "Output" };
        var both = new TaxCode { CompanyId = company.Id, Code = "BOTH", Name = "Both tax", TaxType = "Both" };
        var input = new TaxCode { CompanyId = company.Id, Code = "IN", Name = "Input tax", TaxType = "Input" };
        var retired = new TaxCode { CompanyId = company.Id, Code = "OLD", Name = "Retired output", TaxType = "Output",
            IsActive = false };
        db.AddRange(customer, uom, output, both, input, retired);
        await db.SaveChangesAsync(Ct);
        var first = new Item { CompanyId = company.Id, Code = "I1" + Unique(), Name = "First item", BaseUomId = uom.Id };
        var second = new Item { CompanyId = company.Id, Code = "I2" + Unique(), Name = "Second item", BaseUomId = uom.Id };
        var outputRate = new TaxRate { CompanyId = company.Id, TaxCodeId = output.Id,
            EffectiveFrom = new DateOnly(2028, 1, 1), EffectiveTo = new DateOnly(2028, 12, 31), RatePercent = 5m };
        var nextOutputRate = new TaxRate { CompanyId = company.Id, TaxCodeId = output.Id,
            EffectiveFrom = new DateOnly(2029, 1, 1), RatePercent = 6m };
        var bothRate = new TaxRate { CompanyId = company.Id, TaxCodeId = both.Id,
            EffectiveFrom = new DateOnly(2028, 1, 1), RatePercent = 0m };
        db.AddRange(first, second, outputRate, nextOutputRate, bothRate,
            new TaxRate { CompanyId = company.Id, TaxCodeId = input.Id, EffectiveFrom = new DateOnly(2028, 1, 1), RatePercent = 5m },
            new TaxRate { CompanyId = company.Id, TaxCodeId = retired.Id, EffectiveFrom = new DateOnly(2028, 1, 1), RatePercent = 3m });
        await db.SaveChangesAsync(Ct);
        return new Setup(company.Id, uom.Id, customer.Id, customer.Code, [first.Id, second.Id],
            outputRate.Id, nextOutputRate.Id, bothRate.Id);
    }

    private async Task<string> UserAsync(long companyId, DataScope scope, params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("D" + Guid.NewGuid().ToString("N")[..12],
            "Document options role", null, scope, false, permissions), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
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

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, long companyId, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        return await client.SendAsync(request, Ct);
    }

    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 11, 22)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(), new CustomerDbModel(),
            new PaymentSetupDbModel(), new CurrencyDbModel(), new TaxDbModel()],
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
