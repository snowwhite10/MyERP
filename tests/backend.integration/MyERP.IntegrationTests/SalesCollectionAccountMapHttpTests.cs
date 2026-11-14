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
using MyERP.Shared.Security;

// 本檔原始 SQL 只由測試產生的數字公司 ID 組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests;

/// <summary>
/// FI-COLL-MAP-HTTP-001～009（WU-FI-005a2d，ADR-123，REQ_F4116.1.8，BR-FI-154）：收款科目映射維護 HTTP。
/// 真實 SQL＋真實 API：未設定時明確回應、建立與修改以版本樂觀鎖、兩科目不得相同、科目經 MD 契約驗證（422 含欄位）、
/// 零變更不遞增版本、與 SD 發票映射應收科目不同時警告、獨立 VIEW／EDIT 權限、稽核、跨公司隔離，以及映射中的科目被 MD 視為使用中。
/// </summary>
public sealed class SalesCollectionAccountMapHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Map = "/api/v1/gl/sales-collection-account-map";
    private const string SalesMap = "/api/v1/gl/sales-invoice-account-map";
    private const string Accounts = "/api/v1/md/accounts";
    private const string Password = "Strong pass 2026!";
    private const string ReceivableWarning = "FI.COLLECTION_MAP.RECEIVABLE_DIFFERS_FROM_SALES_MAP";
    private static readonly string[] Editor = ["GL.COLLECTION_MAP.VIEW", "GL.COLLECTION_MAP.EDIT"];
    private static readonly string[] AccountAdmin = ["MD.ACCOUNT.VIEW", "MD.ACCOUNT.CREATE", "MD.ACCOUNT.EDIT", "MD.ACCOUNT.DELETE"];
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
    public async Task RoutesRequireAuthenticationAndAppearInOpenApi()
    {
        // FI-COLL-MAP-HTTP-001
        using var client = factory.CreateClient();
        using (var get = await client.GetAsync(Map, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        using (var put = await client.PutAsJsonAsync(Map, new { version = 0 }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        var route = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths").GetProperty(Map);
        Assert.True(route.TryGetProperty("get", out _));
        Assert.True(route.TryGetProperty("put", out _));
        Assert.False(route.TryGetProperty("delete", out _));
        Assert.False(route.TryGetProperty("post", out _));
    }

    [Fact]
    public async Task UnconfiguredCompanyReadsExplicitEmptyStateAndViewCannotWrite()
    {
        // FI-COLL-MAP-HTTP-002
        var companyId = await CompanyAsync();
        var viewer = await LoginAsync(companyId, ["GL.COLLECTION_MAP.VIEW"]);
        var empty = await ReadAsync(viewer);
        Assert.False(empty.GetProperty("configured").GetBoolean());
        Assert.Equal(0, empty.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("unappliedCashAccount").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("receivableAccount").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("salesInvoiceReceivableAccountCode").ValueKind);
        Assert.Equal(0, empty.GetProperty("warnings").GetArrayLength());
        using var denied = await viewer.PutAsJsonAsync(Map, Body(0, "2181", "1131"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var editOnly = await LoginAsync(companyId, ["GL.COLLECTION_MAP.EDIT"]);
        using var readDenied = await editOnly.GetAsync(Map, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, readDenied.StatusCode);
        Assert.Equal(0, await RowCountAsync(companyId));
    }

    [Fact]
    public async Task CreateUpdateAndZeroChangeFollowVersionAndAreAudited()
    {
        // FI-COLL-MAP-HTTP-003
        var companyId = await CompanyAsync();
        await AccountsAsync(companyId);
        var editor = await LoginAsync(companyId, Editor);

        using var created = await editor.PutAsJsonAsync(Map, Body(0, " 2181 ", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(first.GetProperty("changed").GetBoolean());
        var map = first.GetProperty("map");
        Assert.True(map.GetProperty("configured").GetBoolean());
        Assert.Equal(1, map.GetProperty("version").GetInt32());
        Assert.Equal("2181", map.GetProperty("unappliedCashAccount").GetProperty("code").GetString());
        Assert.Equal("Unapplied cash", map.GetProperty("unappliedCashAccount").GetProperty("name").GetString());
        Assert.True(map.GetProperty("receivableAccount").GetProperty("isPostable").GetBoolean());

        using var unchanged = await editor.PutAsJsonAsync(Map, Body(1, "2181", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        var same = await unchanged.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(same.GetProperty("changed").GetBoolean());
        Assert.Equal(1, same.GetProperty("map").GetProperty("version").GetInt32());

        using var updated = await editor.PutAsJsonAsync(Map, Body(1, "2182", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var second = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(second.GetProperty("changed").GetBoolean());
        Assert.Equal(2, second.GetProperty("map").GetProperty("version").GetInt32());

        // 舊版本、或以 0 再次建立、或超前版本都是 409，不改資料。
        foreach (var stale in new[] { 1, 0, 3 })
        {
            using var conflict = await editor.PutAsJsonAsync(Map, Body(stale, "2181", "1131"), Ct);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("FI.COLLECTION_MAP.VERSION_CONFLICT", await ErrorCodeAsync(conflict));
        }

        var saved = await ReadAsync(editor);
        Assert.Equal(2, saved.GetProperty("version").GetInt32());
        Assert.Equal("2182", saved.GetProperty("unappliedCashAccount").GetProperty("code").GetString());
        Assert.Equal("2182|1131|2", await SqlAsync<string>(
            $"SELECT CONCAT(unapplied_cash_account_code,'|',receivable_account_code,'|',version) AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={companyId}"));
        Assert.Equal("CREATE,UPDATE", await SqlAsync<string>(
            $"SELECT STRING_AGG(action, ',') WITHIN GROUP (ORDER BY id) AS [Value] FROM dbo.sys_audit_log WHERE table_name='gl_sales_collection_account_map' AND company_id={companyId}"));
    }

    [Fact]
    public async Task InvalidInputSameAccountsAndMdAccountsAreRejectedWithFieldNamesWithoutWriting()
    {
        // FI-COLL-MAP-HTTP-004
        var companyId = await CompanyAsync();
        await AccountsAsync(companyId);
        var editor = await LoginAsync(companyId, Editor);

        using var blank = await editor.PutAsJsonAsync(Map, Body(0, " ", new string('1', 31)), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        var errors = (await blank.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        foreach (var field in new[] { "unappliedCashAccountCode", "receivableAccountCode" })
            Assert.True(errors.TryGetProperty(field, out _), field);
        using var negative = await editor.PutAsJsonAsync(Map, Body(-1, "2181", "1131"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        // 兩科目相同（修剪後）：422 指向應收欄位，不寫資料。
        using var sameAccount = await editor.PutAsJsonAsync(Map, Body(0, "1131", " 1131"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sameAccount.StatusCode);
        var sameProblem = await sameAccount.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("FI.COLLECTION_MAP.ACCOUNT_INVALID", sameProblem.GetProperty("errorCode").GetString());
        var sameReasons = sameProblem.GetProperty("accountErrors");
        Assert.Equal("FI.COLLECTION_MAP.SAME_ACCOUNT", sameReasons.GetProperty("receivableAccountCode").GetString());
        Assert.False(sameReasons.TryGetProperty("unappliedCashAccountCode", out _));

        // 不存在、上層（不可過帳）、停用：一次回報全部欄位與 MD 原因。
        using var invalid = await editor.PutAsJsonAsync(Map, Body(0, "9999", "1"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("FI.COLLECTION_MAP.ACCOUNT_INVALID", problem.GetProperty("errorCode").GetString());
        var reasons = problem.GetProperty("accountErrors");
        Assert.Equal("MD.ACCOUNT.NOT_FOUND", reasons.GetProperty("unappliedCashAccountCode").GetString());
        Assert.Equal("MD.ACCOUNT.NOT_POSTABLE", reasons.GetProperty("receivableAccountCode").GetString());
        using var inactive = await editor.PutAsJsonAsync(Map, Body(0, "2183", "1131"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inactive.StatusCode);
        Assert.Equal("MD.ACCOUNT.INACTIVE", (await inactive.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accountErrors").GetProperty("unappliedCashAccountCode").GetString());
        Assert.Equal(0, await RowCountAsync(companyId));

        // 已設定後改成無效或相同科目也不改資料。
        using var created = await editor.PutAsJsonAsync(Map, Body(0, "2181", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        foreach (var (unapplied, receivable) in new[] { ("2183", "1131"), ("2181", "2181") })
        {
            using var rejected = await editor.PutAsJsonAsync(Map, Body(1, unapplied, receivable), Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        }
        Assert.Equal("2181|1131|1", await SqlAsync<string>(
            $"SELECT CONCAT(unapplied_cash_account_code,'|',receivable_account_code,'|',version) AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={companyId}"));
    }

    [Fact]
    public async Task ReceivableDifferentFromSalesInvoiceMapIsSavedWithWarning()
    {
        // FI-COLL-MAP-HTTP-005（ADR-120）：與 SD 發票映射應收科目不同時照常保存，但讀取與保存結果都帶警告與 SD 應收代碼。
        var companyId = await CompanyAsync();
        await AccountsAsync(companyId);
        var editor = await LoginAsync(companyId, [.. Editor, "GL.SALES_MAP.VIEW", "GL.SALES_MAP.EDIT"]);
        using (var sales = await editor.PutAsJsonAsync(SalesMap,
                   new { version = 0, receivableAccountCode = "1131", revenueAccountCode = "4101", outputTaxAccountCode = "2141" }, Ct))
            Assert.Equal(HttpStatusCode.OK, sales.StatusCode);

        var empty = await ReadAsync(editor);
        Assert.Equal("1131", empty.GetProperty("salesInvoiceReceivableAccountCode").GetString());
        Assert.Equal(0, empty.GetProperty("warnings").GetArrayLength());

        using var differs = await editor.PutAsJsonAsync(Map, Body(0, "2181", "1132"), Ct);
        Assert.Equal(HttpStatusCode.OK, differs.StatusCode);
        var saved = (await differs.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("map");
        Assert.Equal([ReceivableWarning], saved.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray());
        Assert.Equal("1131", saved.GetProperty("salesInvoiceReceivableAccountCode").GetString());
        Assert.Equal(1, await RowCountAsync(companyId));

        using var aligned = await editor.PutAsJsonAsync(Map, Body(1, "2181", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, aligned.StatusCode);
        Assert.Equal(0, (await aligned.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("map").GetProperty("warnings").GetArrayLength());

        // SD 映射之後改了應收科目 → 收款映射讀取時出現警告（版本不變）。
        using (var salesChanged = await editor.PutAsJsonAsync(SalesMap,
                   new { version = 1, receivableAccountCode = "1132", revenueAccountCode = "4101", outputTaxAccountCode = "2141" }, Ct))
            Assert.Equal(HttpStatusCode.OK, salesChanged.StatusCode);
        var read = await ReadAsync(editor);
        Assert.Equal(2, read.GetProperty("version").GetInt32());
        Assert.Equal([ReceivableWarning], read.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray());
    }

    [Fact]
    public async Task OtherCompanyNeitherSeesNorChangesTheMap()
    {
        // FI-COLL-MAP-HTTP-006
        var owner = await CompanyAsync();
        await AccountsAsync(owner);
        var ownerClient = await LoginAsync(owner, Editor);
        using var created = await ownerClient.PutAsJsonAsync(Map, Body(0, "2181", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var other = await CompanyAsync();
        await AccountsAsync(other);
        var otherClient = await LoginAsync(other, Editor);
        Assert.False((await ReadAsync(otherClient)).GetProperty("configured").GetBoolean());
        using var own = await otherClient.PutAsJsonAsync(Map, Body(0, "2182", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(1, (await own.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("map").GetProperty("version").GetInt32());
        Assert.Equal("2181|1", await SqlAsync<string>(
            $"SELECT CONCAT(unapplied_cash_account_code,'|',version) AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={owner}"));
    }

    [Fact]
    public async Task MappedAccountsAreInUseForMasterData()
    {
        // FI-COLL-MAP-HTTP-007（BR-FI-154）：映射引用的科目不可刪除、不可新增子科目；停用仍可，讀取時明示不可過帳。
        var companyId = await CompanyAsync();
        var ids = await AccountsAsync(companyId);
        var editor = await LoginAsync(companyId, [.. Editor, .. AccountAdmin]);
        using var created = await editor.PutAsJsonAsync(Map, Body(0, "2181", "1131"), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var delete = await editor.DeleteAsync($"{Accounts}/{ids["2181"]}?version=1", Ct);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("MD.ACCOUNT.IN_USE", await ErrorCodeAsync(delete));
        using var child = await editor.PostAsJsonAsync(Accounts,
            new { code = "11311", name = "Child", parentId = ids["1131"] }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, child.StatusCode);
        Assert.Equal("MD.ACCOUNT.PARENT_IN_USE", await ErrorCodeAsync(child));
        using var free = await editor.DeleteAsync($"{Accounts}/{ids["2182"]}?version=1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, free.StatusCode);

        using var deactivate = await editor.PutAsJsonAsync($"{Accounts}/{ids["2181"]}",
            new { name = "Unapplied cash", balanceDirection = "Credit", isActive = false, version = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var map = await ReadAsync(editor);
        Assert.Equal(1, map.GetProperty("version").GetInt32());
        Assert.False(map.GetProperty("unappliedCashAccount").GetProperty("isActive").GetBoolean());
        Assert.False(map.GetProperty("unappliedCashAccount").GetProperty("isPostable").GetBoolean());
        Assert.True(map.GetProperty("receivableAccount").GetProperty("isPostable").GetBoolean());
    }

    [Fact]
    public async Task ConcurrentFirstSavesCreateExactlyOneRow()
    {
        // FI-COLL-MAP-HTTP-008：兩人同時以版本 0 建立 → 一個 200、一個 409，只有一列。
        var companyId = await CompanyAsync();
        await AccountsAsync(companyId);
        var first = await LoginAsync(companyId, Editor);
        var second = await LoginAsync(companyId, Editor);
        var responses = await Task.WhenAll(
            first.PutAsJsonAsync(Map, Body(0, "2181", "1131"), Ct),
            second.PutAsJsonAsync(Map, Body(0, "2182", "1131"), Ct));
        try
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict],
                responses.Select(r => r.StatusCode).Order().ToArray());
            var loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("FI.COLLECTION_MAP.VERSION_CONFLICT", await ErrorCodeAsync(loser));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        Assert.Equal(1, await RowCountAsync(companyId));
        Assert.Equal(1, await SqlAsync<int>(
            $"SELECT version AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={companyId}"));
    }

    [Fact]
    public async Task CollectionAndSalesMapsHaveIndependentVersionsAndPermissions()
    {
        // FI-COLL-MAP-HTTP-009（ADR-120）：收款映射與 SD 發票映射分表，修改一方不改另一方版本；權限互不包含。
        var companyId = await CompanyAsync();
        await AccountsAsync(companyId);
        var editor = await LoginAsync(companyId, [.. Editor, "GL.SALES_MAP.VIEW", "GL.SALES_MAP.EDIT"]);
        using (var sales = await editor.PutAsJsonAsync(SalesMap,
                   new { version = 0, receivableAccountCode = "1131", revenueAccountCode = "4101", outputTaxAccountCode = "2141" }, Ct))
            Assert.Equal(HttpStatusCode.OK, sales.StatusCode);
        Assert.False((await ReadAsync(editor)).GetProperty("configured").GetBoolean());
        using (var created = await editor.PutAsJsonAsync(Map, Body(0, "2181", "1131"), Ct))
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using (var updated = await editor.PutAsJsonAsync(Map, Body(1, "2182", "1131"), Ct))
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(1, await SqlAsync<int>(
            $"SELECT version AS [Value] FROM dbo.gl_sales_invoice_account_map WHERE company_id={companyId}"));
        Assert.Equal(2, await SqlAsync<int>(
            $"SELECT version AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={companyId}"));

        var collectionOnly = await LoginAsync(companyId, ["GL.COLLECTION_MAP.VIEW"]);
        using (var salesDenied = await collectionOnly.GetAsync(SalesMap, Ct))
            Assert.Equal(HttpStatusCode.Forbidden, salesDenied.StatusCode);
        var salesOnly = await LoginAsync(companyId, ["GL.SALES_MAP.VIEW", "GL.SALES_MAP.EDIT"]);
        using (var collectionDenied = await salesOnly.GetAsync(Map, Ct))
            Assert.Equal(HttpStatusCode.Forbidden, collectionDenied.StatusCode);
    }

    private static object Body(int version, string? unapplied, string? receivable) => new
    {
        version,
        unappliedCashAccountCode = unapplied,
        receivableAccountCode = receivable
    };

    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Map, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    private Task<int> RowCountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.gl_sales_collection_account_map WHERE company_id={companyId}");

    private async Task<T> SqlAsync<T>(string sql)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQueryRaw<T>(sql).SingleAsync(Ct);
    }

    /// <summary>
    /// 經 MD API 建立科目：1131／1132 應收、2181／2182 暫收、2183 停用、2141 銷項稅、4101 收入、上層 1／2／4。
    /// </summary>
    private async Task<Dictionary<string, long>> AccountsAsync(long companyId)
    {
        var client = await LoginAsync(companyId, AccountAdmin);
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        async Task CreateAsync(string code, string name, string? parent, string? type)
        {
            using var response = await client.PostAsJsonAsync(Accounts, new
            {
                code,
                name,
                parentId = parent is null ? (long?)null : ids[parent],
                accountType = type
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids[code] = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        await CreateAsync("1", "Assets", null, "Asset");
        await CreateAsync("1131", "Receivable", "1", null);
        await CreateAsync("1132", "Other receivable", "1", null);
        await CreateAsync("2", "Liabilities", null, "Liability");
        await CreateAsync("2141", "Output tax", "2", null);
        await CreateAsync("2181", "Unapplied cash", "2", null);
        await CreateAsync("2182", "Other unapplied cash", "2", null);
        await CreateAsync("2183", "Old unapplied cash", "2", null);
        await CreateAsync("4", "Revenue", null, "Revenue");
        await CreateAsync("4101", "Sales", "4", null);
        using var deactivate = await client.PutAsJsonAsync($"{Accounts}/{ids["2183"]}",
            new { name = "Old unapplied cash", balanceDirection = "Credit", isActive = false, version = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        return ids;
    }

    private async Task<long> CompanyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "CM" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Collection map test", Ct);
        return company.Id;
    }

    private async Task<HttpClient> LoginAsync(long companyId, string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var role = await services.GetRequiredService<RoleService>().CreateAsync(
            new RoleInput("C" + Guid.NewGuid().ToString("N")[..12], "Collection map role", null,
                DataScope.Company, false, permissions), Ct);
        var username = "cm" + Guid.NewGuid().ToString("N")[..20];
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
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
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
