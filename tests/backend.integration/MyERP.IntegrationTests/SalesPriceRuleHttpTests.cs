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
/// WU-SD-005a8 (ADR-157): sal_price_rule on real SQL and the authorised HTTP API — create, list, detail,
/// same-key overlap (including two concurrent writers) and MD reference checks. SD-PRICE-HTTP-001～005.
/// </summary>
public sealed class SalesPriceRuleHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/price-rules";
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task RoutesRequireAuthenticationAndAppearInOpenApi()
    {
        // SD-PRICE-HTTP-001: anonymous 401 on every route; the operations and their error responses are documented.
        using var client = factory.CreateClient();
        using (var list = await client.GetAsync(Route, Ct)) Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using (var detail = await client.GetAsync(Route + "/1", Ct)) Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        using (var create = await client.PostAsJsonAsync(Route, Rule(1, null, "TWD", "2028-01-01", "2028-12-31", 10m), Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        openApi.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        var collection = paths.GetProperty(Route);
        Assert.True(collection.GetProperty("get").GetProperty("responses").TryGetProperty("400", out _));
        var post = collection.GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "201", "400", "403", "404", "409", "422" })
            Assert.True(post.TryGetProperty(status, out _), status);
        Assert.True(paths.GetProperty(Route + "/{id}").GetProperty("get").GetProperty("responses")
            .TryGetProperty("404", out _));
    }

    [Fact]
    public async Task CreatorSavesRulesAndViewerListsFiltersAndReadsThem()
    {
        // SD-PRICE-HTTP-002: CREATE without VIEW creates default and customer rules; VIEW lists with filters
        // and reads detail; each permission is separate; another company's rule is 404.
        var setup = await SetupAsync();
        var other = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.CREATE"));
        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.VIEW"));

        using var created = await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route,
            Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-06-30", 12.345678m, 5.5m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var defaultId = body.GetProperty("id").GetInt64();
        Assert.Equal($"{Route}/{defaultId}", created.Headers.Location!.OriginalString);
        Assert.Equal(setup.ItemId, body.GetProperty("itemId").GetInt64());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("customerId").ValueKind);
        Assert.Equal("TWD", body.GetProperty("currencyCode").GetString());
        Assert.Equal("2028-01-01", body.GetProperty("validFrom").GetString());
        Assert.Equal("2028-06-30", body.GetProperty("validTo").GetString());
        Assert.Equal(12.345678m, body.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(5.5m, body.GetProperty("discountCeiling").GetDecimal());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, body.GetProperty("version").GetInt32());

        long customerRuleId;
        using (var customer = await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route,
                   Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-03-01", "2028-03-31", 11m)))
        {
            Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
            customerRuleId = (await customer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        long usdRuleId;
        using (var usd = await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route,
                   Rule(setup.ItemId, null, "USD", "2028-01-01", "2028-12-31", 0.5m)))
        {
            Assert.Equal(HttpStatusCode.Created, usd.StatusCode);
            usdRuleId = (await usd.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }

        // Saved exactly in SQL with audit columns.
        await using (var db = Context(setup.CompanyId))
        {
            var saved = await db.Set<SalesPriceRule>().AsNoTracking().SingleAsync(x => x.Id == defaultId, Ct);
            Assert.Equal(setup.CompanyId, saved.CompanyId);
            Assert.Equal(12.345678m, saved.UnitPrice);
            Assert.Equal(5.5m, saved.DiscountCeiling);
            Assert.True(saved.IsActive);
            Assert.False(saved.IsDeleted);
            Assert.True(saved.CreatedBy > 0);
        }

        // Each action is its own permission.
        using (var noView = await SendAsync(client, creator, setup.CompanyId, HttpMethod.Get, Route))
            Assert.Equal(HttpStatusCode.Forbidden, noView.StatusCode);
        using (var noCreate = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Post, Route,
                   Rule(setup.ItemId, null, "TWD", "2029-01-01", "2029-01-31", 1m)))
            Assert.Equal(HttpStatusCode.Forbidden, noCreate.StatusCode);

        async Task<long[]> IdsAsync(string query)
        {
            using var response = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, Route + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            return [.. page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64())];
        }

        // Default sort: newest valid-from first, then ID descending.
        Assert.Equal([customerRuleId, usdRuleId, defaultId], await IdsAsync(""));
        Assert.Equal([defaultId, customerRuleId, usdRuleId], await IdsAsync("?sort=id"));
        Assert.Equal([customerRuleId], await IdsAsync($"?customerId={setup.CustomerId}"));
        Assert.Equal([usdRuleId, defaultId], await IdsAsync("?defaultOnly=true"));
        Assert.Equal([usdRuleId], await IdsAsync("?currencyCode=USD"));
        Assert.Equal([usdRuleId], await IdsAsync("?asOf=2028-07-01"));
        Assert.Equal([customerRuleId, usdRuleId, defaultId], await IdsAsync($"?itemId={setup.ItemId}&asOf=2028-03-31&active=true"));
        Assert.Empty(await IdsAsync("?active=false"));
        Assert.Empty(await IdsAsync($"?itemId={other.ItemId}"));
        using (var paged = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, Route + "?page=2&pageSize=2"))
        {
            var page = await paged.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(3, page.GetProperty("totalCount").GetInt64());
            Assert.Equal(defaultId, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt64());
        }
        foreach (var bad in new[] { "?sort=unitPrice", "?itemId=0", "?customerId=-1", "?currencyCode=usd",
                     $"?customerId={setup.CustomerId}&defaultOnly=true", "?pageSize=201" })
        {
            using var response = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, Route + bad);
            Assert.True(HttpStatusCode.BadRequest == response.StatusCode, bad);
        }

        using (var detail = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, $"{Route}/{customerRuleId}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var rule = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(setup.CustomerId, rule.GetProperty("customerId").GetInt64());
            Assert.Equal(11m, rule.GetProperty("unitPrice").GetDecimal());
            Assert.Equal(0m, rule.GetProperty("discountCeiling").GetDecimal());
        }

        // Another company's rule and an unknown ID are both hidden.
        var otherViewer = await LoginAsync(client, await UserAsync(other.CompanyId, DataScope.Company, "SAL.PRICING.VIEW"));
        foreach (var path in new[] { $"{Route}/{customerRuleId}", $"{Route}/{long.MaxValue}" })
        {
            using var hidden = await SendAsync(client, otherViewer, other.CompanyId, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.Equal("SAL.PRICE_RULE.NOT_FOUND", await ErrorCodeAsync(hidden));
        }
    }

    [Fact]
    public async Task OverlappingActiveRuleOfTheSameKeyIsRejected()
    {
        // SD-PRICE-HTTP-003 (BR-SD-026): inclusive dates; customer and default rules, currencies and items are
        // different keys; an inactive or deleted rule does not block; the conflict lists the blocking rule IDs.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));

        async Task<HttpResponseMessage> PostAsync(long? customerId, string currency, string from, string to,
            long? itemId = null) => await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route,
            Rule(itemId ?? setup.ItemId, customerId, currency, from, to, 10m));
        async Task<long> CreatedAsync(long? customerId, string currency, string from, string to, long? itemId = null)
        {
            using var response = await PostAsync(customerId, currency, from, to, itemId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }

        var first = await CreatedAsync(null, "TWD", "2028-01-01", "2028-01-31");
        var second = await CreatedAsync(null, "TWD", "2028-02-01", "2028-02-29"); // adjacent day: no overlap
        await CreatedAsync(setup.CustomerId, "TWD", "2028-01-15", "2028-02-15"); // customer key
        await CreatedAsync(null, "USD", "2028-01-15", "2028-02-15"); // other currency
        await CreatedAsync(null, "TWD", "2028-01-15", "2028-02-15", setup.SecondItemId); // other item

        using (var overlap = await PostAsync(null, "TWD", "2028-01-31", "2028-02-01"))
        {
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
            var problem = await overlap.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("SAL.PRICING.RULE_OVERLAP", problem.GetProperty("errorCode").GetString());
            Assert.Equal([first, second], problem.GetProperty("conflictingRuleIds").EnumerateArray()
                .Select(x => x.GetInt64()).ToArray());
        }
        using (var customerOverlap = await PostAsync(setup.CustomerId, "TWD", "2028-02-15", "2028-12-31"))
            Assert.Equal(HttpStatusCode.Conflict, customerOverlap.StatusCode);

        // Inactive or soft-deleted rules no longer occupy their dates.
        await using (var db = Context(setup.CompanyId))
        {
            await db.Set<SalesPriceRule>().Where(x => x.Id == first).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct);
            await db.Set<SalesPriceRule>().Where(x => x.Id == second).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsDeleted, true), Ct);
        }
        var replacement = await CreatedAsync(null, "TWD", "2028-01-31", "2028-02-01");
        await using (var db = Context(setup.CompanyId))
            Assert.Equal(1, await db.Set<SalesPriceRule>().CountAsync(x => x.Id == replacement && x.IsActive, Ct));
    }

    [Fact]
    public async Task InvalidFieldsAndUnavailableMasterDataAreRejectedWithoutSaving()
    {
        // SD-PRICE-HTTP-004: 400 field errors (SD-PRICE-006 order), 404 unknown/other-company MD references,
        // 422 inactive item/currency or unapproved customer; nothing is saved.
        var setup = await SetupAsync();
        var other = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));

        async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(object body)
        {
            using var response = await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route, body);
            return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(Ct));
        }

        var invalid = await PostAsync(new { itemId = 0, customerId = 0, currencyCode = "twd",
            validFrom = "2028-02-01", validTo = "2028-01-31", unitPrice = -1m, discountCeiling = 100.00001m });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        var errors = invalid.Body.GetProperty("errors");
        foreach (var field in new[] { "itemId", "customerId", "currencyCode", "validTo", "unitPrice", "discountCeiling" })
            Assert.True(errors.TryGetProperty(field, out _), field);
        var precision = await PostAsync(Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 1.0000001m));
        Assert.Equal(HttpStatusCode.BadRequest, precision.Status);
        Assert.True(precision.Body.GetProperty("errors").TryGetProperty("unitPrice", out _));
        var tooLarge = await PostAsync(Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 10_000_000_000_000m));
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.Status);

        // Boundary values are accepted: zero price, 100% ceiling, a one-day rule, maximum DECIMAL(19,6).
        var boundary = await PostAsync(Rule(setup.ItemId, null, "TWD", "2030-01-01", "2030-01-01", 0m, 100m));
        Assert.Equal(HttpStatusCode.Created, boundary.Status);
        var maximum = await PostAsync(Rule(setup.ItemId, null, "TWD", "2030-01-02", "2030-01-02", 9_999_999_999_999.999999m));
        Assert.Equal(HttpStatusCode.Created, maximum.Status);
        Assert.Equal(9_999_999_999_999.999999m, maximum.Body.GetProperty("unitPrice").GetDecimal());

        var otherItem = await PostAsync(Rule(other.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 1m));
        Assert.Equal(HttpStatusCode.NotFound, otherItem.Status);
        var otherCustomer = await PostAsync(Rule(setup.ItemId, other.CustomerId, "TWD", "2028-01-01", "2028-01-31", 1m));
        Assert.Equal(HttpStatusCode.NotFound, otherCustomer.Status);
        var unknownCurrency = await PostAsync(Rule(setup.ItemId, null, "EUR", "2028-01-01", "2028-01-31", 1m));
        Assert.Equal(HttpStatusCode.NotFound, unknownCurrency.Status);
        Assert.Equal("MD.CURRENCY.NOT_FOUND", unknownCurrency.Body.GetProperty("errorCode").GetString());

        await using (var db = Context(setup.CompanyId))
        {
            await db.Set<Currency>().Where(x => x.Code == "USD").ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false), Ct);
            await db.Set<Customer>().Where(x => x.Id == setup.CustomerId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.ReviewStatus, PartnerReviewStatus.Rejected), Ct);
            await db.Set<Item>().Where(x => x.Id == setup.SecondItemId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct);
        }
        var inactiveCurrency = await PostAsync(Rule(setup.ItemId, null, "USD", "2028-01-01", "2028-01-31", 1m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inactiveCurrency.Status);
        Assert.Equal("MD.CURRENCY.INACTIVE", inactiveCurrency.Body.GetProperty("errorCode").GetString());
        var unapproved = await PostAsync(Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-01-01", "2028-01-31", 1m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unapproved.Status);
        var inactiveItem = await PostAsync(Rule(setup.SecondItemId, null, "TWD", "2028-01-01", "2028-01-31", 1m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inactiveItem.Status);
        Assert.Equal("MD.ITEM.INACTIVE", inactiveItem.Body.GetProperty("errorCode").GetString());
        Assert.Equal("MD.CUSTOMER.NOT_APPROVED", unapproved.Body.GetProperty("errorCode").GetString());
        Assert.Equal("MD.ITEM.NOT_FOUND", otherItem.Body.GetProperty("errorCode").GetString());

        await using (var db = Context(setup.CompanyId))
            Assert.Equal(2, await db.Set<SalesPriceRule>().CountAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentOverlappingWritersCommitOnlyOneRule()
    {
        // SD-PRICE-HTTP-005 (SD-PRICE-007): two writers race on overlapping dates of one default key; the
        // application lock lets exactly one commit, the other gets 409. Different keys do not wait on each other.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));

        for (var round = 0; round < 3; round++)
        {
            var year = 2031 + round;
            var results = await Task.WhenAll(
                Enumerable.Range(0, 4).Select(async i =>
                {
                    using var response = await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route,
                        Rule(setup.ItemId, null, "TWD", $"{year}-01-{1 + i:00}", $"{year}-02-01", 10m + i));
                    return response.StatusCode;
                }));
            Assert.Equal(1, results.Count(x => x == HttpStatusCode.Created));
            Assert.Equal(3, results.Count(x => x == HttpStatusCode.Conflict));
        }
        await using var db = Context(setup.CompanyId);
        Assert.Equal(3, await db.Set<SalesPriceRule>().CountAsync(x => x.ItemId == setup.ItemId, Ct));
    }

    private static object Rule(long itemId, long? customerId, string currencyCode, string validFrom, string validTo,
        decimal unitPrice, decimal discountCeiling = 0m) => new
    {
        itemId, customerId, currencyCode, validFrom, validTo, unitPrice, discountCeiling
    };

    private sealed record Setup(long CompanyId, long ItemId, long SecondItemId, long CustomerId);

    private async Task<Setup> SetupAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "PR" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Price rule HTTP", Ct);
        await using var db = Context(company.Id);
        var customer = new Customer { CompanyId = company.Id,
            Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Price customer", ReviewStatus = PartnerReviewStatus.Approved };
        var uom = new UnitOfMeasure { CompanyId = company.Id,
            Code = "U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Unit" };
        db.AddRange(customer, uom,
            new Currency { CompanyId = company.Id, Code = "TWD", Name = "Taiwan dollar", DecimalPlaces = 0, IsBase = true },
            new Currency { CompanyId = company.Id, Code = "USD", Name = "US dollar", DecimalPlaces = 2 });
        await db.SaveChangesAsync(Ct);
        Item NewItem() => new() { CompanyId = company.Id,
            Code = "I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Price item", BaseUomId = uom.Id };
        var item = NewItem();
        var second = NewItem();
        db.AddRange(item, second);
        await db.SaveChangesAsync(Ct);
        return new Setup(company.Id, item.Id, second.Id, customer.Id);
    }

    private async Task<string> UserAsync(long companyId, DataScope scope, params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
            "Pricing role", null, scope, false, permissions), Ct);
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

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, long companyId,
        HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 11, 21)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesPriceRuleDbModel(), new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(),
            new CustomerDbModel(), new PaymentSetupDbModel(), new CurrencyDbModel()],
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
