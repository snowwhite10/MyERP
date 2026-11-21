using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
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
/// WU-SD-005a9 (ADR-158): versioned edit with two-key locking, deactivate and named lock timeout. SD-PRICE-HTTP-006～009.
/// WU-SD-005a10 (ADR-159): read-only dated quote for pricing viewers and quotation/order form writers. SD-PRICE-HTTP-010～011.
/// WU-SD-005b1a (ADR-161): current MD item/customer labels on every rule response. SD-PRICE-HTTP-012.
/// WU-SD-005b1b1 (ADR-162): pricing-scoped item/customer/currency options without MD permission. SD-PRICE-HTTP-013～014.
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

        // SD-PRICE-HTTP-006/007: edit and deactivate are anonymous-401 and document their error responses.
        using (var put = await client.PutAsJsonAsync(Route + "/1", Edit(1, null, "TWD", "2028-01-01", "2028-12-31", 10m, 1), Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        using (var deactivate = await client.PostAsJsonAsync(Route + "/1/deactivate", new { version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, deactivate.StatusCode);
        var putResponses = paths.GetProperty(Route + "/{id}").GetProperty("put").GetProperty("responses");
        foreach (var status in new[] { "200", "400", "403", "404", "409", "422" })
            Assert.True(putResponses.TryGetProperty(status, out _), "put " + status);
        var deactivateResponses = paths.GetProperty(Route + "/{id}/deactivate").GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "200", "403", "404", "409", "422" })
            Assert.True(deactivateResponses.TryGetProperty(status, out _), "deactivate " + status);
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

    [Fact]
    public async Task EditorChangesRuleWithCurrentVersionAndOverlapIsRecheckedForTheNewKey()
    {
        // SD-PRICE-HTTP-006 (ADR-158): EDIT alone can edit; the version must be current; the rule may shrink or move
        // within its own dates; overlap is re-checked (excluding itself) on the new key; a key change frees the old
        // key; every edit raises the version and leaves an UPDATE audit row with old/new values; field, permission,
        // unknown/other-company, inactive-rule and MD errors save nothing.
        var setup = await SetupAsync();
        var other = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var editor = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.EDIT"));
        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.VIEW"));
        var ruleId = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-03-31", 10m));
        var neighbour = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-04-01", "2028-04-30", 20m));
        var customerRule = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-06-01", "2028-06-30", 30m));

        async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string token, long id, object body)
        {
            using var response = await SendAsync(client, token, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}", body);
            var text = await response.Content.ReadAsStringAsync(Ct);
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }

        // Shrink and reprice within its own dates: allowed, version 1 → 2, saved exactly.
        var edited = await PutAsync(editor, ruleId, Edit(setup.ItemId, null, "TWD", "2028-01-15", "2028-03-31", 11.5m, 1, 7.25m));
        Assert.Equal(HttpStatusCode.OK, edited.Status);
        Assert.Equal(2, edited.Body.GetProperty("version").GetInt32());
        Assert.Equal("2028-01-15", edited.Body.GetProperty("validFrom").GetString());
        Assert.Equal(11.5m, edited.Body.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(7.25m, edited.Body.GetProperty("discountCeiling").GetDecimal());
        Assert.True(edited.Body.GetProperty("isActive").GetBoolean());

        // Stale version (the old 1) and a nonsense version are 409 concurrency conflicts.
        foreach (var version in new[] { 1, 0 })
        {
            var stale = await PutAsync(editor, ruleId, Edit(setup.ItemId, null, "TWD", "2028-01-15", "2028-03-31", 99m, version));
            Assert.Equal(HttpStatusCode.Conflict, stale.Status);
            Assert.Equal("COMMON.CONCURRENCY.CONFLICT", stale.Body.GetProperty("errorCode").GetString());
        }

        // Extending into the neighbour overlaps it (self excluded from the list).
        var overlap = await PutAsync(editor, ruleId, Edit(setup.ItemId, null, "TWD", "2028-01-15", "2028-04-01", 11.5m, 2));
        Assert.Equal(HttpStatusCode.Conflict, overlap.Status);
        Assert.Equal("SAL.PRICING.RULE_OVERLAP", overlap.Body.GetProperty("errorCode").GetString());
        Assert.Equal([neighbour], overlap.Body.GetProperty("conflictingRuleIds").EnumerateArray().Select(x => x.GetInt64()).ToArray());

        // Moving the neighbour onto the customer key overlaps the customer's rule there.
        var customerOverlap = await PutAsync(editor, neighbour,
            Edit(setup.ItemId, setup.CustomerId, "TWD", "2028-06-15", "2028-07-15", 20m, 1));
        Assert.Equal(HttpStatusCode.Conflict, customerOverlap.Status);
        Assert.Equal([customerRule], customerOverlap.Body.GetProperty("conflictingRuleIds").EnumerateArray().Select(x => x.GetInt64()).ToArray());

        // A clean key change (default → customer, same dates) frees the old default dates for a new rule.
        var moved = await PutAsync(editor, neighbour, Edit(setup.ItemId, setup.CustomerId, "TWD", "2028-04-01", "2028-04-30", 21m, 1));
        Assert.Equal(HttpStatusCode.OK, moved.Status);
        Assert.Equal(setup.CustomerId, moved.Body.GetProperty("customerId").GetInt64());
        await CreateAsync(client, creator, setup.CompanyId, Rule(setup.ItemId, null, "TWD", "2028-04-01", "2028-04-30", 22m));

        // Field errors (400), missing EDIT (403, even with VIEW or CREATE), unknown and other-company rules (404).
        var invalid = await PutAsync(editor, ruleId, Edit(0, 0, "twd", "2028-02-01", "2028-01-31", -1m, 2));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        foreach (var field in new[] { "itemId", "customerId", "currencyCode", "validTo", "unitPrice" })
            Assert.True(invalid.Body.GetProperty("errors").TryGetProperty(field, out _), field);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PutAsync(viewer, ruleId, Edit(setup.ItemId, null, "TWD", "2028-01-15", "2028-03-31", 1m, 2))).Status);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PutAsync(creator, ruleId, Edit(setup.ItemId, null, "TWD", "2028-01-15", "2028-03-31", 1m, 2))).Status);
        var unknown = await PutAsync(editor, long.MaxValue, Edit(setup.ItemId, null, "TWD", "2028-01-15", "2028-03-31", 1m, 1));
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal("SAL.PRICE_RULE.NOT_FOUND", unknown.Body.GetProperty("errorCode").GetString());
        var otherCreator = await LoginAsync(client, await UserAsync(other.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var otherRule = await CreateAsync(client, otherCreator, other.CompanyId,
            Rule(other.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 1m));
        Assert.Equal(HttpStatusCode.NotFound,
            (await PutAsync(editor, otherRule, Edit(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 1m, 1))).Status);

        // MD checks apply to the edited references: other-company item 404, inactive currency 422.
        var otherItem = await PutAsync(editor, ruleId, Edit(other.ItemId, null, "TWD", "2028-01-15", "2028-03-31", 1m, 2));
        Assert.Equal(HttpStatusCode.NotFound, otherItem.Status);
        Assert.Equal("MD.ITEM.NOT_FOUND", otherItem.Body.GetProperty("errorCode").GetString());
        await using (var db = Context(setup.CompanyId))
            await db.Set<Currency>().Where(x => x.Code == "USD").ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false), Ct);
        var inactiveCurrency = await PutAsync(editor, ruleId, Edit(setup.ItemId, null, "USD", "2028-01-15", "2028-03-31", 1m, 2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inactiveCurrency.Status);
        Assert.Equal("MD.CURRENCY.INACTIVE", inactiveCurrency.Body.GetProperty("errorCode").GetString());

        // An inactive rule is not editable (422); reactivation is a new rule.
        await using (var db = Context(setup.CompanyId))
            await db.Set<SalesPriceRule>().Where(x => x.Id == customerRule).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false), Ct);
        var inactive = await PutAsync(editor, customerRule, Edit(setup.ItemId, setup.CustomerId, "TWD", "2028-06-01", "2028-06-30", 31m, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inactive.Status);
        Assert.Equal("SAL.PRICING.RULE_INACTIVE", inactive.Body.GetProperty("errorCode").GetString());

        // Only the successful edit changed this rule; it left one UPDATE audit row with old and new prices.
        await using (var db = Context(setup.CompanyId))
        {
            var saved = await db.Set<SalesPriceRule>().AsNoTracking().SingleAsync(x => x.Id == ruleId, Ct);
            Assert.Equal((2, 11.5m, "TWD", new DateOnly(2028, 1, 15)),
                (saved.Version, saved.UnitPrice, saved.CurrencyCode, saved.ValidFrom));
            Assert.NotNull(saved.UpdatedAt);
            var audits = await db.Database.SqlQueryRaw<string>(
                "SELECT [changes] AS [Value] FROM dbo.sys_audit_log WHERE table_name = 'sal_price_rule' AND company_id = {0} " +
                "AND record_id = {1} AND [action] = 'UPDATE' ORDER BY id", setup.CompanyId,
                ruleId.ToString(CultureInfo.InvariantCulture)).ToListAsync(Ct);
            var change = JsonDocument.Parse(Assert.Single(audits)).RootElement.GetProperty("unit_price");
            Assert.Equal(10m, change.GetProperty("old").GetDecimal());
            Assert.Equal(11.5m, change.GetProperty("new").GetDecimal());
        }
    }

    [Fact]
    public async Task DeactivatorRetiresRuleWithCurrentVersionAndFreesItsDates()
    {
        // SD-PRICE-HTTP-007 (ADR-158): DEACTIVATE alone can retire an active rule; the version must be current; the
        // rule stays stored as inactive; a second deactivate is 422; the dates are free for a new rule; audited.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var deactivator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.DEACTIVATE"));
        var editor = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.EDIT"));
        var ruleId = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-12-31", 10m));

        async Task<(HttpStatusCode Status, JsonElement Body)> DeactivateAsync(string token, long id, int version)
        {
            using var response = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post,
                $"{Route}/{id}/deactivate", new { version });
            var text = await response.Content.ReadAsStringAsync(Ct);
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await DeactivateAsync(editor, ruleId, 1)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeactivateAsync(creator, ruleId, 1)).Status);
        var unknown = await DeactivateAsync(deactivator, long.MaxValue, 1);
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal("SAL.PRICE_RULE.NOT_FOUND", unknown.Body.GetProperty("errorCode").GetString());
        var stale = await DeactivateAsync(deactivator, ruleId, 2);
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal("COMMON.CONCURRENCY.CONFLICT", stale.Body.GetProperty("errorCode").GetString());

        var done = await DeactivateAsync(deactivator, ruleId, 1);
        Assert.Equal(HttpStatusCode.OK, done.Status);
        Assert.False(done.Body.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, done.Body.GetProperty("version").GetInt32());
        Assert.Equal(10m, done.Body.GetProperty("unitPrice").GetDecimal());

        var again = await DeactivateAsync(deactivator, ruleId, 2);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.Status);
        Assert.Equal("SAL.PRICING.RULE_INACTIVE", again.Body.GetProperty("errorCode").GetString());

        // The retired dates are free; the retired rule is still stored, inactive, and audited once.
        await CreateAsync(client, creator, setup.CompanyId, Rule(setup.ItemId, null, "TWD", "2028-06-01", "2028-06-30", 12m));
        await using var db = Context(setup.CompanyId);
        var saved = await db.Set<SalesPriceRule>().AsNoTracking().SingleAsync(x => x.Id == ruleId, Ct);
        Assert.False(saved.IsActive);
        Assert.False(saved.IsDeleted);
        Assert.Equal(2, saved.Version);
        var audits = await db.Database.SqlQueryRaw<string>(
            "SELECT [changes] AS [Value] FROM dbo.sys_audit_log WHERE table_name = 'sal_price_rule' AND company_id = {0} " +
            "AND record_id = {1} AND [action] = 'UPDATE'", setup.CompanyId,
            ruleId.ToString(CultureInfo.InvariantCulture)).ToListAsync(Ct);
        Assert.True(JsonDocument.Parse(Assert.Single(audits)).RootElement.TryGetProperty("is_active", out _));
    }

    [Fact]
    public async Task ConcurrentKeySwapsLockBothKeysInFixedOrderWithoutDeadlock()
    {
        // SD-PRICE-HTTP-008 (ADR-158): two edits swap keys in opposite directions at the same time (default ↔
        // customer, non-overlapping dates). Both keys are locked in one fixed order, so both succeed every round.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var editor = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.EDIT"));
        var a = await CreateAsync(client, creator, setup.CompanyId, Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 1m));
        var b = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-02-01", "2028-02-29", 2m));
        long? aCustomer = null, bCustomer = setup.CustomerId;
        for (var round = 1; round <= 4; round++)
        {
            (aCustomer, bCustomer) = (bCustomer, aCustomer);
            var results = await Task.WhenAll(
                SwapAsync(a, aCustomer, "2028-01-01", "2028-01-31", 1m, round),
                SwapAsync(b, bCustomer, "2028-02-01", "2028-02-29", 2m, round));
            Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x));
        }
        await using var db = Context(setup.CompanyId);
        var saved = await db.Set<SalesPriceRule>().AsNoTracking().Where(x => x.ItemId == setup.ItemId)
            .OrderBy(x => x.Id).ToListAsync(Ct);
        Assert.Equal([(5, aCustomer), (5, bCustomer)], saved.Select(x => (x.Version, x.CustomerId)).ToArray());

        async Task<HttpStatusCode> SwapAsync(long id, long? customerId, string from, string to, decimal price, int version)
        {
            using var response = await SendAsync(client, editor, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                Edit(setup.ItemId, customerId, "TWD", from, to, price, version));
            return response.StatusCode;
        }
    }

    [Fact]
    public async Task HeldKeyLockTimesOutAsNamedConflictInsteadOfServerError()
    {
        // SD-PRICE-HTTP-009 (ADR-158): while another transaction holds the key lock, create, edit and deactivate
        // wait only the configured time and return 409 SAL.PRICING.RULE_LOCK_TIMEOUT; nothing changes.
        await using var shortLock = new ApiFactory(database, lockTimeoutMilliseconds: 300);
        var setup = await SetupAsync();
        using var client = shortLock.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var manager = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company,
            "SAL.PRICING.EDIT", "SAL.PRICING.DEACTIVATE"));
        var ruleId = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 1m));

        await using (var holder = new SqlConnection(database.ConnectionString))
        {
            await holder.OpenAsync(Ct);
            await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(Ct);
            await using (var command = holder.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "EXEC sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction'";
                command.Parameters.AddWithValue("@resource",
                    SalesPriceRuleService.LockResource(setup.CompanyId, setup.ItemId, "TWD", null));
                await command.ExecuteNonQueryAsync(Ct);
            }

            HttpResponseMessage[] responses =
            [
                await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route,
                    Rule(setup.ItemId, null, "TWD", "2028-05-01", "2028-05-31", 1m)),
                await SendAsync(client, manager, setup.CompanyId, HttpMethod.Put, $"{Route}/{ruleId}",
                    Edit(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 2m, 1)),
                await SendAsync(client, manager, setup.CompanyId, HttpMethod.Post, $"{Route}/{ruleId}/deactivate",
                    new { version = 1 })
            ];
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                    Assert.Equal("SAL.PRICING.RULE_LOCK_TIMEOUT", await ErrorCodeAsync(response));
                }
            }
            await transaction.RollbackAsync(Ct);
        }

        await using var db = Context(setup.CompanyId);
        var saved = Assert.Single(await db.Set<SalesPriceRule>().AsNoTracking()
            .Where(x => x.ItemId == setup.ItemId).ToListAsync(Ct));
        Assert.Equal((1, 1m, true), (saved.Version, saved.UnitPrice, saved.IsActive));
    }

    [Fact]
    public async Task QuoteSelectsTheDatedCustomerOrDefaultRuleForAnyFormPermissionWithoutWriting()
    {
        // SD-PRICE-HTTP-010 (WU-SD-005a10): anonymous 401 and documented responses; the customer tier wins over
        // the default tier, inclusive date boundaries, inactive/deleted/other-currency rules ignored; VIEW or any
        // quotation/order CREATE/EDIT may quote; nothing is written.
        using var anonymous = factory.CreateClient();
        using (var noAuth = await anonymous.GetAsync(Quote(1, 1, "TWD", "2028-01-01"), Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);
        using (var openApi = await anonymous.GetAsync("/openapi/v1.json", Ct))
        {
            openApi.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
            var responses = document.RootElement.GetProperty("paths").GetProperty(Route + "/quote")
                .GetProperty("get").GetProperty("responses");
            foreach (var status in new[] { "200", "400", "403", "404", "409", "422" })
                Assert.True(responses.TryGetProperty(status, out _), "quote " + status);
        }

        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var defaultJan = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 100m, 10m));
        var defaultFeb = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-02-01", "2028-02-29", 101m, 5m));
        var customerJan = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-01-10", "2028-01-20", 90.123456m, 12.5m));
        await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "USD", "2028-01-01", "2028-12-31", 3.5m));
        var retired = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-02-10", "2028-02-20", 80m));
        var deleted = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-02-21", "2028-02-25", 70m));
        await using (var db = Context(setup.CompanyId))
        {
            await db.Set<SalesPriceRule>().Where(x => x.Id == retired).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct);
            await db.Set<SalesPriceRule>().Where(x => x.Id == deleted).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsDeleted, true), Ct);
        }
        var before = await RuleStateAsync(setup.CompanyId);

        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.VIEW"));
        var body = await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "TWD", "2028-01-10");
        Assert.Equal(customerJan, body.GetProperty("ruleId").GetInt64());
        Assert.Equal(1, body.GetProperty("ruleVersion").GetInt32());
        Assert.Equal(setup.CustomerId, body.GetProperty("customerId").GetInt64());
        Assert.Equal(setup.ItemId, body.GetProperty("itemId").GetInt64());
        Assert.Equal("TWD", body.GetProperty("currencyCode").GetString());
        Assert.Equal(90.123456m, body.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(12.5m, body.GetProperty("discountCeiling").GetDecimal());
        Assert.Equal("2028-01-10", body.GetProperty("validFrom").GetString());
        Assert.Equal("2028-01-20", body.GetProperty("validTo").GetString());
        Assert.Equal("2028-01-10", body.GetProperty("asOf").GetString());

        Assert.Equal(customerJan, (await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "TWD", "2028-01-20"))
            .GetProperty("ruleId").GetInt64());
        var fallback = await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "TWD", "2028-01-21");
        Assert.Equal(defaultJan, fallback.GetProperty("ruleId").GetInt64());
        Assert.Equal(JsonValueKind.Null, fallback.GetProperty("customerId").ValueKind);
        Assert.Equal(100m, fallback.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(defaultJan, (await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "TWD", "2028-01-09"))
            .GetProperty("ruleId").GetInt64());
        // Leap day closes the February default rule; inactive and deleted customer rules never take over.
        Assert.Equal(defaultFeb, (await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "TWD", "2028-02-29"))
            .GetProperty("ruleId").GetInt64());
        Assert.Equal(defaultFeb, (await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "TWD", "2028-02-15"))
            .GetProperty("ruleId").GetInt64());
        Assert.Equal(defaultFeb, (await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "TWD", "2028-02-22"))
            .GetProperty("ruleId").GetInt64());
        Assert.Equal(3.5m, (await QuoteOkAsync(viewer, setup.ItemId, setup.CustomerId, "USD", "2028-01-15"))
            .GetProperty("unitPrice").GetDecimal());

        foreach (var permission in new[] { "SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT", "SAL.ORDER.CREATE", "SAL.ORDER.EDIT" })
        {
            var formUser = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, permission));
            Assert.Equal(customerJan, (await QuoteOkAsync(formUser, setup.ItemId, setup.CustomerId, "TWD", "2028-01-15"))
                .GetProperty("ruleId").GetInt64());
        }
        foreach (var permission in new[] { "SAL.PRICING.CREATE", "SAL.ORDER.VIEW", "SAL.DELIVERY.CREATE" })
        {
            var outsider = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, permission));
            using var denied = await SendAsync(client, outsider, setup.CompanyId, HttpMethod.Get,
                Quote(setup.ItemId, setup.CustomerId, "TWD", "2028-01-15"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));
        }

        Assert.Equal(before, await RuleStateAsync(setup.CompanyId));

        async Task<JsonElement> QuoteOkAsync(string token, long itemId, long customerId, string currency, string date)
        {
            using var response = await SendAsync(client, token, setup.CompanyId, HttpMethod.Get,
                Quote(itemId, customerId, currency, date));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    [Fact]
    public async Task QuoteNamesMissingAndDuplicateRulesAndRejectsBadOrHiddenReferences()
    {
        // SD-PRICE-HTTP-011: 422 SAL.PRICING.RULE_NOT_FOUND (no rule, unknown currency, gap date); 409
        // SAL.PRICING.DUPLICATE_ACTIVE_RULE with conflictingRuleIds when two active rows on the deciding tier
        // cover the date (integrity damage written outside the API); a clean customer tier still wins over a
        // damaged default tier. 400 missing/malformed parameters (all listed at once); 404 hidden item/customer;
        // 422 inactive item / unapproved customer. No price is guessed and nothing is written.
        var setup = await SetupAsync();
        var other = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.VIEW"));
        var first = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-01-01", "2028-01-31", 10m));
        var customerRule = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-01-05", "2028-01-06", 9m));
        long second;
        await using (var db = Context(setup.CompanyId))
        {
            var damaged = new SalesPriceRule { CompanyId = setup.CompanyId, ItemId = setup.ItemId,
                CurrencyCode = "TWD", ValidFrom = new DateOnly(2028, 1, 15), ValidTo = new DateOnly(2028, 2, 15),
                UnitPrice = 11m, DiscountCeiling = 0m, IsActive = true };
            db.Add(damaged);
            await db.SaveChangesAsync(Ct);
            second = damaged.Id;
        }
        var before = await RuleStateAsync(setup.CompanyId);

        using (var duplicate = await Get(Quote(setup.ItemId, setup.CustomerId, "TWD", "2028-01-20")))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("SAL.PRICING.DUPLICATE_ACTIVE_RULE", problem.GetProperty("errorCode").GetString());
            Assert.Equal([first, second], problem.GetProperty("conflictingRuleIds").EnumerateArray()
                .Select(x => x.GetInt64()).ToArray());
            Assert.False(problem.TryGetProperty("unitPrice", out _));
        }
        using (var customerWins = await Get(Quote(setup.ItemId, setup.CustomerId, "TWD", "2028-01-05")))
        {
            Assert.Equal(HttpStatusCode.OK, customerWins.StatusCode);
            Assert.Equal(customerRule, (await customerWins.Content.ReadFromJsonAsync<JsonElement>(Ct))
                .GetProperty("ruleId").GetInt64());
        }
        foreach (var path in new[]
                 {
                     Quote(setup.ItemId, setup.CustomerId, "TWD", "2027-12-31"),
                     Quote(setup.ItemId, setup.CustomerId, "TWD", "2028-02-16"),
                     Quote(setup.SecondItemId, setup.CustomerId, "TWD", "2028-01-10"),
                     Quote(setup.ItemId, setup.CustomerId, "EUR", "2028-01-10")
                 })
        {
            using var missing = await Get(path);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
            Assert.Equal("SAL.PRICING.RULE_NOT_FOUND", await ErrorCodeAsync(missing));
        }

        using (var empty = await Get(Route + "/quote"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            var errors = (await empty.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            foreach (var field in new[] { "itemId", "customerId", "currency", "businessDate" })
                Assert.True(errors.TryGetProperty(field, out _), field);
        }
        foreach (var path in new[]
                 {
                     Quote(0, setup.CustomerId, "TWD", "2028-01-10"),
                     Quote(setup.ItemId, -1, "TWD", "2028-01-10"),
                     Quote(setup.ItemId, setup.CustomerId, "twd", "2028-01-10"),
                     Quote(setup.ItemId, setup.CustomerId, "TWDX", "2028-01-10"),
                     Quote(setup.ItemId, setup.CustomerId, "TWD", "2028-02-30"),
                     Quote(setup.ItemId, setup.CustomerId, "TWD", "tomorrow"),
                     $"{Route}/quote?itemId=abc&customerId={setup.CustomerId}&currency=TWD&businessDate=2028-01-10"
                 })
        {
            using var bad = await Get(path);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using (var hiddenItem = await Get(Quote(other.ItemId, setup.CustomerId, "TWD", "2028-01-10")))
        {
            Assert.Equal(HttpStatusCode.NotFound, hiddenItem.StatusCode);
            Assert.Equal("MD.ITEM.NOT_FOUND", await ErrorCodeAsync(hiddenItem));
        }
        using (var hiddenCustomer = await Get(Quote(setup.ItemId, other.CustomerId, "TWD", "2028-01-10")))
            Assert.Equal(HttpStatusCode.NotFound, hiddenCustomer.StatusCode);
        using (var unknownItem = await Get(Quote(long.MaxValue, setup.CustomerId, "TWD", "2028-01-10")))
            Assert.Equal(HttpStatusCode.NotFound, unknownItem.StatusCode);

        // The other company's own viewer cannot see this company's rules through its own IDs either.
        var otherViewer = await LoginAsync(client, await UserAsync(other.CompanyId, DataScope.Company, "SAL.PRICING.VIEW"));
        using (var otherQuote = await SendAsync(client, otherViewer, other.CompanyId, HttpMethod.Get,
                   Quote(other.ItemId, other.CustomerId, "TWD", "2028-01-10")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otherQuote.StatusCode);
            Assert.Equal("SAL.PRICING.RULE_NOT_FOUND", await ErrorCodeAsync(otherQuote));
        }

        await using (var db = Context(setup.CompanyId))
        {
            await db.Set<Customer>().Where(x => x.Id == setup.CustomerId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.ReviewStatus, PartnerReviewStatus.Rejected), Ct);
            await db.Set<Item>().Where(x => x.Id == setup.SecondItemId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct);
        }
        using (var unapproved = await Get(Quote(setup.ItemId, setup.CustomerId, "TWD", "2028-01-10")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unapproved.StatusCode);
            Assert.Equal("MD.CUSTOMER.NOT_APPROVED", await ErrorCodeAsync(unapproved));
        }
        using (var inactiveItem = await Get(Quote(setup.SecondItemId, setup.CustomerId, "TWD", "2028-01-10")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, inactiveItem.StatusCode);
            Assert.Equal("MD.ITEM.INACTIVE", await ErrorCodeAsync(inactiveItem));
        }

        Assert.Equal(before, await RuleStateAsync(setup.CompanyId));

        Task<HttpResponseMessage> Get(string path) =>
            SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, path);
    }

    [Fact]
    public async Task RuleResponsesCarryCurrentMasterDataLabelsForTheListPage()
    {
        // SD-PRICE-HTTP-012 (ADR-161): list, detail and write responses name the item and customer from MD (current
        // values, inactive included) so the price-rule page needs no MD permission; a default rule has no customer
        // label and a soft-deleted item has no label (the page shows its ID). Labels never cross companies.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        var creator = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.PRICING.CREATE"));
        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.VIEW"));
        string itemCode, itemName, customerCode, customerName;
        await using (var db = Context(setup.CompanyId))
        {
            var item = await db.Set<Item>().AsNoTracking().SingleAsync(x => x.Id == setup.ItemId, Ct);
            var customer = await db.Set<Customer>().AsNoTracking().SingleAsync(x => x.Id == setup.CustomerId, Ct);
            (itemCode, itemName, customerCode, customerName) = (item.Code, item.Name, customer.Code, customer.Name);
        }

        using (var created = await SendAsync(client, creator, setup.CompanyId, HttpMethod.Post, Route,
                   Rule(setup.ItemId, setup.CustomerId, "TWD", "2028-01-01", "2028-01-31", 5m)))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(itemCode, body.GetProperty("itemCode").GetString());
            Assert.Equal(customerName, body.GetProperty("customerName").GetString());
        }
        var defaultId = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.ItemId, null, "TWD", "2028-02-01", "2028-02-28", 6m));
        var deletedItemRule = await CreateAsync(client, creator, setup.CompanyId,
            Rule(setup.SecondItemId, null, "TWD", "2028-03-01", "2028-03-31", 7m));
        await using (var db = Context(setup.CompanyId))
        {
            await db.Set<Item>().Where(x => x.Id == setup.ItemId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct);
            await db.Set<Item>().Where(x => x.Id == setup.SecondItemId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsDeleted, true), Ct);
        }

        using (var list = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, Route + "?sort=id"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var rows = (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal((itemCode, itemName, customerCode, customerName), (rows[0].GetProperty("itemCode").GetString(),
                rows[0].GetProperty("itemName").GetString(), rows[0].GetProperty("customerCode").GetString(),
                rows[0].GetProperty("customerName").GetString()));
            Assert.Equal(itemCode, rows[1].GetProperty("itemCode").GetString()); // inactive item still named
            Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("customerCode").ValueKind);
            Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("customerName").ValueKind);
            Assert.Equal(JsonValueKind.Null, rows[2].GetProperty("itemCode").ValueKind); // soft-deleted item
            Assert.Equal(setup.SecondItemId, rows[2].GetProperty("itemId").GetInt64());
        }
        using (var detail = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, $"{Route}/{defaultId}"))
        {
            var body = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(itemName, body.GetProperty("itemName").GetString());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("customerName").ValueKind);
        }
        using (var detail = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, $"{Route}/{deletedItemRule}"))
            Assert.Equal(JsonValueKind.Null,
                (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("itemName").ValueKind);
    }

    [Fact]
    public async Task PricingUsersChooseActiveItemsCustomersAndCurrenciesWithoutMasterDataPermission()
    {
        // SD-PRICE-HTTP-013 (ADR-162): VIEW, CREATE or EDIT alone may list the choices a rule can be saved with —
        // active items, active approved customers and active currencies of the current company — with keyword search,
        // code order and paging; no MD.ITEM.VIEW / MD.CUSTOMER.VIEW needed. Inactive, unapproved, soft-deleted and
        // other-company records are never offered.
        var setup = await SetupAsync();
        var other = await SetupAsync();
        string itemCode, customerCode;
        long pendingCustomer, deletedCustomer;
        await using (var db = Context(setup.CompanyId))
        {
            itemCode = (await db.Set<Item>().AsNoTracking().SingleAsync(x => x.Id == setup.ItemId, Ct)).Code;
            customerCode = (await db.Set<Customer>().AsNoTracking().SingleAsync(x => x.Id == setup.CustomerId, Ct)).Code;
            await db.Set<Item>().Where(x => x.Id == setup.SecondItemId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, false), Ct);
            var pending = new Customer { CompanyId = setup.CompanyId, Code = "Z" + customerCode[1..],
                Name = "Pending customer", ReviewStatus = PartnerReviewStatus.Pending };
            var deleted = new Customer { CompanyId = setup.CompanyId, Code = "Y" + customerCode[1..],
                Name = "Deleted customer", ReviewStatus = PartnerReviewStatus.Approved, IsDeleted = true };
            db.AddRange(pending, deleted,
                new Currency { CompanyId = setup.CompanyId, Code = "JPY", Name = "Yen", DecimalPlaces = 0, IsActive = false });
            await db.SaveChangesAsync(Ct);
            (pendingCustomer, deletedCustomer) = (pending.Id, deleted.Id);
        }
        using var client = factory.CreateClient();
        foreach (var permission in new[] { "SAL.PRICING.VIEW", "SAL.PRICING.CREATE", "SAL.PRICING.EDIT" })
        {
            var token = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, permission));
            using (var items = await SendAsync(client, token, setup.CompanyId, HttpMethod.Get, Route + "/item-options"))
            {
                Assert.Equal(HttpStatusCode.OK, items.StatusCode);
                var body = await items.Content.ReadFromJsonAsync<JsonElement>(Ct);
                var rows = body.GetProperty("items").EnumerateArray().ToArray();
                Assert.Equal([setup.ItemId], rows.Select(x => x.GetProperty("id").GetInt64()));
                Assert.Equal(itemCode, rows[0].GetProperty("code").GetString());
                Assert.Equal("Price item", rows[0].GetProperty("name").GetString());
                Assert.Equal(1, body.GetProperty("totalCount").GetInt64());
            }
            using (var customers = await SendAsync(client, token, setup.CompanyId, HttpMethod.Get,
                       Route + "/customer-options"))
            {
                Assert.Equal(HttpStatusCode.OK, customers.StatusCode);
                var ids = (await customers.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items")
                    .EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
                Assert.Equal([setup.CustomerId], ids);
                Assert.DoesNotContain(pendingCustomer, ids);
                Assert.DoesNotContain(deletedCustomer, ids);
                Assert.DoesNotContain(other.CustomerId, ids);
            }
            using (var currencies = await SendAsync(client, token, setup.CompanyId, HttpMethod.Get,
                       Route + "/currency-options"))
            {
                Assert.Equal(HttpStatusCode.OK, currencies.StatusCode);
                var rows = (await currencies.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToArray();
                Assert.Equal(["TWD", "USD"], rows.Select(x => x.GetProperty("code").GetString()));
                Assert.Equal(2, rows[1].GetProperty("decimalPlaces").GetInt32());
                Assert.Equal("US dollar", rows[1].GetProperty("name").GetString());
            }
        }

        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.VIEW"));
        await using (var db = Context(setup.CompanyId))
        {
            await db.Set<Item>().Where(x => x.Id == setup.SecondItemId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, true), Ct);
        }
        using (var search = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get,
                   $"{Route}/item-options?keyword={itemCode[3..9]}&pageSize=1&page=1"))
        {
            var body = await search.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal([setup.ItemId], body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
            Assert.Equal(1, body.GetProperty("totalCount").GetInt64());
        }
        using (var paged = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get,
                   $"{Route}/item-options?pageSize=1&page=2"))
        {
            var body = await paged.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(2, body.GetProperty("totalCount").GetInt64());
            Assert.Single(body.GetProperty("items").EnumerateArray());
        }
        using (var bySearch = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get,
                   $"{Route}/customer-options?keyword=Price%20cust"))
            Assert.Equal([setup.CustomerId], (await bySearch.Content.ReadFromJsonAsync<JsonElement>(Ct))
                .GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
    }

    [Fact]
    public async Task PricingOptionsRejectAnonymousUnrelatedPermissionsAndBadPaging()
    {
        // SD-PRICE-HTTP-014 (ADR-162): anonymous 401; a quotation writer or MD viewer without a pricing permission is
        // 403; an unsupported sort or page size is 400; the three routes are documented in OpenAPI.
        var setup = await SetupAsync();
        using var client = factory.CreateClient();
        string[] paths = ["/item-options", "/customer-options", "/currency-options"];
        foreach (var path in paths)
            using (var anonymous = await client.GetAsync(Route + path, Ct))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var unrelated = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company,
            "SAL.QUOTATION.CREATE", "MD.ITEM.VIEW", "MD.CUSTOMER.VIEW"));
        foreach (var path in paths)
            using (var forbidden = await SendAsync(client, unrelated, setup.CompanyId, HttpMethod.Get, Route + path))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var viewer = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Self, "SAL.PRICING.VIEW"));
        using (var sort = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get, Route + "/item-options?sort=name"))
            Assert.Equal(HttpStatusCode.BadRequest, sort.StatusCode);
        using (var size = await SendAsync(client, viewer, setup.CompanyId, HttpMethod.Get,
                   Route + "/customer-options?pageSize=0"))
            Assert.Equal(HttpStatusCode.BadRequest, size.StatusCode);

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
        var documented = document.RootElement.GetProperty("paths");
        foreach (var path in paths)
        {
            var responses = documented.GetProperty(Route + path).GetProperty("get").GetProperty("responses");
            foreach (var status in new[] { "200", "401", "403" })
                Assert.True(responses.TryGetProperty(status, out _), path + " " + status);
        }
    }

    private static string Quote(long itemId, long customerId, string currency, string businessDate) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Route}/quote?itemId={itemId}&customerId={customerId}&currency={currency}&businessDate={businessDate}");

    /// <summary>Every rule row of the company with its version, so a read-only call can be proven write-free.</summary>
    private async Task<string> RuleStateAsync(long companyId)
    {
        await using var db = Context(companyId);
        var rows = await db.Set<SalesPriceRule>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Version, x.IsActive, x.IsDeleted, x.UnitPrice, x.UpdatedAt }).ToListAsync(Ct);
        return JsonSerializer.Serialize(rows);
    }

    private static async Task<long> CreateAsync(HttpClient client, string token, long companyId, object body)
    {
        using var response = await SendAsync(client, token, companyId, HttpMethod.Post, Route, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
    }

    private static object Edit(long itemId, long? customerId, string currencyCode, string validFrom, string validTo,
        decimal unitPrice, int version, decimal discountCeiling = 0m) => new
    {
        itemId, customerId, currencyCode, validFrom, validTo, unitPrice, discountCeiling, version
    };

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

    private sealed class ApiFactory(TestDatabase database, int? lockTimeoutMilliseconds = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
            if (lockTimeoutMilliseconds is { } timeout)
                builder.ConfigureTestServices(services => services.AddScoped(provider => new SalesPriceRuleService(
                    provider.GetRequiredService<MyErpDbContext>(), provider.GetRequiredService<SalesActionAccess>(),
                    provider.GetRequiredService<SalesCompanyAccess>(), provider.GetRequiredService<ISalesItemLookup>(),
                    provider.GetRequiredService<ICustomerLookup>(), provider.GetRequiredService<ICurrencyLookup>())
                {
                    LockTimeoutMilliseconds = timeout
                }));
        }
    }
}
