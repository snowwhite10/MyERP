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
/// FI-COLL-SHTTP-001～009（WU-FI-005b4b，ADR-130，REQ_F4116.5.8，BR-FI-161）：收款核銷單據 HTTP。真實 SQL＋真實 SYS
/// 授權、單號規則與審批引擎：路由與 Swagger、獨立 AR.SETTLEMENT.* 權限、建立／修改／刪除的版本與狀態、BR-FI-039 422
/// <c>lineErrors</c>、列表篩選排序分頁與公司隔離、送審／撤回、SYS 核准同交易過帳（應收未清與收款未核銷減少）、駁回、
/// 零步驟流程送審即過帳、待審期間應收被用完時整個審批決定回滾，以及缺單號規則。收款經 HTTP 建立並零步驟過帳；
/// 發票應收直接寫入 FI 自有的 <c>ar_posted_receivable</c>（發票過帳已另有測試）。
/// </summary>
public sealed class CollectionSettlementHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Root = "/api/v1/ar/collection-settlements";
    private const string Receipts = "/api/v1/ar/sales-collections";
    private const string Password = "Strong pass 2026!";
    private const string BusinessDate = "2026-11-10";
    private static readonly string[] Writer =
        ["AR.SETTLEMENT.VIEW", "AR.SETTLEMENT.CREATE", "AR.SETTLEMENT.EDIT", "AR.SETTLEMENT.DELETE",
            "AR.SETTLEMENT.SUBMIT"];
    private static readonly string[] ReceiptWriter =
        ["AR.COLLECTION.VIEW", "AR.COLLECTION.CREATE", "AR.COLLECTION.SUBMIT"];
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
        // FI-COLL-SHTTP-001
        using var client = factory.CreateClient();
        using (var list = await client.GetAsync(Root, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using (var create = await client.PostAsJsonAsync(Root, new { receiptId = 1 }, Ct))
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
        // FI-COLL-SHTTP-002（權限）：VIEW 只可讀；CREATE 不含讀；沒有 SUBMIT 不可送審／撤回；沒有 DELETE 不可刪除；
        // 收款權限（AR.COLLECTION.*）不能操作核銷。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, (setup.InvoiceA, 100m))).GetProperty("id").GetInt64();

        var viewer = await LoginAsync(setup.Company, ["AR.SETTLEMENT.VIEW"]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Root, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Root}/{id}", Ct)).StatusCode);
        await AssertDeniedAsync(await viewer.PostAsJsonAsync(Root, Body(setup, [(setup.InvoiceA, 1m)]), Ct));
        await AssertDeniedAsync(await viewer.PutAsJsonAsync($"{Root}/{id}",
            Body(setup, [(setup.InvoiceA, 1m)], version: 1), Ct));
        await AssertDeniedAsync(await viewer.DeleteAsync($"{Root}/{id}?version=1", Ct));
        await AssertDeniedAsync(await viewer.PostAsJsonAsync($"{Root}/{id}/submit", new { version = 1 }, Ct));

        var creator = await LoginAsync(setup.Company, ["AR.SETTLEMENT.CREATE"]);
        await AssertDeniedAsync(await creator.GetAsync(Root, Ct));
        await AssertDeniedAsync(await creator.GetAsync($"{Root}/{id}", Ct));
        var editorOnly = await LoginAsync(setup.Company,
            ["AR.SETTLEMENT.VIEW", "AR.SETTLEMENT.EDIT", "AR.SETTLEMENT.CREATE"]);
        await AssertDeniedAsync(await editorOnly.PostAsJsonAsync($"{Root}/{id}/withdraw", new { version = 1 }, Ct));
        await AssertDeniedAsync(await editorOnly.DeleteAsync($"{Root}/{id}?version=1", Ct));
        var receiptOnly = await LoginAsync(setup.Company, [.. ReceiptWriter, "AR.COLLECTION.EDIT", "AR.COLLECTION.DELETE"]);
        await AssertDeniedAsync(await receiptOnly.GetAsync(Root, Ct));
        await AssertDeniedAsync(await receiptOnly.PostAsJsonAsync($"{Root}/{id}/submit", new { version = 1 }, Ct));

        Assert.Equal("Draft|1|0", await RowAsync(id));
    }

    [Fact]
    public async Task CreateUpdateDeleteFollowVersionValidationLineErrorsAndAudit()
    {
        // FI-COLL-SHTTP-003（正常／邊界／衝突／錯誤）：201＋Location、單號取自 SYS 規則、客戶取自收款、合計＝行合計、
        // 行依應收 ID 排序、MD 名稱、收款單號與未核銷；修改遞增版本；舊版本 409；欄位錯誤 400 逐欄（含 lines[i]）；
        // 不可分配的行 422 ALLOCATION_INVALID 並以 lineErrors 列出每一行與原因；收款不足 422；刪除 204 後 404；稽核記錄。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);

        using var created = await writer.PostAsJsonAsync(Root,
            Body(setup, [(setup.InvoiceB, 300.5m), (setup.InvoiceA, 400m)], "  部分核銷 "), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = draft.GetProperty("id").GetInt64();
        Assert.EndsWith($"{Root}/{id}", created.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("WS202611", draft.GetProperty("documentNo").GetString(), StringComparison.Ordinal);
        Assert.Equal(("Draft", 1, "部分核銷", "TWD", 700.5m, setup.Customer), (draft.GetProperty("status").GetString(),
            draft.GetProperty("version").GetInt32(), draft.GetProperty("remark").GetString(),
            draft.GetProperty("currency").GetString(), draft.GetProperty("totalAmount").GetDecimal(),
            draft.GetProperty("customerId").GetInt64()));
        Assert.Equal(("Collection customer", setup.ReceiptNo, 1_000m), (draft.GetProperty("customerName").GetString(),
            draft.GetProperty("receiptDocumentNo").GetString(), draft.GetProperty("receiptUnappliedAmount").GetDecimal()));
        var lines = draft.GetProperty("lines");
        Assert.Equal([setup.InvoiceA, setup.InvoiceB],
            lines.EnumerateArray().Select(x => x.GetProperty("receivableId").GetInt64()).ToArray());
        Assert.Equal([400m, 700m], lines.EnumerateArray().Select(x => x.GetProperty("receivableOpenAmount").GetDecimal()).ToArray());

        using var updated = await writer.PutAsJsonAsync($"{Root}/{id}",
            Body(setup, [(setup.InvoiceA, 250m)], version: 1), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var second = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((2, 250m, 1), (second.GetProperty("version").GetInt32(), second.GetProperty("totalAmount").GetDecimal(),
            second.GetProperty("lines").GetArrayLength()));
        await AssertProblemAsync(await writer.PutAsJsonAsync($"{Root}/{id}",
            Body(setup, [(setup.InvoiceA, 1m)], version: 1), Ct), HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");

        using (var invalid = await writer.PostAsJsonAsync(Root, new
               {
                   receiptId = 0, businessDate = BusinessDate, remark = new string('x', 501),
                   lines = new object[] { new { receivableId = 0, amount = 1m }, new { receivableId = 5, amount = 0.00001m } }
               }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            foreach (var field in new[] { "receiptId", "remark", "lines[0].receivableId", "lines[1].amount" })
                Assert.True(errors.TryGetProperty(field, out _), field);
        }

        using (var bad = await writer.PostAsJsonAsync(Root,
                   Body(setup, [(setup.InvoiceA, 450m), (setup.Other, 10m), (999_999_999L, 5m), (setup.InvoiceB, 1m)]), Ct))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
            var body = await bad.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID", body.GetProperty("errorCode").GetString());
            var reasons = body.GetProperty("lineErrors").EnumerateArray()
                .Select(x => $"{x.GetProperty("receivableId").GetInt64()}:{x.GetProperty("reason").GetString()}")
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(new[]
            {
                $"{setup.InvoiceA}:FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN",
                $"{setup.Other}:FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_MISMATCH",
                "999999999:FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_NOT_FOUND",
            }.Order(StringComparer.Ordinal).ToArray(), reasons);
            Assert.True(body.GetProperty("lineErrors").EnumerateArray().All(x => x.GetProperty("line").GetInt32() > 0));
        }
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root,
                Body(setup, [(setup.InvoiceA, 400m), (setup.InvoiceB, 700m)]), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION_WRITEOFF.RECEIPT_INSUFFICIENT");
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root,
                Body(setup, [(setup.InvoiceA, 1m)], receipt: setup.DraftReceipt), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION_WRITEOFF.RECEIPT_NOT_POSTED");
        Assert.Equal(1, await CountAsync(setup.Company));

        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version=1", Ct),
            HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await writer.DeleteAsync($"{Root}/{id}?version=2", Ct)).StatusCode);
        await AssertProblemAsync(await writer.GetAsync($"{Root}/{id}", Ct), HttpStatusCode.NotFound,
            "FI.COLLECTION.SETTLEMENT_NOT_FOUND");
        Assert.Equal("Draft|3|1", await RowAsync(id));
        Assert.True(await SqlAsync<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.sys_audit_log WHERE table_name='ar_collection_settlement_doc' AND company_id={setup.Company}") >= 3);
    }

    [Fact]
    public async Task ListFiltersSortsPagesAndHidesOtherCompanies()
    {
        // FI-COLL-SHTTP-004（查詢／公司隔離）：狀態、客戶、收款、日期、關鍵字（單號）篩選；排序白名單；分頁總數；
        // 行數與收款單號；其他公司的單據不在列表、明細／修改／送審 404；無效篩選 400。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var a = (await CreateAsync(writer, setup, (setup.InvoiceA, 300m))).GetProperty("id").GetInt64();
        var b = (await CreateAsync(writer, setup, [(setup.InvoiceA, 50m), (setup.InvoiceB, 50m)], "2026-11-12"))
            .GetProperty("id").GetInt64();
        var cDraft = await CreateAsync(writer, setup, [(setup.InvoiceB, 200m)], "2026-11-05");
        var c = cDraft.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await writer.PostAsJsonAsync($"{Root}/{c}/submit", new { version = 1 }, Ct)).StatusCode);

        var all = await ListAsync(writer, "");
        Assert.Equal(3, all.GetProperty("totalCount").GetInt64());
        Assert.Equal([b, a, c], Ids(all)); // 預設業務日新到舊
        Assert.Equal([b, c, a], Ids(await ListAsync(writer, "?sort=totalAmount")));
        Assert.Equal([c], Ids(await ListAsync(writer, "?status=Posted")));
        Assert.Equal([b, a], Ids(await ListAsync(writer, "?status=Draft")));
        Assert.Equal([a], Ids(await ListAsync(writer, "?from=2026-11-10&to=2026-11-11")));
        Assert.Equal([c], Ids(await ListAsync(writer,
            "?keyword=" + Uri.EscapeDataString(cDraft.GetProperty("documentNo").GetString()!.ToLowerInvariant()))));
        Assert.Equal(3, Ids(await ListAsync(writer, $"?customerId={setup.Customer}&receiptId={setup.Receipt}")).Length);
        Assert.Empty(Ids(await ListAsync(writer, $"?receiptId={setup.DraftReceipt}")));
        var page = await ListAsync(writer, "?page=2&pageSize=2");
        Assert.Equal([c], Ids(page));
        Assert.Equal(3L, page.GetProperty("totalCount").GetInt64());
        var first = (await ListAsync(writer, "")).GetProperty("items")[0];
        Assert.Equal(("Draft", 100m, 2, "Collection customer", setup.ReceiptNo), (first.GetProperty("status").GetString(),
            first.GetProperty("totalAmount").GetDecimal(), first.GetProperty("lineCount").GetInt32(),
            first.GetProperty("customerName").GetString(), first.GetProperty("receiptDocumentNo").GetString()));

        foreach (var bad in new[] { "?sort=remark", "?status=Deleted", "?from=2026-11-12&to=2026-11-01",
                     "?customerId=0", "?receiptId=-1", "?pageSize=201", "?sort=documentNo,totalAmount" })
            Assert.Equal(HttpStatusCode.BadRequest, (await writer.GetAsync(Root + bad, Ct)).StatusCode);

        var other = await SetupAsync();
        var outsider = await LoginAsync(other.Company, Writer);
        Assert.Equal(0, (await ListAsync(outsider, "")).GetProperty("totalCount").GetInt64());
        await AssertProblemAsync(await outsider.GetAsync($"{Root}/{a}", Ct), HttpStatusCode.NotFound,
            "FI.COLLECTION.SETTLEMENT_NOT_FOUND");
        await AssertProblemAsync(await outsider.PutAsJsonAsync($"{Root}/{a}",
                Body(other, [(other.InvoiceA, 1m)], version: 1), Ct),
            HttpStatusCode.NotFound, "FI.COLLECTION.SETTLEMENT_NOT_FOUND");
        await AssertProblemAsync(await outsider.PostAsJsonAsync($"{Root}/{a}/submit", new { version = 1 }, Ct),
            HttpStatusCode.NotFound, "FI.COLLECTION.SETTLEMENT_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitWithdrawAndSysFinalApprovalPostInTheSameDecision()
    {
        // FI-COLL-SHTTP-005（狀態／正常）：多級流程送審 → Pending，不可修改或刪除（422）；撤回 → Draft；再送審；
        // 審批人核准 → 同一 SYS 決定內過帳：Posted、應收未清與收款未核銷減少；已過帳不可撤回。
        var setup = await SetupAsync();
        var (approverId, approver) = await LoginWithIdAsync(setup.Company, ["SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS"]);
        await FlowAsync(setup, approverId);
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, [(setup.InvoiceA, 400m), (setup.InvoiceB, 150.25m)]))
            .GetProperty("id").GetInt64();

        var submitted = await SubmitAsync(writer, id, 1, HttpStatusCode.OK);
        Assert.Equal(("Pending", 2, "Pending"), (submitted.GetProperty("status").GetString(),
            submitted.GetProperty("version").GetInt32(), submitted.GetProperty("approval").GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("posting").ValueKind);
        await AssertProblemAsync(await writer.PutAsJsonAsync($"{Root}/{id}",
                Body(setup, [(setup.InvoiceA, 1m)], version: 2), Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.SETTLEMENT_INVALID_STATUS");
        await AssertProblemAsync(await writer.DeleteAsync($"{Root}/{id}?version=2", Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.SETTLEMENT_INVALID_STATUS");

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
        Assert.Equal(("Posted", 449.75m), (detail.GetProperty("status").GetString(),
            detail.GetProperty("receiptUnappliedAmount").GetDecimal()));
        Assert.Equal([0m, 549.75m],
            detail.GetProperty("lines").EnumerateArray().Select(x => x.GetProperty("receivableOpenAmount").GetDecimal()).ToArray());
        Assert.Equal(1, await PostedCountAsync(setup.Company));
        await AssertProblemAsync(await writer.PostAsJsonAsync($"{Root}/{id}/withdraw",
                new { version = detail.GetProperty("version").GetInt32() }, Ct),
            HttpStatusCode.UnprocessableEntity, "FI.COLLECTION.SETTLEMENT_INVALID_STATUS");
    }

    [Fact]
    public async Task RejectionReturnsTheSettlementForEditingWithoutLedgerEffect()
    {
        // FI-COLL-SHTTP-006（駁回）：SYS 駁回 → Rejected、無核銷列、餘額不變；修改回 Draft 後可再送審。
        var setup = await SetupAsync();
        var (approverId, approver) = await LoginWithIdAsync(setup.Company, ["SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS"]);
        await FlowAsync(setup, approverId);
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, (setup.InvoiceA, 100m))).GetProperty("id").GetInt64();
        var instance = (await SubmitAsync(writer, id, 1, HttpStatusCode.OK)).GetProperty("approval")
            .GetProperty("instanceId").GetInt64();

        using (var rejected = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{instance}/reject",
                   new { comment = "分配有誤", version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var detail = await ReadAsync(writer, id);
        Assert.Equal(("Rejected", 1_000m), (detail.GetProperty("status").GetString(),
            detail.GetProperty("receiptUnappliedAmount").GetDecimal()));
        Assert.Equal(0, await PostedCountAsync(setup.Company));
        using var edited = await writer.PutAsJsonAsync($"{Root}/{id}",
            Body(setup, [(setup.InvoiceB, 80m)], version: detail.GetProperty("version").GetInt32()), Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var draft = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal("Pending", (await SubmitAsync(writer, id, draft.GetProperty("version").GetInt32(),
            HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ZeroStepFlowPostsDuringSubmit()
    {
        // FI-COLL-SHTTP-007（零步驟）：沒有啟用流程 → SYS 自動核准，送審回應即 Posted 並含核銷過帳結果。
        var setup = await SetupAsync();
        var writer = await LoginAsync(setup.Company, Writer);
        var id = (await CreateAsync(writer, setup, (setup.InvoiceB, 99m))).GetProperty("id").GetInt64();

        var result = await SubmitAsync(writer, id, 1, HttpStatusCode.OK);

        Assert.Equal(("Posted", "Approved"), (result.GetProperty("status").GetString(),
            result.GetProperty("approval").GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Object, result.GetProperty("posting").ValueKind);
        Assert.Equal(1, await PostedCountAsync(setup.Company));
        Assert.Equal(901m, (await ReadAsync(writer, id)).GetProperty("receiptUnappliedAmount").GetDecimal());
    }

    [Fact]
    public async Task PostingFailureReturnsLineErrorsRollsBackTheDecisionAndCanBeRetried()
    {
        // FI-COLL-SHTTP-008（錯誤／回滾）：兩張核銷同時待審，先核准的一張用去應收 A 大部分未清 → 核准第二張
        // 422 ALLOCATION_INVALID 並帶 lineErrors；單據與 SYS 輪次仍 Pending、無第二筆核銷；
        // 撤回修改為應收 B 後再送審核准成功。
        var setup = await SetupAsync();
        var (approverId, approver) = await LoginWithIdAsync(setup.Company, ["SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS"]);
        await FlowAsync(setup, approverId);
        var writer = await LoginAsync(setup.Company, Writer);
        var first = (await CreateAsync(writer, setup, (setup.InvoiceA, 300m))).GetProperty("id").GetInt64();
        var second = (await CreateAsync(writer, setup, (setup.InvoiceA, 200m))).GetProperty("id").GetInt64();
        var firstRound = (await SubmitAsync(writer, first, 1, HttpStatusCode.OK)).GetProperty("approval")
            .GetProperty("instanceId").GetInt64();
        var secondRound = (await SubmitAsync(writer, second, 1, HttpStatusCode.OK)).GetProperty("approval")
            .GetProperty("instanceId").GetInt64();

        using (var ok = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{firstRound}/approve",
                   new { comment = "OK", version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var failed = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{secondRound}/approve",
                   new { comment = "OK", version = 1 }, Ct))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
            var body = await failed.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID", body.GetProperty("errorCode").GetString());
            var line = Assert.Single(body.GetProperty("lineErrors").EnumerateArray().ToArray());
            Assert.Equal((1, setup.InvoiceA, "FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN"), (line.GetProperty("line").GetInt32(),
                line.GetProperty("receivableId").GetInt64(), line.GetProperty("reason").GetString()));
        }
        Assert.Equal("Pending|2|0", await RowAsync(second));
        Assert.Equal("Pending", await SqlAsync<string>(
            $"SELECT status AS [Value] FROM dbo.sys_approval_instance WHERE id={secondRound}"));
        Assert.Equal(1, await PostedCountAsync(setup.Company));

        Assert.Equal(HttpStatusCode.OK,
            (await writer.PostAsJsonAsync($"{Root}/{second}/withdraw", new { version = 2 }, Ct)).StatusCode);
        using (var fixedUp = await writer.PutAsJsonAsync($"{Root}/{second}",
                   Body(setup, [(setup.InvoiceB, 200m)], version: 3), Ct))
            Assert.Equal(HttpStatusCode.OK, fixedUp.StatusCode);
        var retry = (await SubmitAsync(writer, second, 4, HttpStatusCode.OK)).GetProperty("approval")
            .GetProperty("instanceId").GetInt64();
        using (var approved = await approver.PostAsJsonAsync($"/api/v1/sys/approvals/{retry}/approve",
                   new { comment = "Retry", version = 1 }, Ct))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Posted", (await ReadAsync(writer, second)).GetProperty("status").GetString());
        Assert.Equal(2, await PostedCountAsync(setup.Company));
    }

    [Fact]
    public async Task MissingNumberRuleRejectsCreationWithoutSavingTheSettlement()
    {
        // FI-COLL-SHTTP-009（設定缺失）：公司沒有 AR.SETTLEMENT 單號規則 → 422 SYS.NUMBER_RULE.NOT_CONFIGURED，不寫資料。
        var setup = await SetupAsync(settlementRule: false);
        var writer = await LoginAsync(setup.Company, Writer);
        await AssertProblemAsync(await writer.PostAsJsonAsync(Root, Body(setup, [(setup.InvoiceA, 10m)]), Ct),
            HttpStatusCode.UnprocessableEntity, "SYS.NUMBER_RULE.NOT_CONFIGURED");
        Assert.Equal(0, await CountAsync(setup.Company));
    }

    /// <summary>
    /// <see cref="Receipt"/> 是已過帳收款（1,000）；<see cref="DraftReceipt"/> 未送審；<see cref="InvoiceA"/>（400）與
    /// <see cref="InvoiceB"/>（700）是收款客戶的發票應收；<see cref="Other"/> 屬於另一客戶。
    /// </summary>
    private sealed record Setup(long Company, long Customer, long Receipt, string ReceiptNo, long DraftReceipt,
        long InvoiceA, long InvoiceB, long Other, HttpClient Admin);

    private static object Body(Setup setup, (long ReceivableId, decimal Amount)[] lines, string? remark = null,
        int? version = null, long? receipt = null, string date = BusinessDate)
    {
        var items = lines.Select(x => new { receivableId = x.ReceivableId, amount = x.Amount }).ToArray();
        return version is { } v
            ? new { version = v, receiptId = receipt ?? setup.Receipt, businessDate = date, lines = items, remark }
            : new { receiptId = receipt ?? setup.Receipt, businessDate = date, lines = items, remark };
    }

    private static Task<JsonElement> CreateAsync(HttpClient client, Setup setup, (long, decimal) line) =>
        CreateAsync(client, setup, [line]);

    private static async Task<JsonElement> CreateAsync(HttpClient client, Setup setup,
        (long ReceivableId, decimal Amount)[] lines, string date = BusinessDate)
    {
        using var response = await client.PostAsJsonAsync(Root, Body(setup, lines, date: date), Ct);
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
        $"SELECT CONCAT(status,'|',version,'|',CAST(is_deleted AS int)) AS [Value] FROM dbo.ar_collection_settlement_doc WHERE id={id}");

    private Task<int> CountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_settlement_doc WHERE company_id={companyId} AND is_deleted=0");

    private Task<int> PostedCountAsync(long companyId) => SqlAsync<int>(
        $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_settlement WHERE company_id={companyId}");

    private async Task<T> SqlAsync<T>(string sql)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQueryRaw<T>(sql).SingleAsync(Ct);
    }

    /// <summary>
    /// 公司＋本位幣 TWD、兩個已核准客戶、AR.COLLECTION 與 AR.SETTLEMENT 單號規則（後者可略）直接寫入；科目、資金帳戶、
    /// 2026-11 GL 開放期間與收款科目映射經 API；收款 1,000 經 HTTP 建立並零步驟送審過帳（日期 2026-11-01），另一張收款
    /// 停在 Draft；發票應收直接寫入 FI 自有表。
    /// </summary>
    private async Task<Setup> SetupAsync(bool settlementRule = true)
    {
        long companyId;
        await using (var scope = factory.Services.CreateAsyncScope())
            companyId = (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
                "WS" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Settlement HTTP", Ct)).Id;
        long customerId, otherCustomerId, twdId;
        await using (var db = new MyErpDbContext(
                         new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
                         [new SystemOrganizationDbModel(), new CustomerDbModel(), new CurrencyDbModel()],
                         new FixedCompany(companyId)))
        {
            Customer NewCustomer(string name) => new()
            {
                CompanyId = companyId, Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = name, ReviewStatus = PartnerReviewStatus.Approved
            };
            var customer = NewCustomer("Collection customer");
            var otherCustomer = NewCustomer("Other customer");
            var twd = new Currency { CompanyId = companyId, Code = "TWD", Name = "Taiwan dollar", DecimalPlaces = 0, IsBase = true };
            db.AddRange(customer, otherCustomer, twd);
            db.Add(new NumberRule
            {
                CompanyId = companyId, DocumentType = "AR.COLLECTION", Name = "Collection receipt", Prefix = "RC",
                DateFormat = NumberDateFormat.YearMonth, SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly,
            });
            if (settlementRule)
                db.Add(new NumberRule
                {
                    CompanyId = companyId, DocumentType = "AR.SETTLEMENT", Name = "Collection settlement", Prefix = "WS",
                    DateFormat = NumberDateFormat.YearMonth, SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly,
                });
            await db.SaveChangesAsync(Ct);
            (customerId, otherCustomerId, twdId) = (customer.Id, otherCustomer.Id, twd.Id);
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
        long bank;
        using (var fund = await admin.PostAsJsonAsync("/api/v1/md/fund-accounts", new
               {
                   code = "BANK1", name = "Mega TWD", currencyId = twdId, fundType = "Bank", bankName = "Mega",
                   bankAccountNo = "BANK1", ledgerAccountId = accounts["1102"]
               }, Ct))
        {
            Assert.Equal(HttpStatusCode.Created, fund.StatusCode);
            bank = (await fund.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        using (var period = await admin.PostAsJsonAsync("/api/v1/gl/periods",
                   new { startDate = "2026-11-01", endDate = "2026-11-30" }, Ct))
            Assert.Equal(HttpStatusCode.Created, period.StatusCode);
        using (var map = await admin.PutAsJsonAsync("/api/v1/gl/sales-collection-account-map",
                   new { version = 0, unappliedCashAccountCode = "2181", receivableAccountCode = "1131" }, Ct))
            Assert.Equal(HttpStatusCode.OK, map.StatusCode);

        var cashier = await LoginAsync(companyId, ReceiptWriter);
        async Task<JsonElement> ReceiptAsync(string reference)
        {
            using var response = await cashier.PostAsJsonAsync(Receipts, new
            {
                customerId, businessDate = "2026-11-01", amount = 1_000m, bankAccountId = bank, bankReference = reference
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
        var receipt = await ReceiptAsync("SET-1");
        var receiptId = receipt.GetProperty("id").GetInt64();
        using (var posted = await cashier.PostAsJsonAsync($"{Receipts}/{receiptId}/submit", new { version = 1 }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
            Assert.Equal("Posted", (await posted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        }
        var draftReceipt = (await ReceiptAsync("SET-2")).GetProperty("id").GetInt64();

        long invoiceA, invoiceB, other;
        await using (var db = new MyErpDbContext(
                         new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
                         [new CustomerCreditDbModel()], new FixedCompany(companyId)))
        {
            PostedReceivable Invoice(long customer, long sourceId, decimal amount) => new()
            {
                CompanyId = companyId, CustomerId = customer, SourceType = "Invoice", SourceId = sourceId,
                BaseCurrency = "TWD", OriginalBaseAmount = amount, OpenBaseAmount = amount,
                PostingDate = new DateOnly(2026, 11, 2)
            };
            // 收款過帳已建立收款客戶的信用鍵；只補缺少的。
            foreach (var key in new[] { customerId, otherCustomerId })
                if (!await db.Set<CreditKey>().AnyAsync(x => x.CustomerId == key, Ct))
                    db.Add(new CreditKey { CompanyId = companyId, CustomerId = key });
            var a = Invoice(customerId, 7001, 400m);
            var b = Invoice(customerId, 7002, 700m);
            var o = Invoice(otherCustomerId, 7003, 500m);
            db.AddRange(a, b, o);
            await db.SaveChangesAsync(Ct);
            (invoiceA, invoiceB, other) = (a.Id, b.Id, o.Id);
        }
        return new(companyId, customerId, receiptId, receipt.GetProperty("documentNo").GetString()!, draftReceipt,
            invoiceA, invoiceB, other, admin);
    }

    private static async Task FlowAsync(Setup setup, long approverId)
    {
        using var response = await setup.Admin.PostAsJsonAsync("/api/v1/sys/approval-flows", new
        {
            documentType = "AR.SETTLEMENT", name = "Settlement", allowSelfApproval = false,
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
        var role = await roles.CreateAsync(new RoleInput("R" + Guid.NewGuid().ToString("N")[..12], "Settlement role",
            null, DataScope.Company, fullAccess, permissions), Ct);
        var username = "ws" + Guid.NewGuid().ToString("N")[..20];
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
