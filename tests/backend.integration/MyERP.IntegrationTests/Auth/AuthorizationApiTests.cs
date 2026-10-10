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
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-PERM-010～019：角色、權限代碼、公司存取與資料範圍（myerp_test + 本機 Redis）。</summary>
public sealed class AuthorizationApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private static readonly string[] MixedCasePermissions = ["sys.role.view", "SYS.ROLE.VIEW"];
    private static readonly string[] UnknownPermissions = ["SYS.NOPE.VIEW"];
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task PermissionIsRequiredInTheRequestedCompany()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(DataScope.Company, false, SystemPermissions.RoleView);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, companyA, viewer);
        using var client = factory.CreateClient();

        // 未登入 401；已登入但沒有公司上下文、或公司內無此權限 → 403 COMMON.PERMISSION.DENIED
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/roles", null, companyA)).StatusCode);
        var noCompany = await SendAsync(client, HttpMethod.Get, "/api/v1/sys/roles", token, null);
        Assert.Equal(HttpStatusCode.Forbidden, noCompany.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(noCompany));

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/roles", token, companyA)).StatusCode);
        var create = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/roles", token, companyA, NewRoleBody());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(create));

        // 不可存取的公司：任何已登入請求（即使不需權限）都被拒，避免偽造 X-Company-Id 繞過公司篩選
        var foreign = await SendAsync(client, HttpMethod.Get, "/api/v1/sys/auth/me", token, companyB);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("COMMON.COMPANY.ACCESS_DENIED", await ErrorCodeAsync(foreign));
    }

    [Fact]
    public async Task FullAccessRoleManagesRolesWithValidationDuplicatesAndVersioning()
    {
        var company = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(DataScope.Self, true);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, company, admin);
        using var client = factory.CreateClient();

        var code = "r" + Guid.NewGuid().ToString("N")[..10];
        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/roles", token, company,
            new { code, name = " Sales clerk ", dataScope = "Branch", isFullAccess = false, permissions = MixedCasePermissions });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code.ToUpperInvariant(), role.GetProperty("code").GetString());
        Assert.Equal("Sales clerk", role.GetProperty("name").GetString());
        Assert.Equal("Branch", role.GetProperty("dataScope").GetString());
        Assert.Equal(["SYS.ROLE.VIEW"], role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        var id = role.GetProperty("id").GetInt64();
        var version = role.GetProperty("version").GetInt32();

        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/roles", token, company, NewRoleBody(code.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(RoleService.DuplicateCode, await ErrorCodeAsync(duplicate));

        var unknown = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/roles", token, company,
            new { code = "X" + Guid.NewGuid().ToString("N")[..8], name = "x", dataScope = "Self", isFullAccess = false, permissions = UnknownPermissions });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var update = new { name = "Sales lead", description = "Leads", dataScope = "Company", isFullAccess = false, isActive = true, permissions = new[] { SystemPermissions.RoleEdit }, version };
        var updated = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/roles/{id}", token, company, update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal([SystemPermissions.RoleEdit], body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(version + 1, body.GetProperty("version").GetInt32());

        // 舊版本再改 → 409，權限集合不變
        var stale = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/roles/{id}", token, company, update with { permissions = new[] { SystemPermissions.RoleView } });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var reloaded = await (await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/roles/{id}", token, company)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal([SystemPermissions.RoleEdit], reloaded.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var catalog = await (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/permissions", token, company)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains(catalog.EnumerateArray(), p => p.GetProperty("code").GetString() == SystemPermissions.RoleDelete && p.GetProperty("module").GetString() == "SYS");

        var list = await (await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/roles?keyword={code}&sort=-name", token, company)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task DeleteRefusesRoleInUseAndSoftDeletesUnusedRole()
    {
        var company = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(DataScope.Company, true);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, company, admin);
        using var client = factory.CreateClient();

        var inUse = await SendAsync(client, HttpMethod.Delete, $"/api/v1/sys/roles/{admin}?version=1", token, company);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inUse.StatusCode);
        Assert.Equal(RoleService.RoleInUse, await ErrorCodeAsync(inUse));

        var unused = await CreateRoleAsync(DataScope.Self, false, SystemPermissions.RoleView);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Delete, $"/api/v1/sys/roles/{unused}?version=9", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/v1/sys/roles/{unused}?version=1", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/roles/{unused}", token, company)).StatusCode);
        await using var db = database.CreateRawContext();
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT CAST(is_deleted AS int) AS value FROM sys_role WHERE id = {unused}").SingleAsync(Ct));
    }

    [Fact]
    public async Task CurrentAuthorizationListsCompaniesAndWidestScopeAcrossActiveRoles()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var self = await CreateRoleAsync(DataScope.Self, false, SystemPermissions.RoleView, SystemPermissions.UserView);
        var branch = await CreateRoleAsync(DataScope.Branch, false, SystemPermissions.RoleView);
        var inactive = await CreateRoleAsync(DataScope.Company, false, SystemPermissions.RoleDelete);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, companyA, isDefault: false, self, branch, inactive);
        await AssignAsync(userId, companyB, isDefault: true, self);
        // 指派後才停用的角色不再授予任何權限
        await DeactivateRoleAsync(inactive);
        using var client = factory.CreateClient();

        // 沒帶公司 → 預設公司 B（排第一）
        var byDefault = await (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/auth/authorization", token, null)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(companyB, byDefault.GetProperty("companyId").GetInt64());
        Assert.Equal([companyB, companyA], byDefault.GetProperty("companies").EnumerateArray().Select(c => c.GetProperty("id").GetInt64()));
        Assert.True(byDefault.GetProperty("companies")[0].GetProperty("isDefault").GetBoolean());

        var inA = await (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/auth/authorization", token, companyA)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var scopes = inA.GetProperty("permissions").EnumerateArray()
            .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("dataScope").GetString());
        Assert.Equal(new Dictionary<string, string?> { [SystemPermissions.RoleView] = "Branch", [SystemPermissions.UserView] = "Self" }, scopes);
        Assert.False(inA.GetProperty("isFullAccess").GetBoolean());

        // 停用公司 A 後即不可存取，也不在清單內
        await using (var db = OrganizationContext(null))
        {
            var company = await db.Set<SystemCompany>().SingleAsync(c => c.Id == companyA, Ct);
            company.IsActive = false;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/auth/authorization", token, companyA)).StatusCode);
        var after = await (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/auth/authorization", token, null)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal([companyB], after.GetProperty("companies").EnumerateArray().Select(c => c.GetProperty("id").GetInt64()));
    }

    [Fact]
    public async Task UserWithoutCompaniesGetsEmptyAuthorization()
    {
        var (_, token) = await CreateSignedInUserAsync();
        using var client = factory.CreateClient();
        var body = await (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/auth/authorization", token, null)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("companyId").ValueKind);
        Assert.Equal(0, body.GetProperty("companies").GetArrayLength());
        Assert.Equal(0, body.GetProperty("permissions").GetArrayLength());
    }

    [Fact]
    public async Task AssignmentValidatesOrganizationAndReplacesRolesAndDefaultCompany()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var branchOfB = await CreateBranchAsync(companyB);
        var admin = await CreateRoleAsync(DataScope.Company, true);
        var viewer = await CreateRoleAsync(DataScope.Self, false, SystemPermissions.RoleView);
        var (adminId, token) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, companyA, admin);
        var (targetId, _) = await CreateSignedInUserAsync();
        using var client = factory.CreateClient();
        var path = $"/api/v1/sys/users/{targetId}/companies/{companyA}";

        var crossBranch = await SendAsync(client, HttpMethod.Put, path, token, companyA,
            new { branchId = branchOfB, departmentId = (long?)null, isDefault = true, isActive = true, roleIds = new[] { viewer } });
        Assert.Equal(HttpStatusCode.BadRequest, crossBranch.StatusCode);

        var badRole = await SendAsync(client, HttpMethod.Put, path, token, companyA,
            new { branchId = (long?)null, departmentId = (long?)null, isDefault = true, isActive = true, roleIds = new[] { long.MaxValue } });
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/users/{long.MaxValue}/companies/{companyA}", token, companyA,
            new { isDefault = false, isActive = true, roleIds = Array.Empty<long>() })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Put, path, token, companyA,
            new { isDefault = true, isActive = true, roleIds = new[] { viewer, admin } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Put, path, token, companyA,
            new { isDefault = true, isActive = true, roleIds = new[] { viewer } })).StatusCode);
        await AssignAsync(targetId, companyB, isDefault: true, viewer);

        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<UserAuthorizationStore>();
        var inA = await store.GetAsync(targetId, companyA, Ct);
        Assert.NotNull(inA);
        Assert.False(inA.IsFullAccess);
        Assert.Equal([SystemPermissions.RoleView], inA.Permissions.Keys);
        var companies = await store.ListCompaniesAsync(targetId, Ct);
        Assert.Single(companies, c => c.IsDefault);
        Assert.Equal(companyB, companies[0].Id);
    }

    [Fact]
    public async Task InactiveUserLinkHasNoAuthorization()
    {
        var company = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(DataScope.Self, false, SystemPermissions.RoleView);
        var (userId, _) = await CreateSignedInUserAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
        await roles.AssignAsync(userId, company, new UserCompanyAssignment(null, null, false, false, [viewer]), Ct);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserAuthorizationStore>().GetAsync(userId, company, Ct));
    }

    private static object NewRoleBody(string? code = null) =>
        new { code = code ?? "R" + Guid.NewGuid().ToString("N")[..10], name = "Role", dataScope = "Self", isFullAccess = false, permissions = new[] { SystemPermissions.RoleView } };

    private MyErpDbContext OrganizationContext(long? companyId) =>
        new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new SystemOrganizationDbModel()], new FixedCompany(companyId));

    private async Task<long> CreateCompanyAsync()
    {
        await using var db = OrganizationContext(null);
        var company = await new OrganizationService(db).CreateCompanyAsync("C" + Guid.NewGuid().ToString("N")[..12], "Company", Ct);
        return company.Id;
    }

    private async Task<long> CreateBranchAsync(long companyId)
    {
        await using var db = OrganizationContext(companyId);
        var branch = await new OrganizationService(db).CreateBranchAsync("B" + Guid.NewGuid().ToString("N")[..12], "Branch", Ct);
        return branch.Id;
    }

    private async Task<long> CreateRoleAsync(DataScope scope, bool fullAccess, params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var role = await services.ServiceProvider.GetRequiredService<RoleService>()
            .CreateAsync(new RoleInput("T" + Guid.NewGuid().ToString("N")[..12], "Test role", null, scope, fullAccess, permissions), Ct);
        return role.Id;
    }

    private async Task DeactivateRoleAsync(long roleId)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = await roles.GetAsync(roleId, Ct);
        await roles.UpdateAsync(roleId, new RoleUpdate(role.Name, null, role.DataScope, role.IsFullAccess, false, role.Permissions, role.Version), Ct);
    }

    private Task AssignAsync(long userId, long companyId, params long[] roleIds) => AssignAsync(userId, companyId, false, roleIds);

    private async Task AssignAsync(long userId, long companyId, bool isDefault, params long[] roleIds)
    {
        await using var services = factory.Services.CreateAsyncScope();
        await services.ServiceProvider.GetRequiredService<RoleService>()
            .AssignAsync(userId, companyId, new UserCompanyAssignment(null, null, isDefault, true, roleIds), Ct);
    }

    private async Task<(long Id, string AccessToken)> CreateSignedInUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var username = "perm" + Guid.NewGuid().ToString("N")[..20];
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (user.Id, body.GetProperty("accessToken").GetString()!);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, long? companyId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (companyId is { } id)
        {
            request.Headers.Add("X-Company-Id", id.ToString(CultureInfo.InvariantCulture));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
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
