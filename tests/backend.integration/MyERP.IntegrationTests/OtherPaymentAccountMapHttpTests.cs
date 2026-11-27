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

// 本檔原始 SQL 只由測試產生的數字 ID 組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests;

/// <summary>
/// FI-OP-MAP-HTTP-001～007（WU-FI-008b3b，REQ_F4116.4.2，BR-FI-060）：其他付款費用類別對照維護 HTTP。真實 SQL＋真實 API：
/// 路由與 Swagger、GL.OTHER_PAYMENT_MAP.VIEW／EDIT 分權、每個 MD 支出類別一列（含未設定、停用類別，排除收入類別）、
/// 單列建立／修改以列版本樂觀鎖、零變更不遞增、稽核、400 逐欄、404 類別、422 收入類別與非費用科目（AP 控制、暫收、預付、
/// 銀行、停用、非末級、不存在）、跨公司隔離、映射中的科目 MD 不可刪除。
/// </summary>
public sealed class OtherPaymentAccountMapHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Map = "/api/v1/gl/other-payment-account-map";
    private const string Password = "Strong pass 2026!";
    private static readonly string[] Editor = ["GL.OTHER_PAYMENT_MAP.VIEW", "GL.OTHER_PAYMENT_MAP.EDIT"];
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
        // FI-OP-MAP-HTTP-001：未登入 401；Swagger 有列表 GET 與單列 PUT，沒有 DELETE。
        using var client = factory.CreateClient();
        using (var get = await client.GetAsync(Map, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        using (var put = await client.PutAsJsonAsync($"{Map}/1", new { version = 0, expenseAccountCode = "6101" }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        var paths = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths");
        Assert.True(paths.GetProperty(Map).TryGetProperty("get", out _));
        var item = paths.GetProperty(Map + "/{expenseCategoryId}");
        Assert.True(item.TryGetProperty("put", out _));
        Assert.False(item.TryGetProperty("delete", out _));
    }

    [Fact]
    public async Task ViewAndEditAreSeparatePermissions()
    {
        // FI-OP-MAP-HTTP-002：VIEW 只可讀；只有 EDIT 不可讀；其他付款單據或收款映射權限都不能讀寫本對照。拒絕不寫資料。
        var setup = await SetupAsync();
        var viewer = await LoginAsync(setup.Company, ["GL.OTHER_PAYMENT_MAP.VIEW"]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Map, Ct)).StatusCode);
        await AssertDeniedAsync(await viewer.PutAsJsonAsync($"{Map}/{setup.Courier}", Body(0, "6101"), Ct));
        var editOnly = await LoginAsync(setup.Company, ["GL.OTHER_PAYMENT_MAP.EDIT"]);
        await AssertDeniedAsync(await editOnly.GetAsync(Map, Ct));
        var payer = await LoginAsync(setup.Company,
            ["AP.OTHER_PAYMENT.VIEW", "AP.OTHER_PAYMENT.CREATE", "GL.COLLECTION_MAP.VIEW", "GL.COLLECTION_MAP.EDIT"]);
        await AssertDeniedAsync(await payer.GetAsync(Map, Ct));
        await AssertDeniedAsync(await payer.PutAsJsonAsync($"{Map}/{setup.Courier}", Body(0, "6101"), Ct));
        Assert.Equal(0, await MapCountAsync(setup.Company));
    }

    [Fact]
    public async Task ListHasOneRowPerExpenseCategoryWithLabels()
    {
        // FI-OP-MAP-HTTP-003：每個支出類別（含停用）一列，依代碼排序；收入類別不列；未設定列 configured=false、version 0；
        // 已設定列帶 MD 科目代碼／名稱／可用狀態；他公司的對照不出現。
        var setup = await SetupAsync();
        var other = await SetupAsync();
        var editor = await LoginAsync(setup.Company, Editor);
        Assert.Equal(HttpStatusCode.OK, (await editor.PutAsJsonAsync($"{Map}/{setup.Courier}", Body(0, "6101"), Ct)).StatusCode);
        var otherEditor = await LoginAsync(other.Company, Editor);
        Assert.Equal(HttpStatusCode.OK,
            (await otherEditor.PutAsJsonAsync($"{Map}/{other.Rent}", Body(0, "6102"), Ct)).StatusCode);

        var rows = await ListAsync(editor);
        Assert.Equal(["COURIER", "OLDFEE", "RENT"], rows.Select(x => x.GetProperty("expenseCategoryCode").GetString()));
        var courier = rows[0];
        Assert.Equal((setup.Courier, "快遞費", true, true, 1, "6101", "Freight", true),
            (courier.GetProperty("expenseCategoryId").GetInt64(), courier.GetProperty("expenseCategoryName").GetString(),
                courier.GetProperty("expenseCategoryActive").GetBoolean(), courier.GetProperty("configured").GetBoolean(),
                courier.GetProperty("version").GetInt32(), courier.GetProperty("expenseAccount").GetProperty("code").GetString(),
                courier.GetProperty("expenseAccount").GetProperty("name").GetString(),
                courier.GetProperty("expenseAccount").GetProperty("isUsable").GetBoolean()));
        Assert.False(rows[1].GetProperty("expenseCategoryActive").GetBoolean());
        foreach (var unset in rows.Skip(1))
        {
            Assert.False(unset.GetProperty("configured").GetBoolean());
            Assert.Equal(0, unset.GetProperty("version").GetInt32());
            Assert.Equal(JsonValueKind.Null, unset.GetProperty("expenseAccount").ValueKind);
        }
    }

    [Fact]
    public async Task CreateUpdateAndZeroChangeFollowVersionAndAreAudited()
    {
        // FI-OP-MAP-HTTP-004：版本 0 建立（v1、changed）；同科目（不分大小寫、去空白）changed=false 不遞增；改科目 v2；
        // 舊版本與已存在時用 0 都 409；停用類別仍可設定；稽核 CREATE,UPDATE。
        var setup = await SetupAsync();
        var editor = await LoginAsync(setup.Company, Editor);
        var created = await SaveAsync(editor, setup.Rent, Body(0, " 6102 "));
        Assert.True(created.GetProperty("changed").GetBoolean());
        Assert.Equal((1, "6102"), (created.GetProperty("row").GetProperty("version").GetInt32(),
            created.GetProperty("row").GetProperty("expenseAccount").GetProperty("code").GetString()));
        var same = await SaveAsync(editor, setup.Rent, Body(1, "6102"));
        Assert.False(same.GetProperty("changed").GetBoolean());
        Assert.Equal(1, same.GetProperty("row").GetProperty("version").GetInt32());
        var updated = await SaveAsync(editor, setup.Rent, Body(1, "6101"));
        Assert.True(updated.GetProperty("changed").GetBoolean());
        Assert.Equal(2, updated.GetProperty("row").GetProperty("version").GetInt32());
        await AssertProblemAsync(await editor.PutAsJsonAsync($"{Map}/{setup.Rent}", Body(1, "6102"), Ct),
            HttpStatusCode.Conflict, "FI.OTHER_PAYMENT_MAP.VERSION_CONFLICT");
        await AssertProblemAsync(await editor.PutAsJsonAsync($"{Map}/{setup.Rent}", Body(0, "6102"), Ct),
            HttpStatusCode.Conflict, "FI.OTHER_PAYMENT_MAP.VERSION_CONFLICT");
        var inactive = await SaveAsync(editor, setup.OldFee, Body(0, "5101"));
        Assert.Equal(1, inactive.GetProperty("row").GetProperty("version").GetInt32());

        Assert.Equal($"{setup.Accounts["6101"]}|2", await SqlAsync<string>(
            $"SELECT CONCAT(expense_account_id, '|', version) AS [Value] FROM dbo.gl_other_payment_account_map WHERE company_id={setup.Company} AND expense_category_id={setup.Rent}"));
        Assert.Equal("CREATE,UPDATE,CREATE", await SqlAsync<string>(
            $"SELECT STRING_AGG(action, ',') WITHIN GROUP (ORDER BY id) AS [Value] FROM dbo.sys_audit_log WHERE table_name='gl_other_payment_account_map' AND company_id={setup.Company}"));
    }

    [Fact]
    public async Task InvalidInputCategoryAndAccountsAreRejectedWithoutWriting()
    {
        // FI-OP-MAP-HTTP-005：400 逐欄（空科目、負版本、超長）；404 不存在／他公司類別；422 收入類別；
        // 422 FI.OTHER_PAYMENT_MAP.ACCOUNT_INVALID 並帶 reason：不存在、停用、非末級、AP 控制（負債）、暫收（負債）、
        // 預付（資產）、銀行（資產）、收入科目。全部不寫入。
        var setup = await SetupAsync();
        var other = await SetupAsync();
        var editor = await LoginAsync(setup.Company, Editor);

        using (var invalid = await editor.PutAsJsonAsync($"{Map}/{setup.Courier}",
                   new { version = -1, expenseAccountCode = " " }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty("version", out _));
            Assert.True(errors.TryGetProperty("expenseAccountCode", out _));
        }
        Assert.Equal(HttpStatusCode.BadRequest,
            (await editor.PutAsJsonAsync($"{Map}/{setup.Courier}", Body(0, new string('6', 31)), Ct)).StatusCode);
        await AssertProblemAsync(await editor.PutAsJsonAsync($"{Map}/999999999", Body(0, "6101"), Ct),
            HttpStatusCode.NotFound, "FI.OTHER_PAYMENT_MAP.CATEGORY_NOT_FOUND");
        await AssertProblemAsync(await editor.PutAsJsonAsync($"{Map}/{other.Courier}", Body(0, "6101"), Ct),
            HttpStatusCode.NotFound, "FI.OTHER_PAYMENT_MAP.CATEGORY_NOT_FOUND");
        await AssertProblemAsync(await editor.PutAsJsonAsync($"{Map}/{setup.Income}", Body(0, "6101"), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT_MAP.CATEGORY_INVALID");

        foreach (var (code, reason) in new[]
                 {
                     ("9999", "MD.ACCOUNT.NOT_FOUND"), ("6199", "MD.ACCOUNT.INACTIVE"), ("6", "MD.ACCOUNT.NOT_POSTABLE"),
                     ("2101", "FI.OTHER_PAYMENT_MAP.NOT_EXPENSE_ACCOUNT"), ("2201", "FI.OTHER_PAYMENT_MAP.NOT_EXPENSE_ACCOUNT"),
                     ("1141", "FI.OTHER_PAYMENT_MAP.NOT_EXPENSE_ACCOUNT"), ("1102", "FI.OTHER_PAYMENT_MAP.NOT_EXPENSE_ACCOUNT"),
                     ("4101", "FI.OTHER_PAYMENT_MAP.NOT_EXPENSE_ACCOUNT")
                 })
        {
            using var response = await editor.PutAsJsonAsync($"{Map}/{setup.Courier}", Body(0, code), Ct);
            var problem = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity,
                "FI.OTHER_PAYMENT_MAP.ACCOUNT_INVALID");
            Assert.Equal(reason, problem.GetProperty("reason").GetString());
        }
        Assert.Equal(0, await MapCountAsync(setup.Company));
    }

    [Fact]
    public async Task MappedAccountIsInUseForMasterData()
    {
        // FI-OP-MAP-HTTP-006：對照中的科目（以 ID 儲存）在 MD 視為使用中，刪除 409 MD.ACCOUNT.IN_USE；未對照的科目可刪除。
        var setup = await SetupAsync();
        var editor = await LoginAsync(setup.Company, Editor);
        await SaveAsync(editor, setup.Courier, Body(0, "6101"));
        await AssertProblemAsync(await setup.Admin.DeleteAsync($"/api/v1/md/accounts/{setup.Accounts["6101"]}?version=1", Ct),
            HttpStatusCode.Conflict, "MD.ACCOUNT.IN_USE");
        Assert.Equal(HttpStatusCode.NoContent,
            (await setup.Admin.DeleteAsync($"/api/v1/md/accounts/{setup.Accounts["6102"]}?version=1", Ct)).StatusCode);
    }

    private static object Body(int version, string code) => new { version, expenseAccountCode = code };

    private static async Task<JsonElement> SaveAsync(HttpClient client, long categoryId, object body)
    {
        using var response = await client.PutAsJsonAsync($"{Map}/{categoryId}", body, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement[]> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Map, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray()];
    }

    private static async Task AssertDeniedAsync(HttpResponseMessage response)
    {
        using (response)
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }

    private Task<int> MapCountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.gl_other_payment_account_map WHERE company_id={companyId}");

    private async Task<T> SqlAsync<T>(string sql)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQueryRaw<T>(sql).SingleAsync(Ct);
    }

    private sealed record Setup(long Company, long Courier, long Rent, long OldFee, long Income,
        IReadOnlyDictionary<string, long> Accounts, HttpClient Admin);

    /// <summary>
    /// 公司經 SYS 服務建立；其餘經 MD API：科目 1 資產（1102 銀行、1141 預付）、2 負債（2101 應付、2201 暫收）、
    /// 4 收入（4101）、5 成本（5101）、6 費用（6101 運費、6102 租金、6199 停用）；支出類別 COURIER、RENT、OLDFEE（停用），
    /// 收入類別 SALEINC。
    /// </summary>
    private async Task<Setup> SetupAsync()
    {
        long companyId;
        await using (var scope = factory.Services.CreateAsyncScope())
            companyId = (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
                "OM" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Other payment map", Ct)).Id;
        var admin = await LoginAsync(companyId, [], fullAccess: true);
        var accounts = new Dictionary<string, long>(StringComparer.Ordinal);
        async Task AccountAsync(string code, string name, string? parent, string? type)
        {
            using var response = await admin.PostAsJsonAsync("/api/v1/md/accounts", new
            {
                code, name, parentId = parent is null ? (long?)null : accounts[parent], accountType = type
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            accounts[code] = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        foreach (var (code, name, type) in new[]
                 {
                     ("1", "Assets", "Asset"), ("2", "Liabilities", "Liability"), ("4", "Revenue", "Revenue"),
                     ("5", "Cost", "Cost"), ("6", "Expenses", "Expense")
                 })
            await AccountAsync(code, name, null, type);
        foreach (var (code, name, parent) in new[]
                 {
                     ("1102", "Bank", "1"), ("1141", "Prepaid", "1"), ("2101", "Payable", "2"), ("2201", "Unapplied", "2"),
                     ("4101", "Sales", "4"), ("5101", "Freight cost", "5"), ("6101", "Freight", "6"), ("6102", "Rent", "6"),
                     ("6199", "Old expense", "6")
                 })
            await AccountAsync(code, name, parent, null);
        using (var deactivate = await admin.PutAsJsonAsync($"/api/v1/md/accounts/{accounts["6199"]}",
                   new { name = "Old expense", balanceDirection = "Debit", isActive = false, version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        async Task<long> CategoryAsync(string code, string name, string direction)
        {
            using var response = await admin.PostAsJsonAsync("/api/v1/md/income-expense-categories",
                new { code, name, direction, ledgerAccountId = (long?)null }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        var courier = await CategoryAsync("COURIER", "快遞費", "Expense");
        var rent = await CategoryAsync("RENT", "租金", "Expense");
        var oldFee = await CategoryAsync("OLDFEE", "舊費用", "Expense");
        var income = await CategoryAsync("SALEINC", "雜項收入", "Income");
        using (var deactivate = await admin.PutAsJsonAsync($"/api/v1/md/income-expense-categories/{oldFee}",
                   new { name = "舊費用", direction = "Expense", ledgerAccountId = (long?)null, isActive = false, version = 1 },
                   Ct))
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        return new(companyId, courier, rent, oldFee, income, accounts, admin);
    }

    private async Task<HttpClient> LoginAsync(long companyId, string[] permissions, bool fullAccess = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("R" + Guid.NewGuid().ToString("N")[..12], "Other payment map role",
            null, DataScope.Company, fullAccess, permissions), Ct);
        var username = "om" + Guid.NewGuid().ToString("N")[..20];
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

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
