using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Seeding;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Infrastructure.Seeding;
using DataScope = MyERP.Shared.Security.DataScope;
using PermissionDefinition = MyERP.Shared.Security.PermissionDefinition;

namespace MyERP.IntegrationTests.Database;

/// <summary>SYS-SEED-001～006：種子資料（WU-SYS-013、ADR-023）。帳號來自 Api 的 appsettings.Development.json。</summary>
public sealed class SystemSeedTests(TestDatabase database) : IAsyncLifetime
{
    private static readonly string[] SeedUsernames =
        ["admin", "finance", "sales", "purchase", "warehouse", "production", "hr", "manager", "viewer"];

    private WebApplicationFactory<Program> factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => database.UseTestDatabase(b));
        await SeedAsync(factory);
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task SeedCreatesDemoOrganizationRolesAndUsersAndSecondRunAddsNothing()
    {
        var second = await SeedAsync(factory);
        Assert.Equal(0, second.Total);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var companies = await db.Set<SystemCompany>().IgnoreQueryFilters()
            .Where(c => c.Code == "TW01" || c.Code == "HK01").ToDictionaryAsync(c => c.Code, Ct);
        Assert.Equal(2, companies.Count);
        var tw = companies["TW01"].Id;
        var hk = companies["HK01"].Id;
        Assert.Equal(["KHH", "TPE", "TXG"], await BranchCodesAsync(db, tw));
        Assert.Equal(["HKG", "KLN"], await BranchCodesAsync(db, hk));
        Assert.Equal(14, await db.Set<SystemDepartment>().IgnoreQueryFilters().CountAsync(d => d.CompanyId == tw || d.CompanyId == hk, Ct));

        var users = await db.Set<SystemUser>().Where(u => SeedUsernames.Contains(u.Username)).ToListAsync(Ct);
        Assert.Equal(SeedUsernames.Length, users.Count);
        Assert.All(users, u => Assert.StartsWith("AQAAAA", u.PasswordHash, StringComparison.Ordinal));
        var userIds = users.Select(u => u.Id).ToList();
        var links = await db.Set<SystemUserCompany>().Where(l => userIds.Contains(l.UserId)).ToListAsync(Ct);
        Assert.Equal(SeedUsernames.Length * 2, links.Count);
        Assert.All(links.Where(l => l.CompanyId == tw), l => Assert.True(l.IsDefault));
        Assert.All(links.Where(l => l.CompanyId == hk), l => Assert.False(l.IsDefault));
        Assert.All(links, l => Assert.NotNull(l.BranchId));
        Assert.All(links, l => Assert.NotNull(l.DepartmentId));
        Assert.Equal(SeedUsernames.Length * 2, await db.Set<SystemUserRole>().CountAsync(r => userIds.Contains(r.UserId), Ct));

        var admin = await db.Set<SystemRole>().SingleAsync(r => r.Code == "ADMIN", Ct);
        Assert.True(admin.IsFullAccess);
        Assert.Equal(DataScope.Company, admin.DataScope);
        Assert.False(await db.Set<SystemRoleGrant>().AnyAsync(g => g.RoleId == admin.Id, Ct));
    }

    [Fact]
    public async Task SeededRolePermissionsFollowTheCatalogRules()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();

        var viewer = await GrantsAsync(db, "VIEWER");
        Assert.Equal(catalog.All.Where(p => p.Code.EndsWith(".VIEW", StringComparison.Ordinal)).Select(p => p.Code).Order(), viewer.Order());

        var finance = await GrantsAsync(db, "FINANCE");
        Assert.Contains("SYS.USER.VIEW", finance);
        Assert.Contains("SYS.APPROVAL.PROCESS", finance);
        Assert.DoesNotContain("SYS.USER.CREATE", finance);

        var sales = await GrantsAsync(db, "SALES");
        Assert.Equal(["SYS.APPROVAL.PROCESS", "SYS.APPROVAL.VIEW"], sales.Order());

        var manager = await GrantsAsync(db, "MANAGER");
        Assert.Contains("SYS.AUDIT.VIEW", manager);
        Assert.Contains("SYS.APPROVAL.PROCESS", manager);
        Assert.DoesNotContain("SYS.ROLE.EDIT", manager);
    }

    [Fact]
    public void RoleRulesCoverFutureModulePermissions()
    {
        PermissionDefinition[] future =
        [
            new("GL.JOURNAL.POST", "FI"), new("CO.BUDGET.EDIT", "CO"), new("SAL.ORDER.CREATE", "SD"), new("SAL.ORDER.APPROVE", "SD"),
            new("INV.STOCK.VIEW", "INV"), new("INV.TRANSFER.CREATE", "INV"), new("MD.SUPPLIER.EDIT", "MD"),
            new("MD.SUPPLIER_CONTACT.CREATE", "MD"), new("MD.CUSTOMER.EDIT", "MD"), new("BI.REPORT.EXPORT", "BI"),
            new("HR.EMPLOYEE.EDIT", "HR"), new("PP.WORK_ORDER.CREATE", "PP"), new("CRM.LEAD.CREATE", "CRM"),
        ];

        Assert.Equal(["CO.BUDGET.EDIT", "GL.JOURNAL.POST", "INV.STOCK.VIEW"], Granted("FINANCE"));
        Assert.Equal(["CRM.LEAD.CREATE", "INV.STOCK.VIEW", "SAL.ORDER.APPROVE", "SAL.ORDER.CREATE"], Granted("SALES"));
        Assert.Equal(["MD.SUPPLIER.EDIT", "MD.SUPPLIER_CONTACT.CREATE"], Granted("PURCHASE"));
        Assert.Equal(["INV.STOCK.VIEW", "INV.TRANSFER.CREATE"], Granted("WAREHOUSE"));
        Assert.Equal(["PP.WORK_ORDER.CREATE"], Granted("PRODUCTION"));
        Assert.Equal(["HR.EMPLOYEE.EDIT"], Granted("HR"));
        Assert.Equal(["BI.REPORT.EXPORT", "INV.STOCK.VIEW", "SAL.ORDER.APPROVE"], Granted("MANAGER"));
        Assert.Equal(["INV.STOCK.VIEW"], Granted("VIEWER"));

        string[] Granted(string role) =>
            [.. future.Where(SystemSeedData.Roles.Single(r => r.Code == role).Grants).Select(p => p.Code).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task SeedKeepsChangedPasswordsAndExtraGrants()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            await db.Set<SystemUser>().Where(u => u.Username == "production")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, "changed-by-user"), Ct);
            var hr = await db.Set<SystemRole>().SingleAsync(r => r.Code == "HR", Ct);
            db.Add(new SystemRoleGrant { RoleId = hr.Id, PermissionCode = "SYS.NUMBER_RULE.VIEW" });
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(0, (await SeedAsync(factory)).Total);

        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<MyErpDbContext>();
        Assert.Equal("changed-by-user", await verifyDb.Set<SystemUser>().Where(u => u.Username == "production").Select(u => u.PasswordHash).SingleAsync(Ct));
        Assert.Contains("SYS.NUMBER_RULE.VIEW", await GrantsAsync(verifyDb, "HR"));
    }

    [Fact]
    public async Task SeedRejectsUnknownRoleBeforeWritingAnything()
    {
        await using var invalid = factory.WithWebHostBuilder(b => b.UseSetting("SeedUsers:0:Role", "NOPE"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(invalid));
        Assert.Contains("SeedUsers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedCommandRunsTheRegisteredSystemSeederInDevelopment()
    {
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Contains(scope.ServiceProvider.GetServices<IDataSeeder>(), s => s is SystemDataSeeder);
        }

        await factory.Services.RunDataSeedAsync(Ct);
    }

    /// <summary>SYS-AUTH-017（後端）：種子帳號以真實 API 登入，admin 兩間公司全權；viewer 可查詢但不可新增。</summary>
    [Fact]
    public async Task SeededAccountsLogInThroughTheRealApiWithTheirRoles()
    {
        using var client = factory.CreateClient();

        var admin = await LoginAsync(client, "admin", "az5911");
        var authorization = await GetJsonAsync(client, admin, "/api/v1/sys/auth/authorization", companyId: null);
        Assert.True(authorization.GetProperty("isFullAccess").GetBoolean());
        var companies = authorization.GetProperty("companies").EnumerateArray().ToList();
        Assert.Contains(companies, c => c.GetProperty("code").GetString() == "TW01" && c.GetProperty("isDefault").GetBoolean());
        var hk = companies.Single(c => c.GetProperty("code").GetString() == "HK01").GetProperty("id").GetInt64();
        Assert.True((await GetJsonAsync(client, admin, "/api/v1/sys/auth/authorization", hk)).GetProperty("isFullAccess").GetBoolean());

        var viewer = await LoginAsync(client, "viewer", "Demo@2026");
        using var users = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sys/users");
        users.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewer);
        users.Headers.Add("X-Company-Id", hk.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(users, Ct)).StatusCode);

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sys/branches")
        {
            Content = JsonContent.Create(new { code = "X1", name = "X" }),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewer);
        create.Headers.Add("X-Company-Id", hk.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(create, Ct)).StatusCode);
    }

    private static async Task<SystemSeedResult> SeedAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDataSeeder>().SeedAsync(Ct);
    }

    private static Task<List<string>> BranchCodesAsync(MyErpDbContext db, long companyId) =>
        db.Set<SystemBranch>().IgnoreQueryFilters().Where(b => b.CompanyId == companyId).OrderBy(b => b.Code).Select(b => b.Code).ToListAsync(Ct);

    private static async Task<List<string>> GrantsAsync(MyErpDbContext db, string roleCode)
    {
        var roleId = await db.Set<SystemRole>().Where(r => r.Code == roleCode).Select(r => r.Id).SingleAsync(Ct);
        return await db.Set<SystemRoleGrant>().Where(g => g.RoleId == roleId).Select(g => g.PermissionCode).ToListAsync(Ct);
    }

    /// <summary>以 Development 萬用碼登入（MASTER_PROMPT 第 12 節）。</summary>
    private async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        CaptchaChallenge challenge;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        }

        var response = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password, captchaId = challenge.Id, captchaCode = "123456" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string accessToken, string url, long? companyId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (companyId is { } id)
        {
            request.Headers.Add("X-Company-Id", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
