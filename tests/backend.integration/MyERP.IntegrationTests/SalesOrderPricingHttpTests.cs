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

/// <summary>
/// WU-SD-005a11b (ADR-160): manual sales order create/edit re-select the price rule inside the save transaction and
/// persist line pricing provenance exactly as quotations do. SD-OPRICE-HTTP-001～005.
/// </summary>
public sealed class SalesOrderPricingHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/orders";
    private const string Day = "2028-02-29";
    private static readonly string[] Writer = ["SAL.ORDER.CREATE", "SAL.ORDER.EDIT", "SAL.ORDER.VIEW"];
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task RuleBackedOrderLinesSaveProvenanceAndDetailReturnsIt()
    {
        // SD-OPRICE-HTTP-001: hint matches the customer rule (default ignored); no hint still selects the rule;
        // discount equal to the ceiling is ordinary; response, detail and SQL agree on the Rule provenance.
        var setup = await SetupAsync();
        var defaultRule = await RuleAsync(setup, setup.ItemId, null, 80m, 5m);
        var customerRule = await RuleAsync(setup, setup.ItemId, setup.CustomerId, 100m, 10m, validTo: Day);
        var secondRule = await RuleAsync(setup, setup.SecondItemId, null, 7.5m, 0m);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, Writer));

        using var created = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route, Draft(setup,
            Line(1, setup.ItemId, 100m, 10m, customerRule.Id, customerRule.Version),
            Line(2, setup.SecondItemId, 7.5m, 0m)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetInt64();
        var first = body.GetProperty("lines")[0];
        Assert.Equal("Rule", first.GetProperty("pricingMode").GetString());
        Assert.Equal(customerRule.Id, first.GetProperty("pricingRuleId").GetInt64());
        Assert.Equal(1, first.GetProperty("pricingRuleVersion").GetInt32());
        Assert.Equal(100m, first.GetProperty("ruleUnitPrice").GetDecimal());
        Assert.Equal(10m, first.GetProperty("ruleDiscountCeiling").GetDecimal());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("overrideReason").ValueKind);

        using var detail = await SendAsync(client, token, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var second = (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("lines")[1];
        Assert.Equal("Rule", second.GetProperty("pricingMode").GetString());
        Assert.Equal(secondRule.Id, second.GetProperty("pricingRuleId").GetInt64());
        Assert.Equal(7.5m, second.GetProperty("ruleUnitPrice").GetDecimal());

        var lines = await LinesAsync(setup.CompanyId, id);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, x => Assert.Equal(SalesLinePricingMode.Rule, x.PricingMode));
        Assert.Equal(customerRule.Id, lines[0].PricingRuleId);
        Assert.NotEqual(defaultRule.Id, lines[0].PricingRuleId);
        Assert.Null(lines[0].OverrideBy);
        Assert.Null(lines[0].OverrideAt);
    }

    [Fact]
    public async Task OrderOverrideNeedsPermissionAndReasonAndRecordsActorTime()
    {
        // SD-OPRICE-HTTP-002: manual price, discount above ceiling and a missing rule each need SAL.PRICING.OVERRIDE
        // (403 with lineNo, nothing saved, number unused); with it the reason is required (400 field) and ≤ 200
        // characters; the saved line keeps the rule snapshot, the chosen price, reason, actor and time.
        var setup = await SetupAsync();
        var rule = await RuleAsync(setup, setup.ItemId, null, 100m, 10m);
        using var client = factory.CreateClient();
        var plain = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, Writer));
        foreach (var line in new[]
                 {
                     Line(1, setup.ItemId, 95m, 0m),
                     Line(1, setup.ItemId, 100m, 10.0001m),
                     Line(1, setup.SecondItemId, 5m, 0m, reason: "no rule"),
                 })
        {
            using var denied = await SendAsync(client, plain, setup.CompanyId, HttpMethod.Post, Route,
                Draft(setup, line));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var problem = await denied.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("SAL.PRICING.OVERRIDE_REQUIRED", problem.GetProperty("errorCode").GetString());
            Assert.Equal(1, problem.GetProperty("lineNo").GetInt32());
        }
        Assert.Equal(0, await CountAsync(setup.CompanyId));

        var overrideName = await UserAsync(setup.CompanyId, DataScope.Company, [.. Writer, "SAL.PRICING.OVERRIDE"]);
        var priced = await LoginAsync(client, overrideName);
        foreach (var reason in new string?[] { null, "   ", new string('x', 201) })
        {
            using var invalid = await SendAsync(client, priced, setup.CompanyId, HttpMethod.Post, Route,
                Draft(setup, Line(1, setup.ItemId, 95m, 0m, reason: reason)));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.True((await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct))
                .GetProperty("errors").TryGetProperty("lines[0].overrideReason", out _));
        }
        Assert.Equal(0, await CountAsync(setup.CompanyId));

        using var created = await SendAsync(client, priced, setup.CompanyId, HttpMethod.Post, Route, Draft(setup,
            Line(1, setup.ItemId, 95m, 12m, rule.Id, rule.Version, " volume deal "),
            Line(2, setup.SecondItemId, 5m, 0m, reason: "new product")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.EndsWith("001", body.GetProperty("documentNo").GetString());
        var lines = await LinesAsync(setup.CompanyId, body.GetProperty("id").GetInt64());
        Assert.Equal(SalesLinePricingMode.Override, lines[0].PricingMode);
        Assert.Equal(rule.Id, lines[0].PricingRuleId);
        Assert.Equal(100m, lines[0].RuleUnitPrice);
        Assert.Equal(95m, lines[0].UnitPrice);
        Assert.Equal("volume deal", lines[0].OverrideReason);
        Assert.Equal(await UserIdAsync(overrideName), lines[0].OverrideBy);
        Assert.NotNull(lines[0].OverrideAt);
        Assert.Equal(SalesLinePricingMode.Override, lines[1].PricingMode);
        Assert.Null(lines[1].PricingRuleId);
        Assert.Null(lines[1].RuleUnitPrice);
        Assert.Equal("new product", lines[1].OverrideReason);
        var reported = body.GetProperty("lines")[1];
        Assert.Equal("Override", reported.GetProperty("pricingMode").GetString());
        Assert.Equal("new product", reported.GetProperty("overrideReason").GetString());
        Assert.True(reported.GetProperty("overrideBy").GetInt64() > 0);
        Assert.NotEqual(JsonValueKind.Null, reported.GetProperty("overrideAt").ValueKind);
    }

    [Fact]
    public async Task StaleOrMalformedOrderHintsFailWithoutPartialSave()
    {
        // SD-OPRICE-HTTP-003: malformed pair 400 before 403; a hint whose rule was since edited is 409 STALE_QUOTE
        // with lineNo; inactive, other-currency and expired rules are never selected; a later line failure leaves
        // no order at all.
        var setup = await SetupAsync();
        var rule = await RuleAsync(setup, setup.ItemId, null, 100m, 0m);
        await RuleAsync(setup, setup.SecondItemId, null, 9m, 0m, active: false);
        await RuleAsync(setup, setup.SecondItemId, null, 9m, 0m, currency: "USD");
        await RuleAsync(setup, setup.SecondItemId, null, 9m, 0m, validFrom: "2028-01-01", validTo: "2028-02-28");
        using var client = factory.CreateClient();
        var plain = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, Writer));

        using var half = await SendAsync(client, plain, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup, Line(1, setup.SecondItemId, 9m, 0m, rule.Id, null)));
        Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);
        Assert.True((await half.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errors").TryGetProperty("lines[0].pricingRuleVersion", out _));
        using var versionOnly = await SendAsync(client, plain, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup, Line(1, setup.ItemId, 100m, 0m, null, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, versionOnly.StatusCode);
        Assert.True((await versionOnly.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errors").TryGetProperty("lines[0].pricingRuleId", out _));
        using var nonPositive = await SendAsync(client, plain, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup, Line(1, setup.ItemId, 100m, 0m, 0, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, nonPositive.StatusCode);

        await using (var db = Context(setup.CompanyId))
        {
            var saved = await db.Set<SalesPriceRule>().SingleAsync(x => x.Id == rule.Id, Ct);
            saved.UnitPrice = 101m;
            await db.SaveChangesAsync(Ct);
            Assert.Equal(2, saved.Version);
        }
        using var stale = await SendAsync(client, plain, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup, Line(1, setup.ItemId, 100m, 0m, rule.Id, 1)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("SAL.PRICING.STALE_QUOTE", staleBody.GetProperty("errorCode").GetString());
        Assert.Equal(1, staleBody.GetProperty("lineNo").GetInt32());

        using var noRule = await SendAsync(client, plain, setup.CompanyId, HttpMethod.Post, Route, Draft(setup,
            Line(1, setup.ItemId, 101m, 0m, rule.Id, 2), Line(2, setup.SecondItemId, 9m, 0m)));
        Assert.Equal(HttpStatusCode.Forbidden, noRule.StatusCode);
        var noRuleBody = await noRule.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("SAL.PRICING.OVERRIDE_REQUIRED", noRuleBody.GetProperty("errorCode").GetString());
        Assert.Equal(2, noRuleBody.GetProperty("lineNo").GetInt32());
        Assert.Equal(0, await CountAsync(setup.CompanyId));

        using var accepted = await SendAsync(client, plain, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup, Line(1, setup.ItemId, 101m, 0m, rule.Id, 2)));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.EndsWith("001", (await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("documentNo").GetString());
    }

    [Fact]
    public async Task OrderEditRedecidesLinesAndOverrideScopeMustCoverTheOrder()
    {
        // SD-OPRICE-HTTP-004: rule edits/deactivation leave the saved order line untouched; an edit re-selects
        // (stale hint 409, nothing changed); override on edit needs an OVERRIDE scope that covers the order owner;
        // an edit by the owner with a reason turns the line into Override.
        var setup = await SetupAsync();
        var rule = await RuleAsync(setup, setup.ItemId, null, 100m, 0m);
        using var client = factory.CreateClient();
        var ownerName = await UserAsync(setup.CompanyId, DataScope.Company, [.. Writer, "SAL.PRICING.OVERRIDE"]);
        var owner = await LoginAsync(client, ownerName);
        using var created = await SendAsync(client, owner, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup, Line(1, setup.ItemId, 100m, 0m, rule.Id, 1)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();

        await using (var db = Context(setup.CompanyId))
        {
            var saved = await db.Set<SalesPriceRule>().SingleAsync(x => x.Id == rule.Id, Ct);
            saved.UnitPrice = 120m;
            await db.SaveChangesAsync(Ct);
        }
        var kept = Assert.Single(await LinesAsync(setup.CompanyId, id));
        Assert.Equal((100m, 100m, 1), (kept.UnitPrice, kept.RuleUnitPrice!.Value, kept.PricingRuleVersion!.Value));

        using var stale = await SendAsync(client, owner, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}",
            new { version = 1, draft = Draft(setup, Line(1, setup.ItemId, 100m, 0m, rule.Id, 1)) });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("SAL.PRICING.STALE_QUOTE", await ErrorCodeAsync(stale));
        Assert.Equal(100m, Assert.Single(await LinesAsync(setup.CompanyId, id)).UnitPrice);

        // Self-scope override does not cover another owner's order, even with Company EDIT.
        var otherToken = await LoginAsync(client, await UserAsync(setup.CompanyId,
            (DataScope.Company, Writer), (DataScope.Self, ["SAL.PRICING.OVERRIDE"])));
        using var denied = await SendAsync(client, otherToken, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}",
            new { version = 1, draft = Draft(setup, Line(1, setup.ItemId, 90m, 0m, reason: "match competitor")) });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("SAL.PRICING.OVERRIDE_REQUIRED", await ErrorCodeAsync(denied));

        using var repriced = await SendAsync(client, otherToken, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}",
            new { version = 1, draft = Draft(setup, Line(1, setup.ItemId, 120m, 0m, rule.Id, 2)) });
        Assert.Equal(HttpStatusCode.OK, repriced.StatusCode);
        var line = Assert.Single(await LinesAsync(setup.CompanyId, id));
        Assert.Equal((SalesLinePricingMode.Rule, 2, 120m), (line.PricingMode, line.PricingRuleVersion!.Value, line.UnitPrice));

        using var overridden = await SendAsync(client, owner, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}",
            new { version = 2, draft = Draft(setup, Line(1, setup.ItemId, 110m, 0m, rule.Id, 2, "match competitor")) });
        Assert.Equal(HttpStatusCode.OK, overridden.StatusCode);
        var changed = Assert.Single(await LinesAsync(setup.CompanyId, id));
        Assert.Equal(SalesLinePricingMode.Override, changed.PricingMode);
        Assert.Equal((110m, 120m), (changed.UnitPrice, changed.RuleUnitPrice!.Value));
        Assert.Equal(await UserIdAsync(ownerName), changed.OverrideBy);

        await using (var db = Context(setup.CompanyId))
        {
            var saved = await db.Set<SalesPriceRule>().SingleAsync(x => x.Id == rule.Id, Ct);
            saved.IsActive = false;
            await db.SaveChangesAsync(Ct);
        }
        using var detail = await SendAsync(client, owner, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}");
        var reported = (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("lines")[0];
        Assert.Equal("Override", reported.GetProperty("pricingMode").GetString());
        Assert.Equal(rule.Id, reported.GetProperty("pricingRuleId").GetInt64());
        Assert.Equal("match competitor", reported.GetProperty("overrideReason").GetString());
    }

    [Fact]
    public async Task OrderLineSchemaRejectsInconsistentProvenanceAndKeepsLegacyRows()
    {
        // SD-OPRICE-HTTP-005: sal_order_line CHECK keeps provenance coherent; Legacy rows stay valid; the rule
        // reference is same-company.
        var setup = await SetupAsync();
        var rule = await RuleAsync(setup, setup.ItemId, null, 100m, 0m);
        var other = await SetupAsync();
        var foreignRule = await RuleAsync(other, other.ItemId, null, 100m, 0m);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, await UserAsync(setup.CompanyId, DataScope.Company, Writer));
        using var created = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup, Line(1, setup.ItemId, 100m, 0m)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();

        await using var db = Context(setup.CompanyId);
        async Task<bool> RejectedAsync(string set)
        {
            try
            {
                var sql = $"UPDATE dbo.sal_order_line SET {set} WHERE order_id = {id}";
                await db.Database.ExecuteSqlRawAsync(sql, Ct);
                return false;
            }
            catch (SqlException ex) when (ex.Number is 547)
            {
                return true;
            }
        }
        Assert.True(await RejectedAsync("pricing_rule_id = NULL, pricing_rule_version = NULL"));
        Assert.True(await RejectedAsync("unit_price = 99"));
        Assert.True(await RejectedAsync("pricing_mode = 'Override'"));
        Assert.True(await RejectedAsync("pricing_mode = 'Bogus'"));
        Assert.True(await RejectedAsync("override_reason = N'x'"));
        Assert.True(await RejectedAsync($"pricing_rule_id = {foreignRule.Id}"));
        Assert.False(await RejectedAsync(
            "pricing_mode = 'Legacy', pricing_rule_id = NULL, pricing_rule_version = NULL, " +
            "rule_unit_price = NULL, rule_discount_ceiling = NULL"));
        Assert.False(await RejectedAsync(
            $"pricing_mode = 'Override', pricing_rule_id = {rule.Id}, pricing_rule_version = 1, " +
            "rule_unit_price = 100, rule_discount_ceiling = 0, unit_price = 90, override_reason = N'deal', " +
            "override_by = 7, override_at = SYSDATETIME()"));
    }

    private sealed record Setup(long CompanyId, long ItemId, long SecondItemId, long CustomerId, long TaxRateId);

    private static object Draft(Setup setup, params LineBody[] lines) => new
    {
        customerId = setup.CustomerId, businessDate = Day, currencyCode = "TWD", remark = "pricing",
        lines = lines.Select(line => Merge(line, setup.TaxRateId)).ToArray()
    };

    private static LineBody Line(int lineNo, long itemId, decimal unitPrice, decimal discountRate,
        long? ruleId = null, int? ruleVersion = null, string? reason = null) => new(lineNo, itemId,
        unitPrice, discountRate, ruleId, ruleVersion, reason);

    private sealed record LineBody(int LineNo, long ItemId, decimal UnitPrice, decimal DiscountRate,
        long? RuleId, int? RuleVersion, string? Reason);

    private static object Merge(LineBody x, long taxRateId) => new
    {
        lineNo = x.LineNo, itemId = x.ItemId, quantity = 2m, unitPrice = x.UnitPrice,
        discountRate = x.DiscountRate, taxRateId,
        pricingRuleId = x.RuleId, pricingRuleVersion = x.RuleVersion, overrideReason = x.Reason
    };

    private async Task<SalesPriceRule> RuleAsync(Setup setup, long itemId, long? customerId, decimal price,
        decimal ceiling, string validFrom = "2028-01-01", string? validTo = null, string currency = "TWD",
        bool active = true)
    {
        await using var db = Context(setup.CompanyId);
        var rule = new SalesPriceRule
        {
            CompanyId = setup.CompanyId, ItemId = itemId, CustomerId = customerId, CurrencyCode = currency,
            ValidFrom = DateOnly.Parse(validFrom, CultureInfo.InvariantCulture),
            ValidTo = DateOnly.Parse(validTo ?? "2028-12-31", CultureInfo.InvariantCulture),
            UnitPrice = price, DiscountCeiling = ceiling, IsActive = active
        };
        db.Add(rule);
        await db.SaveChangesAsync(Ct);
        return rule;
    }

    private async Task<List<SalesOrderLine>> LinesAsync(long companyId, long orderId)
    {
        await using var db = Context(companyId);
        return await db.Set<SalesOrderLine>().AsNoTracking()
            .Where(x => x.OrderId == orderId).OrderBy(x => x.LineNo).ToListAsync(Ct);
    }

    private async Task<int> CountAsync(long companyId)
    {
        await using var db = Context(companyId);
        return await db.Set<SalesOrder>().IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, Ct);
    }

    private async Task<long> UserIdAsync(string username)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        return await db.Set<MyERP.Modules.System.Domain.SystemUser>().IgnoreQueryFilters().Where(x => x.Username == username)
            .Select(x => x.Id).SingleAsync(Ct);
    }

    private async Task<Setup> SetupAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "OP" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Order pricing", Ct);
        await using var db = Context(company.Id);
        var customer = new Customer { CompanyId = company.Id,
            Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Pricing customer", ReviewStatus = PartnerReviewStatus.Approved };
        var uom = new UnitOfMeasure { CompanyId = company.Id,
            Code = "U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Unit" };
        var taxCode = new TaxCode { CompanyId = company.Id, Code = "OUT", Name = "Output", TaxType = "Output" };
        db.AddRange(customer, uom, taxCode,
            new Currency { CompanyId = company.Id, Code = "TWD", Name = "Taiwan dollar", DecimalPlaces = 0, IsBase = true },
            new Currency { CompanyId = company.Id, Code = "USD", Name = "US dollar", DecimalPlaces = 2 },
            new NumberRule { CompanyId = company.Id, DocumentType = "SAL.ORDER", Name = "Order",
                Prefix = "OP" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never, SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        Item NewItem() => new() { CompanyId = company.Id,
            Code = "I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Pricing item", BaseUomId = uom.Id };
        var item = NewItem();
        var second = NewItem();
        var taxRate = new TaxRate { CompanyId = company.Id, TaxCodeId = taxCode.Id,
            EffectiveFrom = new DateOnly(2028, 1, 1), RatePercent = 5m };
        db.AddRange(item, second, taxRate);
        await db.SaveChangesAsync(Ct);
        return new Setup(company.Id, item.Id, second.Id, customer.Id, taxRate.Id);
    }

    private Task<string> UserAsync(long companyId, DataScope scope, params string[] permissions) =>
        UserAsync(companyId, (scope, permissions));

    /// <summary>One role per grant; SYS keeps the widest scope of each permission across roles.</summary>
    private async Task<string> UserAsync(long companyId, params (DataScope Scope, string[] Permissions)[] grants)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var roleIds = new List<long>();
        foreach (var (scope, permissions) in grants)
            roleIds.Add((await roles.CreateAsync(new RoleInput("O" + Guid.NewGuid().ToString("N")[..12],
                "Order pricing role", null, scope, false, permissions), Ct)).Id);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.ServiceProvider.GetRequiredService<SystemUserService>()
            .CreateAsync(username, Password, Ct);
        await roles.AssignAsync(user.Id, companyId, new UserCompanyAssignment(null, null, true, true, roleIds), Ct);
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
        [new SalesPriceRuleDbModel(), new SalesQuotationDbModel(), new SalesOrderDbModel(), new SystemOrganizationDbModel(), new UomDbModel(),
            new ItemDbModel(), new CustomerDbModel(), new PaymentSetupDbModel(), new CurrencyDbModel(), new TaxDbModel()],
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
