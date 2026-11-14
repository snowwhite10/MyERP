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
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// FI-COLL-OPEN-001～005（WU-FI-005c2a，ADR-132，REQ_F4116.5.9，BR-FI-162）：核銷表單用的客戶未清應收讀取 API。
/// 真實 SQL＋真實 SYS 授權：路由與 Swagger、只限 AR.SETTLEMENT.VIEW、只列本公司該客戶未清金額 &gt; 0 的應收、
/// 過帳日上限篩選、排序白名單與分頁、無效參數 400。應收直接寫入 FI 自有的 <c>ar_posted_receivable</c>。
/// </summary>
public sealed class OpenReceivableHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Root = "/api/v1/ar/collection-settlements/open-receivables";
    private const string Password = "Strong pass 2026!";
    private const long Customer = 810_001;
    private const long OtherCustomer = 810_002;
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
    public async Task RouteRequiresAuthenticationAndAppearsInOpenApi()
    {
        // FI-COLL-OPEN-001
        using var client = factory.CreateClient();
        using (var anonymous = await client.GetAsync($"{Root}?customerId=1", Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        var paths = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths");
        Assert.True(paths.GetProperty(Root).TryGetProperty("get", out _));
    }

    [Fact]
    public async Task OnlySettlementViewPermissionCanRead()
    {
        // FI-COLL-OPEN-002（權限）：AR.SETTLEMENT.VIEW 可讀；只有 CREATE、或只有收款權限 → 403。
        var setup = await SetupAsync();
        var viewer = await LoginAsync(setup.Company, ["AR.SETTLEMENT.VIEW"]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Root}?customerId={Customer}", Ct)).StatusCode);
        foreach (var permissions in new[] { new[] { "AR.SETTLEMENT.CREATE" }, ["AR.COLLECTION.VIEW", "AR.COLLECTION.CREATE"] })
        {
            var denied = await LoginAsync(setup.Company, permissions);
            using var response = await denied.GetAsync($"{Root}?customerId={Customer}", Ct);
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        }
    }

    [Fact]
    public async Task ListsOnlyOpenReceivablesOfTheCustomerInPostingDateOrder()
    {
        // FI-COLL-OPEN-003（正常／邊界／公司隔離）：只列該客戶未清 > 0 的應收；未清 0、其他客戶、其他公司同客戶 ID 不列；
        // 預設過帳日升冪（同日依 ID）；精確金額（小數 4 位）；原始與未清金額、來源類型與 ID、本位幣。
        var setup = await SetupAsync();
        var client = await LoginAsync(setup.Company, ["AR.SETTLEMENT.VIEW"]);
        var page = await ListAsync(client, $"?customerId={Customer}");
        Assert.Equal(3, page.GetProperty("totalCount").GetInt64());
        Assert.Equal([setup.Older, setup.SameDayFirst, setup.SameDaySecond], Ids(page));
        var first = page.GetProperty("items")[0];
        Assert.Equal(("Invoice", 9001L, "TWD", "2026-10-31", 1_000m, 250.1234m), (
            first.GetProperty("sourceType").GetString(), first.GetProperty("sourceId").GetInt64(),
            first.GetProperty("currency").GetString(), first.GetProperty("postingDate").GetString(),
            first.GetProperty("originalAmount").GetDecimal(), first.GetProperty("openAmount").GetDecimal()));

        var other = await ListAsync(client, $"?customerId={OtherCustomer}");
        Assert.Equal([setup.OtherCustomerOpen], Ids(other));
        var nobody = await ListAsync(client, "?customerId=999999");
        Assert.Equal(0, nobody.GetProperty("totalCount").GetInt64());
        Assert.Empty(Ids(nobody));
    }

    [Fact]
    public async Task FiltersByPostingDateSortsAndPages()
    {
        // FI-COLL-OPEN-004（篩選／排序／分頁）：postedOnOrBefore 含當日（核銷日之後過帳的應收不能核銷）；
        // -openAmount 降冪；openAmount 升冪；-postingDate；分頁第 2 頁與總數。
        var setup = await SetupAsync();
        var client = await LoginAsync(setup.Company, ["AR.SETTLEMENT.VIEW"]);
        Assert.Equal([setup.Older], Ids(await ListAsync(client, $"?customerId={Customer}&postedOnOrBefore=2026-11-01")));
        Assert.Equal([setup.Older, setup.SameDayFirst, setup.SameDaySecond],
            Ids(await ListAsync(client, $"?customerId={Customer}&postedOnOrBefore=2026-11-02")));
        Assert.Empty(Ids(await ListAsync(client, $"?customerId={Customer}&postedOnOrBefore=2026-10-30")));
        Assert.Equal([setup.SameDaySecond, setup.SameDayFirst, setup.Older],
            Ids(await ListAsync(client, $"?customerId={Customer}&sort=-openAmount")));
        Assert.Equal([setup.Older, setup.SameDayFirst, setup.SameDaySecond],
            Ids(await ListAsync(client, $"?customerId={Customer}&sort=openAmount")));
        Assert.Equal([setup.SameDaySecond, setup.SameDayFirst, setup.Older],
            Ids(await ListAsync(client, $"?customerId={Customer}&sort=-postingDate")));
        var second = await ListAsync(client, $"?customerId={Customer}&page=2&pageSize=2");
        Assert.Equal(3, second.GetProperty("totalCount").GetInt64());
        Assert.Equal([setup.SameDaySecond], Ids(second));
    }

    [Fact]
    public async Task InvalidParametersAre400()
    {
        // FI-COLL-OPEN-005（錯誤）：缺 customerId、customerId 0／負數、排序不在白名單、兩個排序欄位、pageSize 201 → 400 指明欄位。
        var setup = await SetupAsync();
        var client = await LoginAsync(setup.Company, ["AR.SETTLEMENT.VIEW"]);
        foreach (var (query, field) in new[]
                 {
                     ("", "customerId"), ("?customerId=0", "customerId"), ("?customerId=-5", "customerId"),
                     ($"?customerId={Customer}&sort=customerId", "sort"),
                     ($"?customerId={Customer}&sort=openAmount,-postingDate", "sort"),
                     ($"?customerId={Customer}&pageSize=201", "pageSize"),
                 })
        {
            using var response = await client.GetAsync(Root + query, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty(field, out _), query);
        }
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(Root + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static long[] Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("receivableId").GetInt64())];

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, body.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// 公司 A：客戶的應收 Older（10-31，原 1,000、未清 250.1234）、SameDayFirst（11-02，未清 400）、SameDaySecond
    /// （11-02，未清 700）、已清（未清 0）；另一客戶未清 500。公司 B：同客戶 ID 未清 900（不得出現）。
    /// </summary>
    private async Task<Setup> SetupAsync()
    {
        async Task<long> CompanyAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
                "OR" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Open receivable HTTP", Ct)).Id;
        }
        var companyId = await CompanyAsync();
        var otherCompany = await CompanyAsync();
        var ids = new List<long>();
        foreach (var (company, rows) in new[]
                 {
                     (companyId, new[]
                     {
                         (Customer, 9001L, 1_000m, 250.1234m, new DateOnly(2026, 10, 31)),
                         (Customer, 9002L, 400m, 400m, new DateOnly(2026, 11, 2)),
                         (Customer, 9003L, 700m, 700m, new DateOnly(2026, 11, 2)),
                         (Customer, 9004L, 300m, 0m, new DateOnly(2026, 10, 1)),
                         (OtherCustomer, 9005L, 500m, 500m, new DateOnly(2026, 10, 15)),
                     }),
                     (otherCompany, new[] { (Customer, 9006L, 900m, 900m, new DateOnly(2026, 10, 1)) }),
                 })
        {
            await using var db = new MyErpDbContext(
                new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
                [new CustomerCreditDbModel()], new FixedCompany(company));
            foreach (var customer in rows.Select(x => x.Item1).Distinct())
                db.Add(new CreditKey { CompanyId = company, CustomerId = customer });
            var entities = rows.Select(x => new PostedReceivable
            {
                CompanyId = company, CustomerId = x.Item1, SourceType = "Invoice", SourceId = x.Item2,
                BaseCurrency = "TWD", OriginalBaseAmount = x.Item3, OpenBaseAmount = x.Item4, PostingDate = x.Item5
            }).ToList();
            db.AddRange(entities);
            await db.SaveChangesAsync(Ct);
            ids.AddRange(entities.Select(x => x.Id));
        }
        return new(companyId, ids[0], ids[1], ids[2], ids[4]);
    }

    private async Task<HttpClient> LoginAsync(long companyId, string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("R" + Guid.NewGuid().ToString("N")[..12], "Open receivable role",
            null, DataScope.Company, false, permissions), Ct);
        var username = "or" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        await roles.AssignAsync(user.Id, companyId, new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        var client = factory.CreateClient();
        clients.Add(client);
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        return client;
    }

    private sealed record Setup(long Company, long Older, long SameDayFirst, long SameDaySecond, long OtherCustomerOpen);

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
