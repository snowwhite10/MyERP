using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-ORG-020～026：公司／分店／部門 API 的權限、公司隔離、重複代碼與版本（myerp_test + 本機 Redis）。</summary>
public sealed class OrganizationApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task CompanyListOnlyShowsAuthorizedCompaniesAndWritesNeedPermissions()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(false, SystemPermissions.CompanyView, SystemPermissions.BranchView);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, companyA, viewer);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/companies", null, companyA)).StatusCode);
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/companies?pageSize=100", token, companyA));
        Assert.Equal([companyA], list.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetInt64()));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt64());

        var create = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/companies", token, companyA, new { code = NewCode(), name = "New" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(create));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/branches", token, companyA,
            new { code = NewCode(), name = "Branch" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/departments", token, companyA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/branches", token, companyA)).StatusCode);

        // 不可存取的公司作為上下文 → 403；可編輯但目標公司未授權 → 404（不洩漏是否存在）
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/companies", token, companyB)).StatusCode);
        var admin = await CreateRoleAsync(true);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, companyA, admin);
        var foreign = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/companies/{companyB}", adminToken, companyA,
            new { name = "Hijack", isActive = false, version = 1 });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("SYS.COMPANY.NOT_FOUND", await ErrorCodeAsync(foreign));
    }

    [Fact]
    public async Task CreatedCompanyIsGrantedToCreatorAndSupportsVersionedDeactivation()
    {
        var company = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(true);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, company, admin);
        using var client = factory.CreateClient();

        var code = NewCode();
        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/companies", token, company, new { code = code.ToLowerInvariant(), name = " Kowloon Ltd " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadAsync(created);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal("Kowloon Ltd", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        var id = body.GetProperty("id").GetInt64();
        var version = body.GetProperty("version").GetInt32();

        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/companies", token, company, new { code, name = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("SYS.COMPANY.DUPLICATE_CODE", await ErrorCodeAsync(duplicate));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/companies", token, company,
            new { code = "bad code", name = "x" })).StatusCode);

        // 建立者可立即切換到新公司，且沿用原公司的角色（全部權限）
        var inNew = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/auth/authorization", token, id));
        Assert.Equal(id, inNew.GetProperty("companyId").GetInt64());
        Assert.True(inNew.GetProperty("isFullAccess").GetBoolean());

        var update = new { name = "Kowloon Holdings", isActive = false, version };
        var updated = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/companies/{id}", token, company, update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = await ReadAsync(updated);
        Assert.False(after.GetProperty("isActive").GetBoolean());
        Assert.Equal(version + 1, after.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/companies/{id}", token, company, update)).StatusCode);

        // 停用後預設列表不含；includeInactive 才列出，並可重新啟用
        var active = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/companies?keyword={code}", token, company));
        Assert.Equal(0, active.GetProperty("totalCount").GetInt64());
        var all = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/companies?keyword={code}&includeInactive=true", token, company));
        Assert.Equal(id, Assert.Single(all.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/branches", token, id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/companies/{id}", token, company,
            new { name = "Kowloon Holdings", isActive = true, version = version + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/branches", token, id)).StatusCode);
    }

    [Fact]
    public async Task BranchesAreIsolatedPerCompanyWithDuplicateAndVersionChecks()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(true);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, companyA, admin);
        await AssignAsync(userId, companyB, admin);
        using var client = factory.CreateClient();

        var code = NewCode();
        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/branches", token, companyA, new { code, name = "Central" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var branch = await ReadAsync(created);
        var id = branch.GetProperty("id").GetInt64();

        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/branches", token, companyA, new { code, name = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("SYS.BRANCH.DUPLICATE_CODE", await ErrorCodeAsync(duplicate));
        // 代碼只需在公司內唯一
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/branches", token, companyB, new { code, name = "Central B" })).StatusCode);

        var inB = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/branches?keyword={code}", token, companyB));
        Assert.DoesNotContain(inB.GetProperty("items").EnumerateArray(), b => b.GetProperty("id").GetInt64() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/branches/{id}", token, companyB,
            new { name = "Hijack", isActive = true, version = 1 })).StatusCode);

        var version = branch.GetProperty("version").GetInt32();
        var updated = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/branches/{id}", token, companyA, new { name = "Central HQ", isActive = false, version });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Central HQ", (await ReadAsync(updated)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/branches/{id}", token, companyA,
            new { name = "Stale", isActive = true, version })).StatusCode);

        // 列表含已停用分店（維護畫面可重新啟用），支援排序
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/branches?keyword={code}&sort=-name", token, companyA));
        Assert.False(Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/branches?sort=createdAt", token, companyA)).StatusCode);
    }

    [Fact]
    public async Task DepartmentsValidateReferencesWithinTheCurrentCompany()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(true);
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, companyA, admin);
        await AssignAsync(userId, companyB, admin);
        using var client = factory.CreateClient();

        var branch = (await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/sys/branches", token, companyA, new { code = NewCode(), name = "Main" })))
            .GetProperty("id").GetInt64();
        var parentResponse = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/departments", token, companyA,
            new { code = NewCode(), name = "Finance", branchId = branch, parentDepartmentId = (long?)null });
        Assert.Equal(HttpStatusCode.Created, parentResponse.StatusCode);
        var parent = await ReadAsync(parentResponse);
        Assert.Equal(branch, parent.GetProperty("branchId").GetInt64());
        var parentId = parent.GetProperty("id").GetInt64();

        var childResponse = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/departments", token, companyA,
            new { code = NewCode(), name = "Accounts payable", branchId = (long?)null, parentDepartmentId = parentId });
        Assert.Equal(HttpStatusCode.Created, childResponse.StatusCode);
        var childId = (await ReadAsync(childResponse)).GetProperty("id").GetInt64();

        // 其他公司的分店／上級部門 → 400
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/departments", token, companyB,
            new { code = NewCode(), name = "Cross", branchId = branch, parentDepartmentId = (long?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/departments", token, companyB,
            new { code = NewCode(), name = "Cross", branchId = (long?)null, parentDepartmentId = parentId })).StatusCode);

        // 上級改成自己的下級 → 循環 400
        var version = parent.GetProperty("version").GetInt32();
        var cycle = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/departments/{parentId}", token, companyA,
            new { name = "Finance", branchId = branch, parentDepartmentId = childId, isActive = true, version });
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);

        var moved = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/departments/{parentId}", token, companyA,
            new { name = "Finance & Admin", branchId = (long?)null, parentDepartmentId = (long?)null, isActive = true, version });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(moved)).GetProperty("branchId").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/departments/{parentId}", token, companyA,
            new { name = "Stale", branchId = (long?)null, parentDepartmentId = (long?)null, isActive = true, version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/departments/{parentId}", token, companyB,
            new { name = "Hijack", branchId = (long?)null, parentDepartmentId = (long?)null, isActive = true, version = version + 1 })).StatusCode);

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/departments?pageSize=100", token, companyA));
        Assert.Equal(2, list.GetProperty("totalCount").GetInt64());
    }

    private static string NewCode() => "O" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private async Task<long> CreateCompanyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(NewCode(), "Company", Ct);
        return company.Id;
    }

    private async Task<long> CreateRoleAsync(bool fullAccess, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var role = await scope.ServiceProvider.GetRequiredService<RoleService>()
            .CreateAsync(new RoleInput("T" + Guid.NewGuid().ToString("N")[..12], "Test role", null, DataScope.Company, fullAccess, permissions), Ct);
        return role.Id;
    }

    private async Task AssignAsync(long userId, long companyId, params long[] roleIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>()
            .AssignAsync(userId, companyId, new UserCompanyAssignment(null, null, false, true, roleIds), Ct);
    }

    private async Task<(long Id, string AccessToken)> CreateSignedInUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var username = "org" + Guid.NewGuid().ToString("N")[..20];
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user.Id, (await ReadAsync(login)).GetProperty("accessToken").GetString()!);
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

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
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
