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
        foreach (var path in new[] { Periods, Periods + "/1", Journals, Journals + "/1" })
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
            Assert.False(route.TryGetProperty("post", out _), path);
            Assert.False(route.TryGetProperty("put", out _), path);
            Assert.False(route.TryGetProperty("delete", out _), path);
        }
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

    private async Task<HttpClient> LoginAsync(long companyId, string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var role = await services.GetRequiredService<RoleService>().CreateAsync(
            new RoleInput("G" + Guid.NewGuid().ToString("N")[..12], "GL role", null,
                DataScope.Company, false, permissions), Ct);
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
