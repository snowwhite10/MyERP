using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

public sealed class GlReadHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Periods = "/api/v1/gl/periods";
    private const string Journals = "/api/v1/gl/journals";
    private const string Password = "Strong pass 2026!";
    private readonly List<HttpClient> clients = [];
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public ValueTask InitializeAsync() { factory = new ApiFactory(database); return ValueTask.CompletedTask; }
    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task ReadRoutesRequireAuthenticationAndAppearInOpenApi()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { Periods, Periods + "/1", Journals, Journals + "/1",
            "/api/v1/ar/sales-sources", "/api/v1/ar/sales-sources/1", "/api/v1/ar/sales-sources/by-key/Invoice/1" })
        {
            using var response = await client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        var paths = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths");
        foreach (var path in new[] { Periods, Periods + "/{id}", Journals, Journals + "/{id}" })
        {
            Assert.True(paths.TryGetProperty(path, out var route), path);
            Assert.True(route.TryGetProperty("get", out _), path);
            if (path.StartsWith(Journals, StringComparison.Ordinal))
            {
                Assert.False(route.TryGetProperty("post", out _), path);
                Assert.False(route.TryGetProperty("put", out _), path);
                Assert.False(route.TryGetProperty("delete", out _), path);
            }
        }
        foreach (var path in new[] { "/api/v1/ar/sales-sources", "/api/v1/ar/sales-sources/{id}",
            "/api/v1/ar/sales-sources/by-key/{sourceType}/{sourceId}" })
        {
            Assert.True(paths.TryGetProperty(path, out var route), path);
            Assert.True(route.TryGetProperty("get", out _), path);
            Assert.False(route.TryGetProperty("post", out _), path);
            Assert.False(route.TryGetProperty("put", out _), path);
            Assert.False(route.TryGetProperty("delete", out _), path);
        }
        Assert.True(paths.GetProperty(Periods).TryGetProperty("post", out _));
        Assert.True(paths.GetProperty(Periods + "/{id}").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty(Periods + "/{id}").TryGetProperty("delete", out _));
        Assert.True(paths.GetProperty(Periods + "/{id}/close").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty(Periods + "/{id}/reopen").TryGetProperty("post", out _));
    }

    [Fact]
    public async Task PeriodWriteRoutesUseSeparatePermissionsAndPreservePostedDates()
    {
        var companyId = await CompanyAsync();
        var owner = await LoginAsync(companyId, ["GL.PERIOD.CREATE", "GL.PERIOD.EDIT", "GL.PERIOD.CLOSE", "GL.PERIOD.REOPEN", "GL.PERIOD.DELETE"]);
        using var created = await owner.PostAsJsonAsync(Periods,
            new { startDate = "2026-03-01", endDate = "2026-03-31" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("period").GetProperty("id").GetInt64();
        Assert.True(body.GetProperty("changed").GetBoolean());
        var editor = await LoginAsync(companyId, ["GL.PERIOD.EDIT"]);
        using var invalidRange = await owner.PostAsJsonAsync(Periods,
            new { startDate = "2026-04-30", endDate = "2026-04-01" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);
        using var invalidVersion = await editor.PutAsJsonAsync($"{Periods}/{id}",
            new { version = 0, startDate = "2026-03-01", endDate = "2026-03-31" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode);
        using var deniedClose = await editor.PostAsJsonAsync($"{Periods}/{id}/close", new { version = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedClose.StatusCode);
        using var deniedState = await editor.PutAsJsonAsync($"{Periods}/{id}",
            new { version = 1, startDate = "2026-03-01", endDate = "2026-03-31", status = "Closed" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, deniedState.StatusCode);
        using var unchanged = await editor.PutAsJsonAsync($"{Periods}/{id}",
            new { version = 1, startDate = "2026-03-01", endDate = "2026-03-31" }, Ct);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.False((await unchanged.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changed").GetBoolean());
        using var closed = await owner.PostAsJsonAsync($"{Periods}/{id}/close", new { version = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.True((await closed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changed").GetBoolean());
        using var closeAgain = await owner.PostAsJsonAsync($"{Periods}/{id}/close", new { version = 2 }, Ct);
        Assert.Equal(HttpStatusCode.OK, closeAgain.StatusCode);
        Assert.False((await closeAgain.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changed").GetBoolean());
        using var stale = await owner.PostAsJsonAsync($"{Periods}/{id}/reopen", new { version = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var reopened = await owner.PostAsJsonAsync($"{Periods}/{id}/reopen", new { version = 2 }, Ct);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var foreign = await LoginAsync(await CompanyAsync(), ["GL.PERIOD.EDIT", "GL.PERIOD.DELETE"]);
        using var hidden = await foreign.DeleteAsync($"{Periods}/{id}?version=3", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var deleted = await owner.DeleteAsync($"{Periods}/{id}?version=3", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task PeriodWriteHttpRejectsOverlapAndPostedHistoryWithoutMutation()
    {
        var companyId = await CompanyAsync();
        var (periodId, journalId) = await SeedAsync(companyId);
        var client = await LoginAsync(companyId, ["GL.PERIOD.CREATE", "GL.PERIOD.EDIT", "GL.PERIOD.DELETE", "GL.PERIOD.VIEW"]);
        using var overlap = await client.PostAsJsonAsync(Periods,
            new { startDate = "2026-01-31", endDate = "2026-02-01" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        using var move = await client.PutAsJsonAsync($"{Periods}/{periodId}",
            new { version = 1, startDate = "2026-02-01", endDate = "2026-02-28" }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, move.StatusCode);
        using var deletion = await client.DeleteAsync($"{Periods}/{periodId}?version=1", Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deletion.StatusCode);
        using var unchanged = await client.GetAsync($"{Periods}/{periodId}", Ct);
        var saved = await unchanged.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, saved.GetProperty("version").GetInt32());
        Assert.Equal("2026-01-31", saved.GetProperty("endDate").GetString());
        using var journal = await client.GetAsync($"{Journals}/{journalId}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, journal.StatusCode);
    }

    [Fact]
    public async Task AuthorizedReadsReturnSavedRowsAndHideOtherCompany()
    {
        var owner = await CompanyAsync();
        var (periodId, journalId) = await SeedAsync(owner);
        var client = await LoginAsync(owner, ["GL.PERIOD.VIEW", "GL.JOURNAL.VIEW"]);
        using var periodPage = await client.GetAsync(Periods + "?status=Closed&from=2026-01-31&to=2026-01-31", Ct);
        Assert.Equal(HttpStatusCode.OK, periodPage.StatusCode);
        Assert.Equal(periodId, (await periodPage.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items")[0].GetProperty("id").GetInt64());
        using var periodDetail = await client.GetAsync($"{Periods}/{periodId}", Ct);
        Assert.Equal("Closed", (await periodDetail.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("status").GetString());
        using var journalPage = await client.GetAsync(Journals + "?sourceModule=FI&sourceType=TEST&sourceId=9001&accountCode=1000", Ct);
        Assert.Equal(HttpStatusCode.OK, journalPage.StatusCode);
        Assert.Equal(journalId, (await journalPage.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items")[0].GetProperty("id").GetInt64());
        using var journalDetail = await client.GetAsync($"{Journals}/{journalId}", Ct);
        Assert.Equal(HttpStatusCode.OK, journalDetail.StatusCode);
        var saved = await journalDetail.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(9001, saved.GetProperty("header").GetProperty("sourceId").GetInt64());
        Assert.Equal("Saved cash account", saved.GetProperty("lines")[0].GetProperty("accountName").GetString());
        Assert.Equal(2, saved.GetProperty("lines").GetArrayLength());
        using var empty = await client.GetAsync(Journals + "?accountCode=9999", Ct);
        Assert.Equal(0, (await empty.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        using var invalid = await client.GetAsync(Journals + "?from=2026-02-01&to=2026-01-01", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var other = await LoginAsync(await CompanyAsync(), ["GL.PERIOD.VIEW", "GL.JOURNAL.VIEW"]);
        using var hiddenPeriod = await other.GetAsync($"{Periods}/{periodId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hiddenPeriod.StatusCode);
        using var hiddenJournal = await other.GetAsync($"{Journals}/{journalId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hiddenJournal.StatusCode);
        var denied = await LoginAsync(owner, ["AR.CREDIT.VIEW"]);
        using var forbidden = await denied.GetAsync(Journals, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task ReadQueriesValidateBoundsAndReturnStableEmptyPages()
    {
        var companyId = await CompanyAsync();
        var (periodId, journalId) = await SeedAsync(companyId);
        var client = await LoginAsync(companyId, ["GL.PERIOD.VIEW", "GL.JOURNAL.VIEW"]);
        foreach (var path in new[]
        {
            Periods + "?page=0", Periods + "?pageSize=201", Periods + "?from=2026-02-01&to=2026-01-01",
            Periods + "?sort=unknown", Journals + "?periodId=0", Journals + "?sourceId=0",
            Journals + "?sourceModule=" + new string('X', 21), Journals + "?sort=unknown"
        })
        {
            using var response = await client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var path in new[] { Periods + "/0", Journals + "/0" })
        {
            using var response = await client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var periodPage = await client.GetAsync(Periods + "?page=2&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, periodPage.StatusCode);
        var periodResult = await periodPage.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(0, periodResult.GetProperty("items").GetArrayLength());
        Assert.True(periodResult.GetProperty("totalCount").GetInt64() >= 1);
        using var journalPage = await client.GetAsync(Journals + "?page=1&pageSize=200&sort=id", Ct);
        Assert.Equal(HttpStatusCode.OK, journalPage.StatusCode);
        var journalResult = await journalPage.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains(journalResult.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetInt64() == journalId);
        Assert.True(periodId > 0);
    }

    [Fact]
    public async Task SalesSourceReadShowsCommittedInvoiceCreditAndCompanyBoundary()
    {
        var companyId = await CompanyAsync();
        var (periodId, journalId) = await SeedAsync(companyId);
        var (invoiceId, creditId, sourceId) = await SeedSalesSourcesAsync(companyId, periodId, journalId);
        var owner = await LoginAsync(companyId, ["AR.SALES_SOURCE.VIEW"]);
        using var list = await owner.GetAsync($"/api/v1/ar/sales-sources?customerId=77&sourceType=Invoice&from=2026-01-01&to=2026-01-31", Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt64());
        Assert.Equal(invoiceId, page.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(70m, page.GetProperty("items")[0].GetProperty("openBaseAmount").GetDecimal());
        using var detail = await owner.GetAsync($"/api/v1/ar/sales-sources/by-key/Invoice/{sourceId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(invoiceId, body.GetProperty("source").GetProperty("id").GetInt64());
        Assert.Equal(1, body.GetProperty("allocations").GetArrayLength());
        Assert.Equal(30m, body.GetProperty("allocations")[0].GetProperty("baseGrossAmount").GetDecimal());
        using var credit = await owner.GetAsync($"/api/v1/ar/sales-sources/{creditId}", Ct);
        Assert.Equal(HttpStatusCode.OK, credit.StatusCode);
        var creditBody = await credit.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Return", creditBody.GetProperty("source").GetProperty("sourceType").GetString());
        Assert.Equal(1, creditBody.GetProperty("allocations").GetArrayLength());
        Assert.Equal(sourceId, creditBody.GetProperty("allocations")[0].GetProperty("originalSourceId").GetInt64());
        using var missing = await owner.GetAsync($"/api/v1/ar/sales-sources/by-key/Invoice/{sourceId + 99}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await owner.GetAsync("/api/v1/ar/sales-sources?customerId=0", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=201", "sort=unknown",
            "from=2026-02-01&to=2026-01-01", "sourceType=Unknown" })
        {
            using var bad = await owner.GetAsync("/api/v1/ar/sales-sources?" + query, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        using var empty = await owner.GetAsync("/api/v1/ar/sales-sources?customerId=88", Ct);
        Assert.Equal(0, (await empty.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        var foreign = await LoginAsync(await CompanyAsync(), ["AR.SALES_SOURCE.VIEW"]);
        using var hidden = await foreign.GetAsync($"/api/v1/ar/sales-sources/{invoiceId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var hiddenList = await foreign.GetAsync("/api/v1/ar/sales-sources", Ct);
        Assert.Equal(0, (await hiddenList.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        var selfScope = await LoginAsync(companyId, ["AR.SALES_SOURCE.VIEW"], DataScope.Self);
        using var scoped = await selfScope.GetAsync($"/api/v1/ar/sales-sources/{invoiceId}", Ct);
        Assert.Equal(HttpStatusCode.OK, scoped.StatusCode);
        var denied = await LoginAsync(companyId, ["AR.CREDIT.VIEW"]);
        using var forbidden = await denied.GetAsync("/api/v1/ar/sales-sources", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private async Task<(long InvoiceId, long CreditId, long SourceId)> SeedSalesSourcesAsync(long companyId,
        long periodId, long journalId)
    {
        await using var db = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new CustomerCreditDbModel(), new SalesSourceDbModel(), new GlPeriodDbModel(), new GlJournalDbModel()],
            new FixedCompany(companyId));
        var stamp = new DateTime(2026, 1, 31);
        db.Add(new CreditKey { CompanyId = companyId, CustomerId = 77, CreatedAt = stamp, CreatedBy = 7, Version = 1 });
        await db.SaveChangesAsync(Ct);
        var glKey = new GlSourceKey { CompanyId = companyId, SourceModule = "FI", SourceType = "TEST",
            SourceId = 9002, SourceVersion = 1, Fingerprint = new string('B', 64),
            Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        db.Add(glKey);
        await db.SaveChangesAsync(Ct);
        var creditJournal = new GlJournal { CompanyId = companyId, PeriodId = periodId, SourceKeyId = glKey.Id,
            BusinessDate = new DateOnly(2026, 1, 31), BaseCurrency = "TWD", TotalDebit = 30m,
            TotalCredit = 30m, Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        db.Add(creditJournal);
        await db.SaveChangesAsync(Ct);
        var sourceId = Random.Shared.NextInt64(9000000, 10000000);
        var invoice = new SalesSource { CompanyId = companyId, CustomerId = 77, SourceType = SalesSourceType.Invoice,
            SourceId = sourceId, SourceVersion = 1, Fingerprint = "test-invoice", GlJournalId = journalId,
            BusinessDate = new DateOnly(2026, 1, 31), Currency = "TWD", BaseCurrency = "TWD", ExchangeRate = 1m,
            NetAmount = 100m, GrossAmount = 100m, BaseNetAmount = 100m, BaseGrossAmount = 100m,
            Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        var credit = new SalesSource { CompanyId = companyId, CustomerId = 77, SourceType = SalesSourceType.Return,
            SourceId = sourceId + 1, SourceVersion = 1, Fingerprint = "test-credit", GlJournalId = creditJournal.Id,
            BusinessDate = new DateOnly(2026, 1, 31), Currency = "TWD", BaseCurrency = "TWD", ExchangeRate = 1m,
            NetAmount = 30m, GrossAmount = 30m, BaseNetAmount = 30m, BaseGrossAmount = 30m,
            Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        db.AddRange(invoice, credit);
        await db.SaveChangesAsync(Ct);
        var receivable = new PostedReceivable { CompanyId = companyId, CustomerId = 77, SourceType = "Invoice",
            SourceId = sourceId, BaseCurrency = "TWD", OriginalBaseAmount = 100m, OpenBaseAmount = 70m,
            PostingDate = new DateOnly(2026, 1, 31), Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        db.Add(receivable);
        await db.SaveChangesAsync(Ct);
        db.Add(new CreditAllocation { CompanyId = companyId, CreditSourceId = credit.Id, ReceivableId = receivable.Id,
            InvoiceLineId = sourceId + 10, Quantity = 1m, NetAmount = 30m, GrossAmount = 30m,
            BaseNetAmount = 30m, BaseGrossAmount = 30m, Version = 1, CreatedAt = stamp, CreatedBy = 7 });
        await db.SaveChangesAsync(Ct);
        return (invoice.Id, credit.Id, sourceId);
    }

    private async Task<long> CompanyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "GL" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "GL read test", Ct);
        return company.Id;
    }

    private async Task<(long, long)> SeedAsync(long companyId)
    {
        await using var db = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new GlPeriodDbModel(), new GlJournalDbModel()], new FixedCompany(companyId));
        var stamp = new DateTime(2026, 1, 31);
        var period = new GlPeriod { CompanyId = companyId, StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 31), Status = GlPeriodStatus.Closed,
            Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        db.Add(period);
        await db.SaveChangesAsync(Ct);
        var source = new GlSourceKey { CompanyId = companyId, SourceModule = "FI", SourceType = "TEST",
            SourceId = 9001, SourceVersion = 1, Fingerprint = new string('A', 64),
            Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        db.Add(source);
        await db.SaveChangesAsync(Ct);
        var journal = new GlJournal { CompanyId = companyId, PeriodId = period.Id, SourceKeyId = source.Id,
            BusinessDate = new DateOnly(2026, 1, 31), BaseCurrency = "TWD", TotalDebit = 25m,
            TotalCredit = 25m, Version = 1, CreatedAt = stamp, CreatedBy = 7 };
        db.Add(journal);
        await db.SaveChangesAsync(Ct);
        db.AddRange(
            new GlJournalLine { CompanyId = companyId, JournalId = journal.Id, Sequence = 1,
                AccountId = 101, AccountCode = "1000", AccountName = "Saved cash account",
                AccountVersion = 1, MappingVersion = 1, Debit = 25m, Credit = 0,
                Version = 1, CreatedAt = stamp, CreatedBy = 7 },
            new GlJournalLine { CompanyId = companyId, JournalId = journal.Id, Sequence = 2,
                AccountId = 102, AccountCode = "2000", AccountName = "Saved payable account",
                AccountVersion = 1, MappingVersion = 1, Debit = 0, Credit = 25m,
                Version = 1, CreatedAt = stamp, CreatedBy = 7 });
        await db.SaveChangesAsync(Ct);
        return (period.Id, journal.Id);
    }

    private async Task<HttpClient> LoginAsync(long companyId, string[] permissions, DataScope dataScope = DataScope.Company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var role = await services.GetRequiredService<RoleService>().CreateAsync(
            new RoleInput("G" + Guid.NewGuid().ToString("N")[..12], "GL role", null,
                dataScope, false, permissions), Ct);
        var username = "gl" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        await services.GetRequiredService<RoleService>().AssignAsync(user.Id, companyId,
            new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        var client = factory.CreateClient();
        clients.Add(client);
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client;
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
