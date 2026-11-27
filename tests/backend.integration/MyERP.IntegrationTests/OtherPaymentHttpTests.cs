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
/// FI-OP-HTTP-001～008（WU-FI-008b3a，REQ_F4116.4.1～4.5，BR-FI-059～065）：其他付款單據 HTTP。真實 SQL＋真實 SYS 授權、
/// 單號規則與審批引擎：路由與 Swagger、AP.OTHER_PAYMENT.* 分權、建立／修改／刪除的版本與狀態、列表篩選排序分頁與公司隔離、
/// 送審／撤回、SYS 核准同交易過帳（Posted、借費用／貸銀行、傳票）、駁回回可編輯、零步驟送審即過帳、過帳失敗整筆回滾，
/// 以及缺少單號規則。費用類別對照經 008b3b 的 HTTP API 建立。
/// </summary>
public sealed class OtherPaymentHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Root = "/api/v1/ap/other-payments";
    private const string Password = "Strong pass 2026!";
    private const string BusinessDate = "2026-11-10";
    private static readonly string[] Writer =
        ["AP.OTHER_PAYMENT.VIEW", "AP.OTHER_PAYMENT.CREATE", "AP.OTHER_PAYMENT.EDIT", "AP.OTHER_PAYMENT.DELETE",
            "AP.OTHER_PAYMENT.SUBMIT"];
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
        // FI-OP-HTTP-001：未登入一律 401；Swagger 列出列表／建立、明細／修改／刪除、送審、撤回。
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
    public async Task DecimalTextFromBrowserSavesWithoutJavaScriptRounding()
    {
        // FI-OP-UI-008: the browser sends the exact decimal text, including a value beyond binary64 integer precision.
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var body = new { payeeDescription = "Power", expenseCategoryId = setup.Courier,
            bankAccountId = setup.Bank, bankReference = "WD-EXACT", businessDate = BusinessDate,
            amount = "999999999999999.9999", remark = (string?)null };
        using var created = await writer.PostAsJsonAsync(Root, body, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(999999999999999.9999m, draft.GetProperty("amount").GetDecimal());
        var id = draft.GetProperty("id").GetInt64();
        using var replaced = await writer.PutAsJsonAsync($"{Root}/{id}", new { version = 1,
            body.payeeDescription, body.expenseCategoryId, body.bankAccountId, body.bankReference, body.businessDate,
            amount = "999999999999998.1234", body.remark }, Ct);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        using var read = await writer.GetAsync($"{Root}/{id}", Ct);
        var saved = await read.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(999999999999998.1234m, saved.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task EachActionNeedsItsOwnPermission()
    {
        // FI-OP-HTTP-002（權限）：VIEW 只可讀；CREATE 不含讀；沒有 SUBMIT 不可送審／撤回；沒有 DELETE 不可刪除；
        // 收款權限不能操作其他付款。拒絕不改資料。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, "P-1")).GetProperty("id").GetInt64();

        var viewer = await LoginAsync(setup.Company, ["AP.OTHER_PAYMENT.VIEW"]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Root, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Root}/{id}", Ct)).StatusCode);
        await AssertDeniedAsync(await viewer.PostAsJsonAsync(Root, Body(setup, "P-2"), Ct));
        await AssertDeniedAsync(await viewer.PutAsJsonAsync($"{Root}/{id}", Body(setup, "P-1", version: 1), Ct));
        await AssertDeniedAsync(await viewer.DeleteAsync($"{Root}/{id}?version=1", Ct));
        await AssertDeniedAsync(await viewer.PostAsJsonAsync($"{Root}/{id}/submit", new { version = 1 }, Ct));

        var creator = await LoginAsync(setup.Company, ["AP.OTHER_PAYMENT.CREATE"]);
        await AssertDeniedAsync(await creator.GetAsync(Root, Ct));
        await AssertDeniedAsync(await creator.GetAsync($"{Root}/{id}", Ct));
        var editorOnly = await LoginAsync(setup.Company,
            ["AP.OTHER_PAYMENT.VIEW", "AP.OTHER_PAYMENT.EDIT", "AP.OTHER_PAYMENT.CREATE"]);
        await AssertDeniedAsync(await editorOnly.PostAsJsonAsync($"{Root}/{id}/withdraw", new { version = 1 }, Ct));
        await AssertDeniedAsync(await editorOnly.DeleteAsync($"{Root}/{id}?version=1", Ct));
        var collector = await LoginAsync(setup.Company, ["AR.COLLECTION.VIEW", "AR.COLLECTION.CREATE"]);
        await AssertDeniedAsync(await collector.GetAsync(Root, Ct));
        await AssertDeniedAsync(await collector.PostAsJsonAsync(Root, Body(setup, "P-3"), Ct));

        Assert.Equal("Draft|1|0", await RowAsync(id));
        Assert.Equal(1, await CountAsync(setup.Company));
    }

    [Fact]
    public async Task CreateUpdateDeleteFollowVersionValidationAndAudit()
    {
        // FI-OP-HTTP-003（正常／邊界／衝突）：201＋Location、OP 單號、MD 類別與資金帳戶名稱、未過帳無傳票；修改遞增版本；
        // 舊版本 409；欄位錯誤 400 逐欄；外幣帳戶與收入類別 422；提款參考重複（不分大小寫）409；刪除 204 後 404、釋出參考；
        // 稽核記錄。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);

        using var created = await writer.PostAsJsonAsync(Root,
            Body(setup, " WD-001 ", 1200.5m, "  首期運費  ", payee: "  順豐速運 "), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = draft.GetProperty("id").GetInt64();
        Assert.EndsWith($"{Root}/{id}", created.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("OP202611", draft.GetProperty("documentNo").GetString(), StringComparison.Ordinal);
        Assert.Equal(("Draft", 1, "WD-001", "首期運費", "順豐速運", "TWD", 1200.5m), (draft.GetProperty("status").GetString(),
            draft.GetProperty("version").GetInt32(), draft.GetProperty("bankReference").GetString(),
            draft.GetProperty("remark").GetString(), draft.GetProperty("payeeDescription").GetString(),
            draft.GetProperty("currency").GetString(), draft.GetProperty("amount").GetDecimal()));
        Assert.Equal(("COURIER", "快遞費", "BANK1", "Mega TWD"), (draft.GetProperty("expenseCategoryCode").GetString(),
            draft.GetProperty("expenseCategoryName").GetString(), draft.GetProperty("bankAccountCode").GetString(),
            draft.GetProperty("bankAccountName").GetString()));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("journalId").ValueKind);

        using var updated = await writer.PutAsJsonAsync($"{Root}/{id}", Body(setup, "WD-001", 900m, version: 1), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var second = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((2, 900m), (second.GetProperty("version").GetInt32(), second.GetProperty("amount").GetDecimal()));
        await AssertProblemAsync(await writer.PutAsJsonAsync($"{Root}/{id}", Body(setup, "WD-001", version: 1), Ct),
            HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");

        using (var invalid = await writer.PostAsJsonAsync(Root, new
               {
                   payeeDescription = new string('x', 201), expenseCategoryId = 0, bankAccountId = 0, bankReference = " ",
                   businessDate = BusinessDate, amount = 0.00001m, remark = new string('x', 501)
               }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            foreach (var field in new[]
                     { "payeeDescription", "expenseCategoryId", "bankAccountId", "bankReference", "amount", "remark" })
                Assert.True(errors.TryGetProperty(field, out _), field);
        }
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, "WD-USD", bank: setup.UsdBank), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.FUND_ACCOUNT_CURRENCY_MISMATCH");
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, "WD-INC", category: setup.Income), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.EXPENSE_CATEGORY_NOT_EXPENSE");
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, "wd-001"), Ct),
            HttpStatusCode.Conflict, "FI.OTHER_PAYMENT.BANK_REFERENCE_DUPLICATE");
        Assert.Equal(1, await CountAsync(setup.Company));

        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version=1", Ct),
            HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await writer.DeleteAsync($"{Root}/{id}?version=2", Ct)).StatusCode);
        await AssertProblemAsync(await writer.GetAsync($"{Root}/{id}", Ct), HttpStatusCode.NotFound,
            "FI.OTHER_PAYMENT.NOT_FOUND");
        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version=3", Ct), HttpStatusCode.NotFound,
            "FI.OTHER_PAYMENT.NOT_FOUND");
        Assert.Equal("Draft|3|1", await RowAsync(id));
        Assert.Equal(HttpStatusCode.Created, (await writer.PostAsJsonAsync(Root, Body(setup, "WD-001"), Ct)).StatusCode);
        Assert.True(await SqlAsync<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.sys_audit_log WHERE table_name='ap_other_payment_doc' AND company_id={setup.Company}") >= 3);
    }

    [Fact]
    public async Task ListFiltersSortsPagesAndHidesOtherCompanies()
    {
        // FI-OP-HTTP-004（查詢／公司隔離）：狀態、資金帳戶、費用類別、日期、關鍵字（單號、提款參考或收款對象）篩選；
        // 排序白名單；分頁總數；列表顯示 MD 標籤、已過帳傳票；其他公司的單據不在列表、明細／修改／送審 404；無效篩選 400。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var a = (await CreateAsync(writer, setup, "LIST-A", 300m)).GetProperty("id").GetInt64();
        var b = (await CreateAsync(writer, setup, "LIST-B", 100m, date: "2026-11-12", payee: "中華電信"))
            .GetProperty("id").GetInt64();
        var c = (await CreateAsync(writer, setup, "OTHER-C", 200m, date: "2026-11-05")).GetProperty("id").GetInt64();
        var d = (await CreateAsync(writer, setup, "RENT-D", 50m, category: setup.Rent)).GetProperty("id").GetInt64();
        Assert.Equal("Posted", (await SubmitAsync(writer, c, 1, HttpStatusCode.OK)).GetProperty("status").GetString());

        var all = await ListAsync(writer, "");
        Assert.Equal(4, all.GetProperty("totalCount").GetInt64());
        Assert.Equal([b, d, a, c], Ids(all)); // 預設業務日新到舊，同日按 ID 新到舊
        Assert.Equal([d, b, c, a], Ids(await ListAsync(writer, "?sort=amount")));
        Assert.Equal([a, b, c, d], Ids(await ListAsync(writer, "?sort=documentNo")));
        Assert.Equal([c], Ids(await ListAsync(writer, "?status=Posted")));
        Assert.Equal([b, d, a], Ids(await ListAsync(writer, "?status=Draft")));
        Assert.Equal([b, a], Ids(await ListAsync(writer, "?keyword=list-")));
        Assert.Equal([b], Ids(await ListAsync(writer, "?keyword=中華")));
        Assert.Equal([d, a], Ids(await ListAsync(writer, "?from=2026-11-06&to=2026-11-11")));
        Assert.Equal([d], Ids(await ListAsync(writer, $"?expenseCategoryId={setup.Rent}")));
        Assert.Equal(4, Ids(await ListAsync(writer, $"?bankAccountId={setup.Bank}")).Length);
        Assert.Empty(Ids(await ListAsync(writer, $"?bankAccountId={setup.UsdBank}")));
        var page = await ListAsync(writer, "?page=2&pageSize=3");
        Assert.Equal([c], Ids(page));
        Assert.Equal(4L, page.GetProperty("totalCount").GetInt64());
        var posted = page.GetProperty("items")[0];
        Assert.Equal(("Posted", 200m, "快遞費", "Mega TWD", "順豐速運"), (posted.GetProperty("status").GetString(),
            posted.GetProperty("amount").GetDecimal(), posted.GetProperty("expenseCategoryName").GetString(),
            posted.GetProperty("bankAccountName").GetString(), posted.GetProperty("payeeDescription").GetString()));
        Assert.True(posted.GetProperty("journalId").GetInt64() > 0);
        Assert.Equal(JsonValueKind.Null,
            (await ListAsync(writer, "?status=Draft")).GetProperty("items")[0].GetProperty("journalId").ValueKind);

        foreach (var bad in new[] { "?sort=bankReference", "?sort=amount,documentNo", "?status=Deleted",
                     "?from=2026-11-12&to=2026-11-01", "?bankAccountId=0", "?expenseCategoryId=-1", "?pageSize=201",
                     "?keyword=" + new string('k', 51) })
            Assert.Equal(HttpStatusCode.BadRequest, (await writer.GetAsync(Root + bad, Ct)).StatusCode);

        var other = await SetupAsync();
        var outsider = await LoginAsync(other.Company, Writer);
        Assert.Equal(0, (await ListAsync(outsider, "")).GetProperty("totalCount").GetInt64());
        await AssertProblemAsync(await outsider.GetAsync($"{Root}/{a}", Ct), HttpStatusCode.NotFound,
            "FI.OTHER_PAYMENT.NOT_FOUND");
        await AssertProblemAsync(await outsider.PutAsJsonAsync($"{Root}/{a}", Body(other, "X", version: 1), Ct),
            HttpStatusCode.NotFound, "FI.OTHER_PAYMENT.NOT_FOUND");
        await AssertProblemAsync(await outsider.PostAsJsonAsync($"{Root}/{a}/submit", new { version = 1 }, Ct),
            HttpStatusCode.NotFound, "FI.OTHER_PAYMENT.NOT_FOUND");
        Assert.Equal("Draft|1|0", await RowAsync(a));
    }

    [Fact]
    public async Task SubmitWithdrawAndSysFinalApprovalPostInTheSameDecision()
    {
        // FI-OP-HTTP-005（狀態／正常）：多級流程送審 → Pending，不可修改、刪除或再送審（422）；撤回 → Draft；再送審；
        // 審批人核准 → 同一 SYS 決定內過帳：Posted、傳票借 6101／貸 1102；已過帳不可撤回、修改或刪除。
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
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.INVALID_STATUS");
        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version=2", Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.INVALID_STATUS");
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{id}/submit", new { version = 2 }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.INVALID_STATUS");
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{id}/withdraw", new { version = 1 }, Ct),
            HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");

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
        Assert.Equal("Posted", detail.GetProperty("status").GetString());
        var journal = detail.GetProperty("journalId").GetInt64();
        Assert.Equal("6101:750.2500:0.0000|1102:0.0000:750.2500", await SqlAsync<string>(
            $"SELECT STRING_AGG(CONCAT(account_code,':',debit,':',credit),'|') WITHIN GROUP (ORDER BY sequence) AS [Value] FROM dbo.gl_journal_line WHERE journal_id={journal}"));
        Assert.Equal(1, await PostedCountAsync(setup.Company));
        var version = detail.GetProperty("version").GetInt32();
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{id}/withdraw", new { version }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.INVALID_STATUS");
        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version={version}", Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.INVALID_STATUS");
    }

    [Fact]
    public async Task RejectionReturnsThePaymentForEditingWithoutLedgerEffect()
    {
        // FI-OP-HTTP-006（駁回）：SYS 駁回 → Rejected、無來源列與傳票；修改回 Draft 後可再送審。
        var setup = await SetupAsync();
        var (approverId, approver) = await LoginWithIdAsync(setup.Company, ["SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS"]);
        await FlowAsync(setup, approverId);
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, "WF-R")).GetProperty("id").GetInt64();
        var instance = (await SubmitAsync(writer, id, 1, HttpStatusCode.OK)).GetProperty("approval")
            .GetProperty("instanceId").GetInt64();

        using (var rejected = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{instance}/reject",
                   new { comment = "缺少發票", version = 1 }, Ct))
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
    public async Task ZeroStepFlowPostsDuringSubmitAndPostingFailureRollsBackTheSubmission()
    {
        // FI-OP-HTTP-007（零步驟／錯誤）：沒有啟用流程 → SYS 自動核准，送審回應即 Posted 並含過帳結果（費用科目、金額）；
        // 費用類別沒有對照 → 422 FI.OTHER_PAYMENT_POSTING.MAPPING_MISSING，單據仍 Draft v1、無 SYS 輪次、無傳票；
        // 資金帳戶停用 → 送審 422 FI.OTHER_PAYMENT.FUND_ACCOUNT_INACTIVE。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, "WF-0", 99m)).GetProperty("id").GetInt64();

        var result = await SubmitAsync(writer, id, 1, HttpStatusCode.OK);

        Assert.Equal(("Posted", "Approved"), (result.GetProperty("status").GetString(),
            result.GetProperty("approval").GetProperty("status").GetString()));
        var posting = result.GetProperty("posting");
        Assert.Equal((99m, setup.CourierLedger, "WF-0"), (posting.GetProperty("amount").GetDecimal(),
            posting.GetProperty("expenseAccountId").GetInt64(), posting.GetProperty("bankReference").GetString()));
        Assert.Equal(posting.GetProperty("journalId").GetInt64(), (await ReadAsync(writer, id)).GetProperty("journalId").GetInt64());
        Assert.Equal(1, await PostedCountAsync(setup.Company));

        var unmapped = (await CreateAsync(writer, setup, "WF-NOMAP", category: setup.Rent)).GetProperty("id").GetInt64();
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{unmapped}/submit", new { version = 1 }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT_POSTING.MAPPING_MISSING");
        Assert.Equal("Draft|1|0", await RowAsync(unmapped));
        Assert.Equal(0, await SqlAsync<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.sys_approval_instance WHERE company_id={setup.Company} AND document_type='AP.OTHER_PAYMENT' AND document_id={unmapped}"));
        Assert.Equal(1, await PostedCountAsync(setup.Company));

        var inactive = (await CreateAsync(writer, setup, "WF-INACTIVE")).GetProperty("id").GetInt64();
        await SetFundActiveAsync(setup, false);
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{inactive}/submit", new { version = 1 }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.OTHER_PAYMENT.FUND_ACCOUNT_INACTIVE");
        Assert.Equal("Draft|1|0", await RowAsync(inactive));
    }

    [Fact]
    public async Task MissingNumberRuleRejectsCreationWithoutSavingThePayment()
    {
        // FI-OP-HTTP-008（設定缺失）：公司沒有 AP.OTHER_PAYMENT 單號規則 → 422 SYS.NUMBER_RULE.NOT_CONFIGURED，不寫資料。
        var setup = await SetupAsync(numberRule: false);
        var writer = await LoginAsync(setup.Company, Writer);
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, "NR-1"), Ct),
            HttpStatusCode.UnprocessableEntity, "SYS.NUMBER_RULE.NOT_CONFIGURED");
        Assert.Equal(0, await CountAsync(setup.Company));
    }

    private sealed record Setup(long Company, long Bank, long UsdBank, long Courier, long Rent, long Income,
        long CourierLedger, HttpClient Admin);

    private static object Body(Setup setup, string reference, decimal amount = 500m, string? remark = null,
        int? version = null, long? bank = null, string date = BusinessDate, long? category = null,
        string payee = "順豐速運") => version is { } v
        ? new { version = v, payeeDescription = payee, expenseCategoryId = category ?? setup.Courier,
            bankAccountId = bank ?? setup.Bank, bankReference = reference, businessDate = date, amount, remark }
        : new { payeeDescription = payee, expenseCategoryId = category ?? setup.Courier,
            bankAccountId = bank ?? setup.Bank, bankReference = reference, businessDate = date, amount, remark };

    private static async Task<JsonElement> CreateAsync(HttpClient client, Setup setup, string reference,
        decimal amount = 500m, string date = BusinessDate, long? category = null, string payee = "順豐速運")
    {
        using var response = await client.PostAsJsonAsync(Root,
            Body(setup, reference, amount, date: date, category: category, payee: payee), Ct);
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
        $"SELECT CONCAT(status,'|',version,'|',CAST(is_deleted AS int)) AS [Value] FROM dbo.ap_other_payment_doc WHERE id={id}");

    private Task<int> CountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_doc WHERE company_id={companyId} AND is_deleted=0");

    private Task<int> PostedCountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.ap_other_payment_source WHERE company_id={companyId}");

    private async Task<T> SqlAsync<T>(string sql)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQueryRaw<T>(sql).SingleAsync(Ct);
    }

    /// <summary>
    /// 公司＋本位幣 TWD＋USD、AP.OTHER_PAYMENT 單號規則（OP＋年月＋4 碼，可略）直接寫入；其餘經 API：MD 科目（1102 銀行、
    /// 6101 運費）、資金帳戶 BANK1（TWD→1102）與 BANKUSD、費用類別 COURIER（→6101）／RENT（無對照）、收入類別 SALEINC、
    /// 2026-11 GL 開放期間；COURIER→6101 對照以 SQL 寫入（對照 API 在 008b3b）。
    /// </summary>
    private async Task<Setup> SetupAsync(bool numberRule = true)
    {
        long companyId;
        await using (var scope = factory.Services.CreateAsyncScope())
            companyId = (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
                "OP" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Other payment HTTP", Ct)).Id;
        long twdId, usdId;
        await using (var db = new MyErpDbContext(
                         new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
                         [new SystemOrganizationDbModel(), new CurrencyDbModel()], new FixedCompany(companyId)))
        {
            var twd = new Currency { CompanyId = companyId, Code = "TWD", Name = "Taiwan dollar", DecimalPlaces = 0, IsBase = true };
            var usd = new Currency { CompanyId = companyId, Code = "USD", Name = "US dollar", DecimalPlaces = 2 };
            db.AddRange(twd, usd);
            if (numberRule)
                db.Add(new NumberRule
                {
                    CompanyId = companyId, DocumentType = "AP.OTHER_PAYMENT", Name = "Other payment", Prefix = "OP",
                    DateFormat = NumberDateFormat.YearMonth, SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly,
                });
            await db.SaveChangesAsync(Ct);
            (twdId, usdId) = (twd.Id, usd.Id);
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
        await AccountAsync("6", "Expenses", null, "Expense");
        await AccountAsync("6101", "Freight", "6", null);
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
        async Task<long> CategoryAsync(string code, string name, string direction, long? ledger)
        {
            using var response = await admin.PostAsJsonAsync("/api/v1/md/income-expense-categories",
                new { code, name, direction, ledgerAccountId = ledger }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        var courier = await CategoryAsync("COURIER", "快遞費", "Expense", accounts["6101"]);
        var rent = await CategoryAsync("RENT", "租金", "Expense", null);
        var income = await CategoryAsync("SALEINC", "雜項收入", "Income", null);
        using (var period = await admin.PostAsJsonAsync("/api/v1/gl/periods",
                   new { startDate = "2026-11-01", endDate = "2026-11-30" }, Ct))
            Assert.Equal(HttpStatusCode.Created, period.StatusCode);
        using (var map = await admin.PutAsJsonAsync($"/api/v1/gl/other-payment-account-map/{courier}",
                   new { version = 0, expenseAccountCode = "6101" }, Ct))
            Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        return new(companyId, bank, usdBank, courier, rent, income, accounts["6101"], admin);
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
            documentType = "AP.OTHER_PAYMENT", name = "Other payment", allowSelfApproval = false,
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
        var role = await roles.CreateAsync(new RoleInput("R" + Guid.NewGuid().ToString("N")[..12], "Other payment role",
            null, DataScope.Company, fullAccess, permissions), Ct);
        var username = "op" + Guid.NewGuid().ToString("N")[..20];
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
