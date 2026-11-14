using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-SDOC-001～009（WU-FI-005b2，ADR-127）：FI 收款核銷單據草稿 <c>ar_collection_settlement_doc</c>／分配行
/// <c>ar_collection_settlement_doc_line</c> 與 <see cref="CollectionSettlementDocumentService"/> 的建立／修改／刪除／明細，
/// 對真實 SQL。收款先經正式收款過帳服務過帳（單據來源以測試假實作），發票應收直接寫入 FI 自有 <c>ar_posted_receivable</c>；
/// SYS 單號與授權以測試替身注入。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static readonly string[] AllSettlementDocPermissions =
        ["AR.SETTLEMENT.VIEW", "AR.SETTLEMENT.CREATE", "AR.SETTLEMENT.EDIT", "AR.SETTLEMENT.DELETE"];

    /// <summary>單號替身：在呼叫者交易內取號（與 SYS 相同），可模擬規則不存在的 422。</summary>
    private sealed class SettlementDocNumbers(ReceiptDocActor actor) : INumberGenerator
    {
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CollectionSettlementDocumentService.DocumentType, documentType);
            if (actor.FailNumbering)
                throw new BusinessRuleException("SYS.NUMBER_RULE.NOT_FOUND", "Number rule is missing.");
            return Task.FromResult($"WS{documentDate:yyyyMM}{Interlocked.Increment(ref actor.Numbers):0000}");
        }
    }

    private static ReceiptDocActor SettlementActor() => new() { Permissions = AllSettlementDocPermissions };

    private ServiceProvider SettlementDocProvider(long companyId, ReceiptDocActor actor, FakeCollectionReceipts receipts) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(actor);
            services.AddSingleton(receipts);
            services.AddScoped<IFinanceCollectionReceiptSource, FakeCollectionReceiptSource>();
            services.AddScoped<ICurrentAuthorization, ReceiptDocAuthorization>();
            services.AddScoped<INumberGenerator, SettlementDocNumbers>();
        });

    private static CollectionSettlementDocumentInput SettlementInput(params (long ReceivableId, decimal Amount)[] lines) =>
        new(WriteOffReceiptId, BusinessDate, lines.Select(x => new CollectionSettlementLineInput(x.ReceivableId, x.Amount)).ToArray(),
            null);

    private static async Task<T> SettlementDocAsync<T>(ServiceProvider provider,
        Func<CollectionSettlementDocumentService, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CollectionSettlementDocumentService>());
    }

    private static async Task SettlementDocAsync(ServiceProvider provider,
        Func<CollectionSettlementDocumentService, Task> action)
    {
        await using var scope = provider.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<CollectionSettlementDocumentService>());
    }

    /// <summary>單據數（未刪除或全部）與全部分配行數。</summary>
    private async Task<(int Documents, int Lines)> SettlementDocCountAsync(long companyId, bool includeDeleted = false)
    {
        await using var db = database.CreateRawContext();
        var documents = await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_settlement_doc WHERE company_id={companyId} AND (is_deleted=0 OR {includeDeleted}=1)")
            .SingleAsync(TestContext.Current.CancellationToken);
        var lines = await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM dbo.ar_collection_settlement_doc_line WHERE company_id={companyId}")
            .SingleAsync(TestContext.Current.CancellationToken);
        return (documents, lines);
    }

    private async Task SetSettlementDocStatusAsync(long id, string status)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "UPDATE dbo.ar_collection_settlement_doc SET status=@s, version=version+1 WHERE id=@id", connection);
        command.Parameters.AddWithValue("@s", status);
        command.Parameters.AddWithValue("@id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SettlementDraftTakesCustomerFromPostedReceiptSortsLinesAndChangesNoBalance()
    {
        // FI-COLL-SDOC-001（正常）：Draft、版本 1、單號在交易內取得、客戶取自已過帳收款、幣別取 MD 本位幣、合計＝行合計、
        // 行依應收 ID 升冪編號、備註修剪；明細帶收款未核銷與各應收目前未清；草稿不改 GL、收款未核銷或應收未清；
        // 草稿不保留額度（第二張草稿可用完同一收款）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, SettlementActor(), receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5101, 400m, token: token);
        var b = await AddReceivableAsync(provider, 5102, 700m, token: token);

        var created = await SettlementDocAsync(provider, s => s.CreateAsync(
            SettlementInput((b, 300.5m), (a, 400m)) with { Remark = "  部分核銷 " }, token));

        Assert.Equal(("WS2026110001", FinanceCollectionWriteOffState.Draft, 1, "TWD", CollectionCustomerId, 700.5m, "部分核銷"),
            (created.DocumentNo, created.Status, created.Version, created.Currency, created.CustomerId, created.TotalAmount,
                created.Remark));
        Assert.Equal((WriteOffReceiptId, (string?)null, (decimal?)1_000m, BusinessDate, 7L, (long?)null),
            (created.ReceiptId, created.ReceiptDocumentNo, created.ReceiptUnappliedAmount, created.BusinessDate,
                created.CreatedBy, created.UpdatedBy));
        Assert.Equal([new(1, a, 400m, 400m), new(2, b, 300.5m, 700m)], created.Lines);
        var read = await SettlementDocAsync(provider, s => s.GetAsync(created.Id, token));
        Assert.Equal(created.Lines, read.Lines);
        Assert.Equal(created with { Lines = read.Lines }, read);

        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([400m, 700m], await OpenAmountsAsync(companyId, token, a, b));
        var second = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 400m), (b, 600m)), token));
        Assert.Equal(("WS2026110002", 1_000m), (second.DocumentNo, second.TotalAmount));
        Assert.Equal((2, 4), await SettlementDocCountAsync(companyId));
    }

    [Fact]
    public async Task InvalidSettlementFieldsAre400WithEveryFieldAndSaveNothing()
    {
        // FI-COLL-SDOC-002（欄位邊界）：收款、日期、行（缺、空、超過 1000）、備註超長；逐行應收 ID（非正、重複）與金額
        // （0、負、5 位小數）以請求順序回報；合計溢位；不取號、不存列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = SettlementActor();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, actor, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);

        var all = await Assert.ThrowsAsync<RequestValidationException>(() => SettlementDocAsync(provider, s =>
            s.CreateAsync(new(0, default, [], new string('x', 501)), token)));
        Assert.Equal(["businessDate", "lines", "receiptId", "remark"], all.Errors.Keys.Order(StringComparer.Ordinal));
        var none = await Assert.ThrowsAsync<RequestValidationException>(() => SettlementDocAsync(provider, s =>
            s.CreateAsync(new(WriteOffReceiptId, BusinessDate, null, null), token)));
        Assert.Equal(["lines"], none.Errors.Keys);

        var lines = await Assert.ThrowsAsync<RequestValidationException>(() => SettlementDocAsync(provider, s =>
            s.CreateAsync(SettlementInput((0, 1m), (11, 0m), (12, 0.00001m), (13, -1m), (11, 1m), (11, 2m), (14, 5m)), token)));
        Assert.Equal(["lines[0].receivableId", "lines[1].amount", "lines[2].amount", "lines[3].amount",
                "lines[4].receivableId", "lines[5].receivableId"],
            lines.Errors.Keys.Order(StringComparer.Ordinal));

        var tooMany = await Assert.ThrowsAsync<RequestValidationException>(() => SettlementDocAsync(provider, s =>
            s.CreateAsync(SettlementInput(Enumerable.Range(1, 1001).Select(i => ((long)i, 1m)).ToArray()), token)));
        Assert.Equal(["lines"], tooMany.Errors.Keys);
        var overflow = await Assert.ThrowsAsync<RequestValidationException>(() => SettlementDocAsync(provider, s =>
            s.CreateAsync(SettlementInput((1, 999_999_999_999_999m), (2, 999_999_999_999_999m)), token)));
        Assert.Equal(["lines"], overflow.Errors.Keys);

        Assert.Equal(0, actor.Numbers);
        Assert.Equal((0, 0), await SettlementDocCountAsync(companyId, includeDeleted: true));
    }

    [Fact]
    public async Task SettlementReceiptMustBePostedNotLaterAndCoverTheTotal()
    {
        // FI-COLL-SDOC-003（關聯）：未過帳、不存在或其他公司的收款 422 RECEIPT_NOT_POSTED；核銷日早於收款日 422
        // RECEIPT_MISMATCH；合計超過收款未核銷 0.0001 即 422 RECEIPT_INSUFFICIENT，剛好等於可存；全部不取號、不存列。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = SettlementActor();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, actor, receipts);
        var setup = await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5201, 2_000m, token: token);
        receipts.Items[1001] = CollectionReceipt(companyId, 1001, setup.BankId, reference: "NOT-POSTED");

        async Task Expect(CollectionSettlementDocumentInput input, string code, ServiceProvider? on = null)
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                SettlementDocAsync(on ?? provider, s => s.CreateAsync(input, token)));
            Assert.Equal(code, error.ErrorCode);
        }

        await Expect(SettlementInput((a, 10m)) with { ReceiptId = 1001 }, CollectionWriteOffPostingErrors.ReceiptNotPosted);
        await Expect(SettlementInput((a, 10m)) with { ReceiptId = 999_999 }, CollectionWriteOffPostingErrors.ReceiptNotPosted);
        await Expect(SettlementInput((a, 10m)) with { BusinessDate = BusinessDate.AddDays(-1) },
            CollectionWriteOffPostingErrors.ReceiptMismatch);
        await Expect(SettlementInput((a, 1_000.0001m)), CollectionWriteOffPostingErrors.ReceiptInsufficient);

        var otherCompany = NewCompany();
        await using var other = SettlementDocProvider(otherCompany, SettlementActor(), new FakeCollectionReceipts());
        await SeedCollectionAsync(other, otherCompany, token, withMap: false);
        await Expect(SettlementInput((a, 10m)), CollectionWriteOffPostingErrors.ReceiptNotPosted, other);

        Assert.Equal(0, actor.Numbers);
        Assert.Equal((0, 0), await SettlementDocCountAsync(companyId, includeDeleted: true));
        var exact = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 1_000m)), token));
        Assert.Equal(1_000m, exact.TotalAmount);
    }

    [Fact]
    public async Task EveryUnusableLineIsReportedTogetherWithItsReasonAndNothingIsSaved()
    {
        // FI-COLL-SDOC-004（逐行原因、無部分成功）：超過未清、退貨應收、其他客戶、晚於核銷日、不存在的應收一次 422
        // ALLOCATION_INVALID，lineErrors 依應收 ID 升冪的行號與原因；剛好等於未清可存。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = SettlementActor();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, actor, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var open = await AddReceivableAsync(provider, 5301, 500m, open: 300m, token: token);
        var credit = await AddReceivableAsync(provider, 5302, 100m, type: "Return", token: token);
        var otherCustomer = await AddReceivableAsync(provider, 5303, 100m, customerId: 802, token: token);
        var later = await AddReceivableAsync(provider, 5304, 100m, date: BusinessDate.AddDays(1), token: token);
        const long missing = 999_999_999;

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => SettlementDocAsync(provider, s => s.CreateAsync(
            SettlementInput((missing, 10m), (later, 10m), (otherCustomer, 10m), (credit, 10m), (open, 300.0001m)), token)));
        Assert.Equal(CollectionWriteOffPostingErrors.AllocationInvalid, error.ErrorCode);
        var lineErrors = Assert.IsAssignableFrom<IReadOnlyList<CollectionWriteOffLineError>>(
            error.Extensions![CollectionSettlementDocumentService.LineErrorsExtension]);
        Assert.Equal(
        [
            new(1, open, CollectionWriteOffLineReasons.ExceedsOpen),
            new(2, credit, CollectionWriteOffLineReasons.ReceivableMismatch),
            new(3, otherCustomer, CollectionWriteOffLineReasons.ReceivableMismatch),
            new(4, later, CollectionWriteOffLineReasons.ReceivableAfterSettlement),
            new(5, missing, CollectionWriteOffLineReasons.ReceivableNotFound),
        ], lineErrors);
        Assert.Equal((0, (0, 0)), (actor.Numbers, await SettlementDocCountAsync(companyId, includeDeleted: true)));

        var exact = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((open, 300m)), token));
        Assert.Equal([new(1, open, 300m, 300m)], exact.Lines);
    }

    [Fact]
    public async Task SettlementSchemaRejectsDuplicateOrInvalidLinesAndForeignReceivables()
    {
        // FI-COLL-SDOC-005（結構守衛）：同單同應收重複行 2601／2627；行金額 0、行號 0、他公司應收（複合外鍵）、
        // 表頭合計 0、刪除列無刪除時間皆 547。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, SettlementActor(), receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5401, 100m, token: token);
        var created = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 50m)), token));
        var otherCompany = NewCompany();
        await using var other = SettlementDocProvider(otherCompany, SettlementActor(), new FakeCollectionReceipts());
        var foreign = await AddReceivableAsync(other, 5401, 100m, token: token);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        async Task<SqlException> FailAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@id", created.Id);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(token));
        }

        const string copyLine = """
            INSERT dbo.ar_collection_settlement_doc_line (company_id, settlement_doc_id, line_no, receivable_id, amount,
                created_at, created_by, version)
            SELECT company_id, settlement_doc_id, @no, @r, amount, created_at, created_by, 1
            FROM dbo.ar_collection_settlement_doc_line WHERE settlement_doc_id = @id
            """;
        var duplicate = await FailAsync(copyLine, ("@no", 2), ("@r", a));
        Assert.True(duplicate.Number is 2601 or 2627, duplicate.Message);
        Assert.Equal(547, (await FailAsync(copyLine, ("@no", 2), ("@r", foreign))).Number);
        Assert.Equal(547, (await FailAsync(
            "UPDATE dbo.ar_collection_settlement_doc_line SET amount = 0 WHERE settlement_doc_id = @id")).Number);
        Assert.Equal(547, (await FailAsync(
            "UPDATE dbo.ar_collection_settlement_doc_line SET line_no = 0 WHERE settlement_doc_id = @id")).Number);
        Assert.Equal(547, (await FailAsync(
            "UPDATE dbo.ar_collection_settlement_doc SET total_amount = 0 WHERE id = @id")).Number);
        Assert.Equal(547, (await FailAsync(
            "UPDATE dbo.ar_collection_settlement_doc SET is_deleted = 1 WHERE id = @id")).Number);
        Assert.Equal((1, 1), await SettlementDocCountAsync(companyId));
    }

    [Fact]
    public async Task SettlementReplaceSwapsLinesByVersionAndOnlyDraftOrRejectedWhichReturnsToDraft()
    {
        // FI-COLL-SDOC-006（狀態限制／版本）：修改整批替換行、遞增版本、保留單號；舊版本 409 與無效行 422 皆不改列；
        // Rejected 修改後回 Draft；Pending／Approved／Posted 422。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, SettlementActor(), receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5501, 300m, token: token);
        var b = await AddReceivableAsync(provider, 5502, 300m, token: token);
        var created = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 100m)), token));

        var updated = await SettlementDocAsync(provider, s => s.ReplaceAsync(created.Id, 1,
            SettlementInput((b, 200m), (a, 50m)) with { Remark = "改分配" }, token));
        Assert.Equal((2, 250m, "改分配", (long?)7, created.DocumentNo),
            (updated.Version, updated.TotalAmount, updated.Remark, updated.UpdatedBy, updated.DocumentNo));
        Assert.Equal([new(1, a, 50m, 300m), new(2, b, 200m, 300m)], updated.Lines);
        Assert.Equal((1, 2), await SettlementDocCountAsync(companyId));

        var stale = await Assert.ThrowsAsync<ConflictException>(() => SettlementDocAsync(provider, s =>
            s.ReplaceAsync(created.Id, 1, SettlementInput((a, 1m)), token)));
        Assert.Equal(CommonErrorCodes.ConcurrencyConflict, stale.ErrorCode);
        var invalid = await Assert.ThrowsAsync<BusinessRuleException>(() => SettlementDocAsync(provider, s =>
            s.ReplaceAsync(created.Id, 2, SettlementInput((a, 300.01m)), token)));
        Assert.Equal(CollectionWriteOffPostingErrors.AllocationInvalid, invalid.ErrorCode);
        var kept = await SettlementDocAsync(provider, s => s.GetAsync(created.Id, token));
        Assert.Equal((2, 250m), (kept.Version, kept.TotalAmount));
        Assert.Equal(updated.Lines, kept.Lines);

        await SetSettlementDocStatusAsync(created.Id, "Rejected");
        var redraft = await SettlementDocAsync(provider, s => s.ReplaceAsync(created.Id, 3,
            SettlementInput((b, 10m)), token));
        Assert.Equal((FinanceCollectionWriteOffState.Draft, 4, 10m), (redraft.Status, redraft.Version, redraft.TotalAmount));

        foreach (var status in new[] { "Pending", "Approved", "Posted" })
        {
            await SetSettlementDocStatusAsync(created.Id, status);
            var version = (await SettlementDocAsync(provider, s => s.GetAsync(created.Id, token))).Version;
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => SettlementDocAsync(provider, s =>
                s.ReplaceAsync(created.Id, version, SettlementInput((a, 5m)), token)));
            Assert.Equal(CollectionSettlementDocumentErrors.InvalidStatus, error.ErrorCode);
            Assert.Equal([new(1, b, 10m, 300m)],
                (await SettlementDocAsync(provider, s => s.GetAsync(created.Id, token))).Lines);
        }
    }

    [Fact]
    public async Task SettlementDeleteSoftDeletesDraftOrRejectedByVersionAndHidesTheDocument()
    {
        // FI-COLL-SDOC-007（狀態限制／已刪除記錄）：Draft、Rejected 可刪（軟刪除，保留單號與行）；舊版本 409；Pending 422；
        // 刪除後讀取、修改、再刪除皆 404。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, SettlementActor(), receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5601, 300m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 10m)), token));
        var rejected = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 20m)), token));
        var pending = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 30m)), token));
        await SetSettlementDocStatusAsync(rejected.Id, "Rejected");
        await SetSettlementDocStatusAsync(pending.Id, "Pending");

        await Assert.ThrowsAsync<ConflictException>(() => SettlementDocAsync(provider, s => s.DeleteAsync(draft.Id, 9, token)));
        await SettlementDocAsync(provider, s => s.DeleteAsync(draft.Id, 1, token));
        await SettlementDocAsync(provider, s => s.DeleteAsync(rejected.Id, 2, token));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            SettlementDocAsync(provider, s => s.DeleteAsync(pending.Id, 2, token)));
        Assert.Equal(CollectionSettlementDocumentErrors.InvalidStatus, error.ErrorCode);

        foreach (var id in new[] { draft.Id, rejected.Id })
        {
            Assert.Equal(CollectionSettlementDocumentErrors.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() =>
                SettlementDocAsync(provider, s => s.GetAsync(id, token)))).ErrorCode);
            await Assert.ThrowsAsync<NotFoundException>(() => SettlementDocAsync(provider, s =>
                s.ReplaceAsync(id, 2, SettlementInput((a, 1m)), token)));
            await Assert.ThrowsAsync<NotFoundException>(() => SettlementDocAsync(provider, s => s.DeleteAsync(id, 2, token)));
        }
        Assert.Equal(((1, 3), (3, 3)),
            (await SettlementDocCountAsync(companyId), await SettlementDocCountAsync(companyId, includeDeleted: true)));
    }

    [Fact]
    public async Task SettlementDocumentCommandsNeedTheirOwnPermissionAndCompany()
    {
        // FI-COLL-SDOC-008（權限）：缺少各動作權限 403 且不取號（收款權限不代替核銷權限）；授權公司不同 403；
        // 其他公司的 ID 404 且原單不變。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = SettlementActor();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, actor, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5701, 300m, token: token);
        var created = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 10m)), token));

        actor.Permissions = ["AR.SETTLEMENT.VIEW", .. AllReceiptDocPermissions];
        await Assert.ThrowsAsync<ForbiddenException>(() => SettlementDocAsync(provider, s =>
            s.CreateAsync(SettlementInput((a, 20m)), token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => SettlementDocAsync(provider, s =>
            s.ReplaceAsync(created.Id, 1, SettlementInput((a, 20m)), token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => SettlementDocAsync(provider, s => s.DeleteAsync(created.Id, 1, token)));
        Assert.Equal(created.Id, (await SettlementDocAsync(provider, s => s.GetAsync(created.Id, token))).Id);
        actor.Permissions = ["AR.SETTLEMENT.CREATE"];
        await Assert.ThrowsAsync<ForbiddenException>(() => SettlementDocAsync(provider, s => s.GetAsync(created.Id, token)));
        actor.Permissions = AllSettlementDocPermissions;
        actor.AuthorizedCompany = companyId + 1;
        await Assert.ThrowsAsync<ForbiddenException>(() => SettlementDocAsync(provider, s => s.GetAsync(created.Id, token)));
        Assert.Equal(1, actor.Numbers);

        var otherCompany = NewCompany();
        await using var other = SettlementDocProvider(otherCompany, SettlementActor(), new FakeCollectionReceipts());
        await Assert.ThrowsAsync<NotFoundException>(() => SettlementDocAsync(other, s => s.GetAsync(created.Id, token)));
        await Assert.ThrowsAsync<NotFoundException>(() => SettlementDocAsync(other, s =>
            s.ReplaceAsync(created.Id, 1, SettlementInput((a, 20m)), token)));
        await Assert.ThrowsAsync<NotFoundException>(() => SettlementDocAsync(other, s => s.DeleteAsync(created.Id, 1, token)));
        actor.AuthorizedCompany = null;
        var kept = await SettlementDocAsync(provider, s => s.GetAsync(created.Id, token));
        Assert.Equal((10m, 1), (kept.TotalAmount, kept.Version));
    }

    [Fact]
    public async Task SettlementNumberingFailureOrMissingBaseCurrencySavesNothing()
    {
        // FI-COLL-SDOC-009（錯誤／回滾）：單號規則不存在（SYS 422）→ 表頭與行皆不存；公司未設本位幣 → 422
        // BASE_CURRENCY_MISSING。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var actor = SettlementActor();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementDocProvider(companyId, actor, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 5801, 300m, token: token);
        actor.FailNumbering = true;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => SettlementDocAsync(provider, s =>
            s.CreateAsync(SettlementInput((a, 10m)), token)));
        Assert.Equal("SYS.NUMBER_RULE.NOT_FOUND", error.ErrorCode);
        Assert.Equal((0, 0), await SettlementDocCountAsync(companyId, includeDeleted: true));

        var bare = NewCompany();
        await using var empty = SettlementDocProvider(bare, SettlementActor(), new FakeCollectionReceipts());
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => SettlementDocAsync(empty, s =>
            s.CreateAsync(SettlementInput((a, 10m)), token)));
        Assert.Equal(CollectionReceiptDocumentErrors.BaseCurrencyMissing, missing.ErrorCode);
    }
}
