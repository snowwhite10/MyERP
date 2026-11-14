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
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

// 本檔原始 SQL 只由測試產生的數字 ID 組成，沒有外部輸入。
#pragma warning disable EF1002

namespace MyERP.IntegrationTests;

/// <summary>
/// FI-COLL-HTTP-001～009（WU-FI-005b4a，ADR-129，REQ_F4116.1.12，BR-FI-160）：銷售收款單據 HTTP。真實 SQL＋真實 SYS
/// 授權、單號規則與審批引擎：路由與 Swagger、獨立 AR.COLLECTION.* 權限、建立／修改／刪除的版本與狀態、列表篩選排序分頁與
/// 公司隔離、送審／撤回、SYS 核准同交易過帳（Posted、未核銷＝金額、傳票）、駁回回可編輯、零步驟流程送審即過帳，以及
/// 過帳失敗時整個審批決定回滾、修正後可再核准。
/// </summary>
public sealed class SalesCollectionReceiptHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Root = "/api/v1/ar/sales-collections";
    private const string Password = "Strong pass 2026!";
    private const string BusinessDate = "2026-11-10";
    private static readonly string[] Writer =
        ["AR.COLLECTION.VIEW", "AR.COLLECTION.CREATE", "AR.COLLECTION.EDIT", "AR.COLLECTION.DELETE",
            "AR.COLLECTION.SUBMIT"];
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
        // FI-COLL-HTTP-001
        using var client = factory.CreateClient();
        using (var list = await client.GetAsync(Root, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using (var create = await client.PostAsJsonAsync(Root, new { amount = 1 }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        using (var submit = await client.PostAsJsonAsync($"{Root}/1/submit", new { version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, submit.StatusCode);
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        var paths = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths");
        var collection = paths.GetProperty(Root);
        Assert.True(collection.TryGetProperty("get", out _));
        Assert.True(collection.TryGetProperty("post", out _));
        var item = paths.GetProperty(Root + "/{id}");
        foreach (var verb in new[] { "get", "put", "delete" })
            Assert.True(item.TryGetProperty(verb, out _), verb);
        Assert.True(paths.GetProperty(Root + "/{id}/submit").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty(Root + "/{id}/withdraw").TryGetProperty("post", out _));
    }

    [Fact]
    public async Task EachActionNeedsItsOwnPermission()
    {
        // FI-COLL-HTTP-002（權限）：VIEW 只可讀；CREATE 不含讀；沒有 SUBMIT 不可送審／撤回；沒有 DELETE 不可刪除。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var draft = await CreateAsync(writer, setup, "P-1");
        var id = draft.GetProperty("id").GetInt64();

        var viewer = await LoginAsync(setup.Company, ["AR.COLLECTION.VIEW"]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Root, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Root}/{id}", Ct)).StatusCode);
        await AssertDeniedAsync(await viewer.PostAsJsonAsync(Root, Body(setup, "P-2"), Ct));
        await AssertDeniedAsync(await viewer.PutAsJsonAsync($"{Root}/{id}", Body(setup, "P-1", version: 1), Ct));
        await AssertDeniedAsync(await viewer.DeleteAsync($"{Root}/{id}?version=1", Ct));
        await AssertDeniedAsync(await viewer.PostAsJsonAsync($"{Root}/{id}/submit", new { version = 1 }, Ct));

        var creator = await LoginAsync(setup.Company, ["AR.COLLECTION.CREATE"]);
        await AssertDeniedAsync(await creator.GetAsync(Root, Ct));
        await AssertDeniedAsync(await creator.GetAsync($"{Root}/{id}", Ct));
        var editorOnly = await LoginAsync(setup.Company,
            ["AR.COLLECTION.VIEW", "AR.COLLECTION.EDIT", "AR.COLLECTION.CREATE"]);
        await AssertDeniedAsync(await editorOnly.PostAsJsonAsync($"{Root}/{id}/withdraw", new { version = 1 }, Ct));
        await AssertDeniedAsync(await editorOnly.DeleteAsync($"{Root}/{id}?version=1", Ct));

        Assert.Equal("Draft|1|0", await RowAsync(id));
    }

    [Fact]
    public async Task CreateUpdateDeleteFollowVersionValidationAndAudit()
    {
        // FI-COLL-HTTP-003（正常／邊界／衝突）：201＋Location、單號取自 SYS 規則、MD 名稱；修改遞增版本；舊版本 409；
        // 欄位錯誤 400 逐欄；幣別不符 422；銀行參考重複 409；刪除 204 後 404；稽核記錄。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);

        using var created = await writer.PostAsJsonAsync(Root, Body(setup, " TT-001 ", 1200.5m, "  首期  "), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = draft.GetProperty("id").GetInt64();
        Assert.EndsWith($"{Root}/{id}", created.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("RC202611", draft.GetProperty("documentNo").GetString(), StringComparison.Ordinal);
        Assert.Equal(("Draft", 1, "TT-001", "首期", "TWD", 1200.5m), (draft.GetProperty("status").GetString(),
            draft.GetProperty("version").GetInt32(), draft.GetProperty("bankReference").GetString(),
            draft.GetProperty("remark").GetString(), draft.GetProperty("currency").GetString(),
            draft.GetProperty("amount").GetDecimal()));
        Assert.Equal(("Collection customer", "BANK1"), (draft.GetProperty("customerName").GetString(),
            draft.GetProperty("bankAccountCode").GetString()));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("unappliedAmount").ValueKind);

        using var updated = await writer.PutAsJsonAsync($"{Root}/{id}", Body(setup, "TT-001", 900m, version: 1), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var second = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((2, 900m), (second.GetProperty("version").GetInt32(), second.GetProperty("amount").GetDecimal()));
        await AssertProblemAsync(await writer.PutAsJsonAsync($"{Root}/{id}", Body(setup, "TT-001", version: 1), Ct),
            HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");

        using (var invalid = await writer.PostAsJsonAsync(Root, new
               {
                   customerId = 0, businessDate = BusinessDate, amount = 0.00001m, bankAccountId = 0,
                   bankReference = " ", remark = new string('x', 501)
               }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            foreach (var field in new[] { "customerId", "amount", "bankAccountId", "bankReference", "remark" })
                Assert.True(errors.TryGetProperty(field, out _), field);
        }
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, "TT-USD", bank: setup.UsdBank), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.FUND_ACCOUNT_CURRENCY_MISMATCH");
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, "tt-001"), Ct),
            HttpStatusCode.Conflict, "FI.COLLECTION.BANK_REFERENCE_DUPLICATE");
        Assert.Equal(1, await CountAsync(setup.Company));

        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version=1", Ct),
            HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await writer.DeleteAsync($"{Root}/{id}?version=2", Ct)).StatusCode);
        await AssertProblemAsync(await writer.GetAsync($"{Root}/{id}", Ct), HttpStatusCode.NotFound,
            "FI.COLLECTION.RECEIPT_NOT_FOUND");
        Assert.Equal("Draft|3|1", await RowAsync(id));
        // 刪除釋出銀行參考：同參考可再建立。
        Assert.Equal(HttpStatusCode.Created, (await writer.PostAsJsonAsync(Root, Body(setup, "TT-001"), Ct)).StatusCode);
        Assert.True(await SqlAsync<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.sys_audit_log WHERE table_name='ar_collection_receipt_doc' AND company_id={setup.Company}") >= 3);
    }

    [Fact]
    public async Task ListFiltersSortsPagesAndHidesOtherCompanies()
    {
        // FI-COLL-HTTP-004（查詢／公司隔離）：狀態、客戶、資金帳戶、日期、關鍵字（單號或銀行參考）篩選；排序白名單；
        // 分頁總數；其他公司的單據不在列表、明細 404；無效篩選 400。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var a = (await CreateAsync(writer, setup, "LIST-A", 300m)).GetProperty("id").GetInt64();
        var b = (await CreateAsync(writer, setup, "LIST-B", 100m, date: "2026-11-12")).GetProperty("id").GetInt64();
        var c = (await CreateAsync(writer, setup, "OTHER-C", 200m, date: "2026-11-05")).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await writer.PostAsJsonAsync($"{Root}/{c}/submit", new { version = 1 }, Ct)).StatusCode);

        var all = await ListAsync(writer, "");
        Assert.Equal(3, all.GetProperty("totalCount").GetInt64());
        Assert.Equal([b, a, c], Ids(all)); // 預設業務日新到舊
        Assert.Equal([b, c, a], Ids(await ListAsync(writer, "?sort=amount")));
        Assert.Equal([c], Ids(await ListAsync(writer, "?status=Posted")));
        Assert.Equal([b, a], Ids(await ListAsync(writer, "?status=Draft")));
        Assert.Equal([b, a], Ids(await ListAsync(writer, "?keyword=list-")));
        Assert.Equal([a], Ids(await ListAsync(writer, "?from=2026-11-06&to=2026-11-11")));
        Assert.Equal(3, Ids(await ListAsync(writer, $"?customerId={setup.Customer}&bankAccountId={setup.Bank}")).Length);
        var page = await ListAsync(writer, "?page=2&pageSize=2");
        Assert.Equal([c], Ids(page));
        Assert.Equal(3L, page.GetProperty("totalCount").GetInt64());
        var posted = page.GetProperty("items")[0];
        Assert.Equal(("Posted", 200m, "Collection customer", "Mega TWD"), (posted.GetProperty("status").GetString(),
            posted.GetProperty("unappliedAmount").GetDecimal(), posted.GetProperty("customerName").GetString(),
            posted.GetProperty("bankAccountName").GetString()));

        foreach (var bad in new[] { "?sort=bankReference", "?status=Deleted", "?from=2026-11-12&to=2026-11-01",
                     "?customerId=0", "?pageSize=201" })
            Assert.Equal(HttpStatusCode.BadRequest, (await writer.GetAsync(Root + bad, Ct)).StatusCode);

        var other = await SetupAsync();
        var outsider = await LoginAsync(other.Company, Writer);
        Assert.Equal(0, (await ListAsync(outsider, "")).GetProperty("totalCount").GetInt64());
        await AssertProblemAsync(await outsider.GetAsync($"{Root}/{a}", Ct), HttpStatusCode.NotFound,
            "FI.COLLECTION.RECEIPT_NOT_FOUND");
        await AssertProblemAsync(await outsider.PutAsJsonAsync($"{Root}/{a}", Body(other, "X", version: 1), Ct),
            HttpStatusCode.NotFound, "FI.COLLECTION.RECEIPT_NOT_FOUND");
        await AssertProblemAsync(await outsider.PostAsJsonAsync($"{Root}/{a}/submit", new { version = 1 }, Ct),
            HttpStatusCode.NotFound, "FI.COLLECTION.RECEIPT_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitWithdrawAndSysFinalApprovalPostInTheSameDecision()
    {
        // FI-COLL-HTTP-005（狀態／正常）：多級流程送審 → Pending，不可修改或刪除（422）；撤回 → Draft；再送審；
        // 審批人核准 → 同一 SYS 決定內過帳：Posted、未核銷＝金額、一張傳票；已過帳不可撤回。
        var setup = await SetupAsync();
        var (approverId, approver) = await LoginWithIdAsync(setup.Company, ["SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS"]);
        await FlowAsync(setup, approverId);
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, "WF-1", 750.25m)).GetProperty("id").GetInt64();

        var submitted = await SubmitAsync(writer, id, 1, HttpStatusCode.OK);
        Assert.Equal(("Pending", 2, "Pending"), (submitted.GetProperty("status").GetString(),
            submitted.GetProperty("version").GetInt32(), submitted.GetProperty("approval").GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("posting").ValueKind);
        await AssertProblemAsync(await writer.PutAsJsonAsync($"{Root}/{id}", Body(setup, "WF-1", version: 2), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.RECEIPT_INVALID_STATUS");
        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version=2", Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.RECEIPT_INVALID_STATUS");
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{id}/submit", new { version = 2 }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.RECEIPT_INVALID_STATUS");

        using (var withdrawn = await writer.PostAsJsonAsync($"{Root}/{id}/withdraw", new { version = 2 }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
            var body = await withdrawn.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(("Draft", "Cancelled"), (body.GetProperty("status").GetString(),
                body.GetProperty("approval").GetProperty("status").GetString()));
        }
        var again = await SubmitAsync(writer, id, 3, HttpStatusCode.OK);
        var instance = again.GetProperty("approval").GetProperty("instanceId").GetInt64();

        using (var approved = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{instance}/approve",
                   new { comment = "OK", version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var detail = await ReadAsync(writer, id);
        Assert.Equal(("Posted", 750.25m), (detail.GetProperty("status").GetString(),
            detail.GetProperty("unappliedAmount").GetDecimal()));
        Assert.True(detail.GetProperty("journalId").GetInt64() > 0);
        Assert.Equal("750.2500|750.2500|1", await SqlAsync<string>(
            $"SELECT CONCAT(r.amount,'|',r.unapplied_amount,'|',(SELECT COUNT(*) FROM dbo.gl_journal j WHERE j.id=r.gl_journal_id)) AS [Value] FROM dbo.ar_collection_receipt r WHERE r.company_id={setup.Company} AND r.source_id={id}"));
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{id}/withdraw",
                new { version = detail.GetProperty("version").GetInt32() }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.RECEIPT_INVALID_STATUS");
    }

    [Fact]
    public async Task RejectionReturnsTheReceiptForEditingWithoutLedgerEffect()
    {
        // FI-COLL-HTTP-006（駁回）：SYS 駁回 → Rejected、無收款列與傳票；修改回 Draft 後可再送審。
        var setup = await SetupAsync();
        var (approverId, approver) = await LoginWithIdAsync(setup.Company, ["SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS"]);
        await FlowAsync(setup, approverId);
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, "WF-R")).GetProperty("id").GetInt64();
        var instance = (await SubmitAsync(writer, id, 1, HttpStatusCode.OK)).GetProperty("approval")
            .GetProperty("instanceId").GetInt64();

        using (var rejected = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{instance}/reject",
                   new { comment = "金額不符", version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var detail = await ReadAsync(writer, id);
        Assert.Equal("Rejected", detail.GetProperty("status").GetString());
        Assert.Equal(0, await PostedCountAsync(setup.Company));
        using var edited = await writer.PutAsJsonAsync($"{Root}/{id}",
            Body(setup, "WF-R", 480m, version: detail.GetProperty("version").GetInt32()), Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var draft = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal("Pending", (await SubmitAsync(writer, id, draft.GetProperty("version").GetInt32(),
            HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ZeroStepFlowPostsDuringSubmit()
    {
        // FI-COLL-HTTP-007（零步驟）：沒有啟用流程 → SYS 自動核准，送審回應即 Posted 並含過帳結果。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, "WF-0", 99m)).GetProperty("id").GetInt64();

        var result = await SubmitAsync(writer, id, 1, HttpStatusCode.OK);

        Assert.Equal(("Posted", "Approved"), (result.GetProperty("status").GetString(),
            result.GetProperty("approval").GetProperty("status").GetString()));
        var posting = result.GetProperty("posting");
        Assert.Equal((99m, 99m), (posting.GetProperty("amount").GetDecimal(), posting.GetProperty("unappliedAmount").GetDecimal()));
        Assert.Equal(1, await PostedCountAsync(setup.Company));
    }

    [Fact]
    public async Task PostingFailureRollsBackTheWholeDecisionAndCanBeRetried()
    {
        // FI-COLL-HTTP-008（錯誤）：核准時資金帳戶已停用 → 422，審批輪次仍 Pending、單據仍 Pending、無收款列／傳票；
        // 重新啟用後同一輪次再核准成功過帳。送審時資金帳戶停用 → 422，不建 SYS 輪次。
        var setup = await SetupAsync();
        var (approverId, approver) = await LoginWithIdAsync(setup.Company, ["SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS"]);
        await FlowAsync(setup, approverId);
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, "WF-F", 300m)).GetProperty("id").GetInt64();
        var instance = (await SubmitAsync(writer, id, 1, HttpStatusCode.OK)).GetProperty("approval")
            .GetProperty("instanceId").GetInt64();

        var second = (await CreateAsync(writer, setup, "WF-F2")).GetProperty("id").GetInt64();
        await SetFundActiveAsync(setup, false);
        await AssertProblemAsync(await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{instance}/approve",
                new { comment = "OK", version = 1 }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION_POSTING.FUND_ACCOUNT_INVALID");
        Assert.Equal("Pending|2|0", await RowAsync(id));
        Assert.Equal("Pending", await SqlAsync<string>(
            $"SELECT status AS [Value] FROM dbo.sys_approval_instance WHERE id={instance}"));
        Assert.Equal(0, await PostedCountAsync(setup.Company));

        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{second}/submit", new { version = 1 }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.FUND_ACCOUNT_INACTIVE");
        Assert.Equal("Draft|1|0", await RowAsync(second));
        Assert.Equal(1, await SqlAsync<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.sys_approval_instance WHERE company_id={setup.Company} AND document_type='AR.COLLECTION'"));

        await SetFundActiveAsync(setup, true);
        using (var approved = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{instance}/approve",
                   new { comment = "Retry", version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Posted", (await ReadAsync(writer, id)).GetProperty("status").GetString());
        Assert.Equal(1, await PostedCountAsync(setup.Company));
    }

    [Fact]
    public async Task MissingNumberRuleRejectsCreationWithoutSavingTheReceipt()
    {
        // FI-COLL-HTTP-009（設定缺失）：公司沒有 AR.COLLECTION 單號規則 → 422 SYS.NUMBER_RULE.NOT_CONFIGURED，不寫資料。
        var setup = await SetupAsync(numberRule: false);
        var writer = await LoginAsync(setup.Company, Writer);
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, "NR-1"), Ct),
            HttpStatusCode.UnprocessableEntity, "SYS.NUMBER_RULE.NOT_CONFIGURED");
        Assert.Equal(0, await CountAsync(setup.Company));
    }

    private sealed record Setup(long Company, long Customer, long Bank, long UsdBank, HttpClient Admin);

    private static object Body(Setup setup, string reference, decimal amount = 500m, string? remark = null,
        int? version = null, long? bank = null, string date = BusinessDate) => version is { } v
        ? new { version = v, customerId = setup.Customer, businessDate = date, amount, bankAccountId = bank ?? setup.Bank,
            bankReference = reference, remark }
        : new { customerId = setup.Customer, businessDate = date, amount, bankAccountId = bank ?? setup.Bank,
            bankReference = reference, remark };

    private static async Task<JsonElement> CreateAsync(HttpClient client, Setup setup, string reference,
        decimal amount = 500m, string date = BusinessDate)
    {
        using var response = await client.PostAsJsonAsync(Root, Body(setup, reference, amount, date: date), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> SubmitAsync(HttpClient client, long id, int version, HttpStatusCode expected)
    {
        using var response = await client.PostAsJsonAsync($"{Root}/{id}/submit", new { version }, Ct);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, long id)
    {
        using var response = await client.GetAsync($"{Root}/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(Root + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static long[] Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64())];

    private static async Task AssertDeniedAsync(HttpResponseMessage response)
    {
        using (response)
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(code, body.GetProperty("errorCode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        }
    }

    private Task<string> RowAsync(long id) => SqlAsync<string>(
        $"SELECT CONCAT(status,'|',version,'|',CAST(is_deleted AS int)) AS [Value] FROM dbo.ar_collection_receipt_doc WHERE id={id}");

    private Task<int> CountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_receipt_doc WHERE company_id={companyId} AND is_deleted=0");

    private Task<int> PostedCountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_receipt WHERE company_id={companyId}");

    private async Task<T> SqlAsync<T>(string sql)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQueryRaw<T>(sql).SingleAsync(Ct);
    }

    /// <summary>
    /// 公司＋本位幣 TWD＋USD、已核准客戶、AR.COLLECTION 單號規則（RC＋年月＋4 碼，可略）直接寫入；其餘經 API：
    /// MD 科目（1102 銀行、1131 應收、2181 暫收）、資金帳戶 BANK1（TWD→1102）與 BANKUSD、2026-11 GL 開放期間、收款科目映射。
    /// </summary>
    private async Task<Setup> SetupAsync(bool numberRule = true)
    {
        long companyId;
        await using (var scope = factory.Services.CreateAsyncScope())
            companyId = (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
                "RC" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Collection HTTP", Ct)).Id;
        long customerId, twdId, usdId;
        await using (var db = new MyErpDbContext(
                         new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
                         [new SystemOrganizationDbModel(), new CustomerDbModel(), new PaymentSetupDbModel(),
                             new CurrencyDbModel()], new FixedCompany(companyId)))
        {
            var customer = new Customer
            {
                CompanyId = companyId, Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Collection customer", ReviewStatus = PartnerReviewStatus.Approved
            };
            var twd = new Currency { CompanyId = companyId, Code = "TWD", Name = "Taiwan dollar", DecimalPlaces = 0, IsBase = true };
            var usd = new Currency { CompanyId = companyId, Code = "USD", Name = "US dollar", DecimalPlaces = 2 };
            db.AddRange(customer, twd, usd);
            if (numberRule)
                db.Add(new NumberRule
                {
                    CompanyId = companyId, DocumentType = "AR.COLLECTION", Name = "Collection receipt", Prefix = "RC",
                    DateFormat = NumberDateFormat.YearMonth, SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly,
                });
            await db.SaveChangesAsync(Ct);
            (customerId, twdId, usdId) = (customer.Id, twd.Id, usd.Id);
        }

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
        await AccountAsync("1", "Assets", null, "Asset");
        await AccountAsync("1102", "Bank", "1", null);
        await AccountAsync("1131", "Receivable", "1", null);
        await AccountAsync("2", "Liabilities", null, "Liability");
        await AccountAsync("2181", "Unapplied cash", "2", null);
        async Task<long> FundAsync(string code, string name, long currencyId)
        {
            using var response = await admin.PostAsJsonAsync("/api/v1/md/fund-accounts", new
            {
                code, name, currencyId, fundType = "Bank", bankName = "Mega", bankAccountNo = code,
                ledgerAccountId = accounts["1102"]
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        var bank = await FundAsync("BANK1", "Mega TWD", twdId);
        var usdBank = await FundAsync("BANKUSD", "Mega USD", usdId);
        using (var period = await admin.PostAsJsonAsync("/api/v1/gl/periods",
                   new { startDate = "2026-11-01", endDate = "2026-11-30" }, Ct))
            Assert.Equal(HttpStatusCode.Created, period.StatusCode);
        using (var map = await admin.PutAsJsonAsync("/api/v1/gl/sales-collection-account-map",
                   new { version = 0, unappliedCashAccountCode = "2181", receivableAccountCode = "1131" }, Ct))
            Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        return new(companyId, customerId, bank, usdBank, admin);
    }

    private static async Task SetFundActiveAsync(Setup setup, bool active)
    {
        using var read = await setup.Admin.GetAsync("/api/v1/md/fund-accounts?pageSize=200", Ct);
        var fund = (await read.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetInt64() == setup.Bank);
        using var response = await setup.Admin.PutAsJsonAsync($"/api/v1/md/fund-accounts/{setup.Bank}", new
        {
            name = fund.GetProperty("name").GetString(), fundType = "Bank", bankName = "Mega", bankAccountNo = "BANK1",
            ledgerAccountId = fund.GetProperty("ledgerAccountId").GetInt64(), isActive = active,
            version = fund.GetProperty("version").GetInt32()
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task FlowAsync(Setup setup, long approverId)
    {
        using var response = await setup.Admin.PostAsJsonAsync("/api/v1/sys/approval-flows", new
        {
            documentType = "AR.COLLECTION", name = "Collection", allowSelfApproval = false,
            steps = new object[] { new { name = "Finance manager", approverType = "User", userId = approverId } }
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpClient> LoginAsync(long companyId, string[] permissions, bool fullAccess = false) =>
        (await LoginWithIdAsync(companyId, permissions, fullAccess)).Client;

    private async Task<(long UserId, HttpClient Client)> LoginWithIdAsync(long companyId, string[] permissions,
        bool fullAccess = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("R" + Guid.NewGuid().ToString("N")[..12], "Collection role",
            null, DataScope.Company, fullAccess, permissions), Ct);
        var username = "rc" + Guid.NewGuid().ToString("N")[..20];
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
        return (user.Id, client);
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
