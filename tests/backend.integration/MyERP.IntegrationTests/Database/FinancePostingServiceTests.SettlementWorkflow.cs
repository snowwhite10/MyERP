using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-SWF-001～009（WU-FI-005b3b，ADR-128）：核銷單據送審／撤回、SYS <c>AR.SETTLEMENT</c> 最終審批結果處理器
/// （同交易呼叫核銷過帳服務）、零步驟流程，以及 FI 正式核銷來源埠 <c>CollectionSettlementDocumentSource</c>，對真實 SQL。
/// 收款 1000 先經正式收款過帳服務過帳（收款來源以測試假實作）；SYS 審批引擎以 005b3a 的替身注入（行為同 SYS），
/// 結果處理器以 SYS 的方式在交易內、SaveChanges 之前呼叫。HTTP 端到端在 005b4。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static readonly string[] AllSettlementWorkflowPermissions = [.. AllSettlementDocPermissions, "AR.SETTLEMENT.SUBMIT"];

    private ServiceProvider SettlementWorkflowProvider(long companyId, ReceiptDocActor actor, FakeApprovalEngine engine,
        FakeCollectionReceipts receipts, bool withMap = true) =>
        Provider(companyId, mapping: null, services =>
        {
            if (!withMap)
            {
                foreach (var map in services.Where(x => x.ServiceType == typeof(ISalesCollectionAccountMapSource)).ToList())
                    services.Remove(map);
            }
            services.AddSingleton(actor);
            services.AddSingleton(engine);
            services.AddSingleton(receipts);
            services.AddScoped<IFinanceCollectionReceiptSource, FakeCollectionReceiptSource>();
            services.AddScoped<ICurrentAuthorization, ReceiptDocAuthorization>();
            services.AddScoped<INumberGenerator, SettlementDocNumbers>();
            services.AddScoped<IApprovalWorkflowService, FakeApprovalWorkflow>();
        });

    private static ReceiptDocActor SettlementWorkflowActor() => new() { Permissions = AllSettlementWorkflowPermissions };

    private static async Task<T> SettlementSubmitAsync<T>(ServiceProvider provider,
        Func<CollectionSettlementSubmissionService, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CollectionSettlementSubmissionService>());
    }

    /// <summary>以 SYS 的方式交付最終決定：交易內、SaveChanges 之前呼叫處理器，成功才提交。</summary>
    private static async Task DecideSettlementAsync(ServiceProvider provider, long documentId, ApprovalStatus status,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        var handler = sp.GetServices<IApprovalResultHandler>().Single(x => x.DocumentType == "AR.SETTLEMENT");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await handler.HandleAsync(new ApprovalCompleted(601, "AR.SETTLEMENT", documentId, status, 9,
            status == ApprovalStatus.Rejected ? "分配有誤" : null), token);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private async Task<(string Status, int Version, bool Deleted)> SettlementDocRowAsync(long id)
    {
        await using var db = database.CreateRawContext();
        var row = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(status, '|', version, '|', CAST(is_deleted AS int)) AS [Value] " +
            "FROM dbo.ar_collection_settlement_doc WHERE id = {0}", id).SingleAsync(TestContext.Current.CancellationToken);
        var parts = row.Split('|');
        return (parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2] == "1");
    }

    private sealed record PostedSettlementRow(long CustomerId, int SourceVersion, long ReceiptId, decimal TotalAmount,
        string Fingerprint);

    private async Task<PostedSettlementRow?> PostedSettlementForAsync(long companyId, long documentId)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQueryRaw<PostedSettlementRow>(
            "SELECT customer_id, source_version, receipt_id, total_amount, fingerprint FROM dbo.ar_collection_settlement " +
            "WHERE company_id = {0} AND source_id = {1}", companyId, documentId)
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SettlementSubmitMovesDraftToPendingAndFinalApprovalPostsInTheSameTransaction()
    {
        // FI-COLL-SWF-001／002（正常）：送審 → Pending，SYS 收到 AR.SETTLEMENT、單號、合計、備註；不改餘額。最終核准 →
        // 同交易核銷過帳（收款未核銷與應收未清減少、一張傳票），單據 Posted；來源版本＝核准時版本、指紋＝正式來源指紋。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), engine, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6101, 600m, token: token);
        var b = await AddReceivableAsync(provider, 6102, 700m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(
            SettlementInput((b, 300m), (a, 600m)) with { Remark = "十一月核銷" }, token));

        var submitted = await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, draft.Version, token));
        Assert.Equal((FinanceCollectionWriteOffState.Pending, 2, ApprovalStatus.Pending, (CollectionWriteOffPostingReceipt?)null),
            (submitted.Status, submitted.Version, submitted.Approval.Status, submitted.Posting));
        Assert.Equal(new ApprovalSubmission("AR.SETTLEMENT", draft.Id, draft.DocumentNo, 900m, "十一月核銷"),
            Assert.Single(engine.Submissions));
        Assert.Equal(("Pending", 2, false), await SettlementDocRowAsync(draft.Id));
        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));

        await DecideSettlementAsync(provider, draft.Id, ApprovalStatus.Approved, token);

        Assert.Equal(("Posted", 4, false), await SettlementDocRowAsync(draft.Id));
        var posted = await PostedSettlementForAsync(companyId, draft.Id);
        Assert.NotNull(posted);
        Assert.Equal((CollectionCustomerId, 3, 900m),
            (posted.CustomerId, posted.SourceVersion, posted.TotalAmount)); // receipt_id 為內部收款列 ID
        Assert.Matches("^FI-COLLECTION-SETTLEMENT-v1:[0-9a-f]{64}$", posted.Fingerprint);
        Assert.Equal((1, 2, 2, 100m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([0m, 400m], await OpenAmountsAsync(companyId, token, a, b));
        Assert.Equal(FinanceCollectionWriteOffState.Posted,
            (await SettlementDocAsync(provider, s => s.GetAsync(draft.Id, token))).Status);
    }

    [Fact]
    public async Task SettlementRejectionReturnsItForEditingWithoutAnyLedgerEffect()
    {
        // FI-COLL-SWF-003（駁回）：駁回 → Rejected、無核銷／傳票、餘額不變；Rejected 不可直接送審 422；修改回 Draft 後可再送審。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), engine, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6201, 500m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 500m)), token));
        await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        engine.Pending.Clear(); // SYS 完成該輪

        await DecideSettlementAsync(provider, draft.Id, ApprovalStatus.Rejected, token);

        Assert.Equal(("Rejected", 3, false), await SettlementDocRowAsync(draft.Id));
        Assert.Null(await PostedSettlementForAsync(companyId, draft.Id));
        Assert.Equal((0, 0, 1, 1_000m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([500m], await OpenAmountsAsync(companyId, token, a));
        var resubmitRejected = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 3, token)));
        Assert.Equal(CollectionSettlementDocumentErrors.InvalidStatus, resubmitRejected.ErrorCode);
        var edited = await SettlementDocAsync(provider, s => s.ReplaceAsync(draft.Id, 3, SettlementInput((a, 450m)), token));
        var again = await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, edited.Version, token));
        Assert.Equal((FinanceCollectionWriteOffState.Pending, 2), (again.Status, engine.Submissions.Count));
    }

    [Fact]
    public async Task ZeroStepSettlementWorkflowPostsDuringSubmit()
    {
        // FI-COLL-SWF-004（零步驟）：SYS 立即核准 → 送審交易內過帳，回傳 Posted 與核銷過帳結果（分配與餘額）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine { SubmitStatus = ApprovalStatus.Approved };
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), engine, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6301, 250m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 250m)), token));

        var result = await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));

        Assert.Equal((FinanceCollectionWriteOffState.Posted, 4, ApprovalStatus.Approved),
            (result.Status, result.Version, result.Approval.Status));
        Assert.NotNull(result.Posting);
        Assert.Equal((draft.Id, WriteOffReceiptId, 250m, 750m, 3),
            (result.Posting.SettlementId, result.Posting.ReceiptId, result.Posting.TotalAmount,
                result.Posting.ReceiptUnappliedAmount, result.Posting.SourceVersion));
        Assert.Equal([new CollectionWriteOffAllocationReceipt(a, 250m, 0m)], result.Posting.Allocations);
        Assert.Equal(("Posted", 4, false), await SettlementDocRowAsync(draft.Id));
        Assert.Equal((1, 1, 2, 750m), await WriteOffStateAsync(companyId, token));
    }

    [Fact]
    public async Task SettlementPostingFailureRollsBackTheDecisionAndSubmitRechecksTheDraft()
    {
        // FI-COLL-SWF-005（錯誤／回滾／重驗）：待審期間另一核銷用完應收 → 最終核准過帳 422 ALLOCATION_INVALID（含 lineErrors），
        // 審批整筆回滾：單據仍 Pending、版本不變、無新核銷。送審時以草稿規則重驗：已無未清的草稿 422 同碼＋lineErrors、
        // 不送 SYS、仍 Draft。零步驟過帳失敗（科目對應缺）時送審整筆回滾，單據仍 Draft。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), engine, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6401, 300m, token: token);
        var b = await AddReceivableAsync(provider, 6402, 100m, token: token);
        var pending = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 300m)), token));
        var stale = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 200m)), token));
        await SettlementSubmitAsync(provider, s => s.SubmitAsync(pending.Id, 1, token));
        engine.SubmitStatus = ApprovalStatus.Approved;
        var consumer = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 250m)), token));
        await SettlementSubmitAsync(provider, s => s.SubmitAsync(consumer.Id, 1, token));
        Assert.Equal((1, 1, 2, 750m), await WriteOffStateAsync(companyId, token));

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            DecideSettlementAsync(provider, pending.Id, ApprovalStatus.Approved, token));
        Assert.Equal(CollectionWriteOffPostingErrors.AllocationInvalid, failure.ErrorCode);
        var lineErrors = Assert.IsAssignableFrom<IReadOnlyList<CollectionWriteOffLineError>>(
            failure.Extensions![CollectionSettlementDocumentService.LineErrorsExtension]);
        Assert.Equal(a, Assert.Single(lineErrors).ReceivableId);
        Assert.Equal(("Pending", 2, false), await SettlementDocRowAsync(pending.Id));
        Assert.Equal((1, 1, 2, 750m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([50m], await OpenAmountsAsync(companyId, token, a));

        engine.Submissions.Clear();
        var recheck = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            SettlementSubmitAsync(provider, s => s.SubmitAsync(stale.Id, 1, token)));
        Assert.Equal(CollectionWriteOffPostingErrors.AllocationInvalid, recheck.ErrorCode);
        Assert.True(recheck.Extensions!.ContainsKey(CollectionSettlementDocumentService.LineErrorsExtension));
        Assert.Empty(engine.Submissions);
        Assert.Equal(("Draft", 1, false), await SettlementDocRowAsync(stale.Id));

        var zero = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((b, 100m)), token));
        await using (var noMap = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), engine, receipts, withMap: false))
        {
            var zeroFailure = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                SettlementSubmitAsync(noMap, s => s.SubmitAsync(zero.Id, 1, token)));
            Assert.Equal(CollectionWriteOffPostingErrors.MappingMissing, zeroFailure.ErrorCode);
        }
        Assert.Equal(("Draft", 1, false), await SettlementDocRowAsync(zero.Id));
        Assert.Equal((1, 1, 2, 750m), await WriteOffStateAsync(companyId, token));
        Assert.Equal([100m], await OpenAmountsAsync(companyId, token, b));
    }

    [Fact]
    public async Task SettlementWithdrawCancelsThePendingRoundAndReturnsToDraft()
    {
        // FI-COLL-SWF-006（撤回／狀態限制／版本）：Draft 撤回 422；舊版本 409；Pending 撤回 → SYS 輪次取消、Draft；可再修改送審。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), engine, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6501, 500m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 500m)), token));

        var notPending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            SettlementSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        Assert.Equal(CollectionSettlementDocumentErrors.InvalidStatus, notPending.ErrorCode);
        await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        await Assert.ThrowsAsync<ConflictException>(() => SettlementSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        Assert.Empty(engine.Cancels);

        var withdrawn = await SettlementSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 2, token));
        Assert.Equal((FinanceCollectionWriteOffState.Draft, 3, ApprovalStatus.Cancelled),
            (withdrawn.Status, withdrawn.Version, withdrawn.Approval.Status));
        Assert.Equal([draft.Id], engine.Cancels);
        var edited = await SettlementDocAsync(provider, s => s.ReplaceAsync(draft.Id, 3, SettlementInput((a, 400m)), token));
        Assert.Equal(FinanceCollectionWriteOffState.Pending,
            (await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, edited.Version, token))).Status);
    }

    [Fact]
    public async Task SettlementSubmitGuardsPermissionCompanyVersionStatusAndSysResult()
    {
        // FI-COLL-SWF-007（權限／公司／版本／狀態）：缺 SUBMIT 403（送審與撤回）、其他公司 404、舊版本 409；SYS 回傳不屬於
        // 本單的狀態 409 且整筆回滾；Pending 再送審 422；已刪除 404。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var actor = SettlementWorkflowActor();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, actor, engine, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6601, 900m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 100m)), token));

        actor.Permissions = AllSettlementDocPermissions;
        await Assert.ThrowsAsync<ForbiddenException>(() => SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => SettlementSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        actor.Permissions = AllSettlementWorkflowPermissions;
        await using (var other = SettlementWorkflowProvider(NewCompany(), SettlementWorkflowActor(), engine, new FakeCollectionReceipts()))
            await Assert.ThrowsAsync<NotFoundException>(() => SettlementSubmitAsync(other, s => s.SubmitAsync(draft.Id, 1, token)));
        await Assert.ThrowsAsync<ConflictException>(() => SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 7, token)));

        engine.ReturnForeignDocument = true;
        var invalid = await Assert.ThrowsAsync<ConflictException>(() =>
            SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        Assert.Equal(CollectionSettlementWorkflowErrors.ApprovalResultInvalid, invalid.ErrorCode);
        Assert.Equal(("Draft", 1, false), await SettlementDocRowAsync(draft.Id));
        engine.ReturnForeignDocument = false;
        engine.Pending.Clear();

        await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        var pending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 2, token)));
        Assert.Equal(CollectionSettlementDocumentErrors.InvalidStatus, pending.ErrorCode);
        var deleted = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 50m)), token));
        await SettlementDocAsync(provider, s => s.DeleteAsync(deleted.Id, 1, token));
        await Assert.ThrowsAsync<NotFoundException>(() => SettlementSubmitAsync(provider, s => s.SubmitAsync(deleted.Id, 1, token)));
    }

    [Fact]
    public async Task SettlementResultHandlerOnlyActsOnAPendingSettlementInsideATransaction()
    {
        // FI-COLL-SWF-008（狀態限制）：處理器類型 AR.SETTLEMENT；非 Pending（Draft、已 Posted 再次核准）409 且不改；不存在 404；
        // 無交易 InvalidOperation；非最終狀態 ArgumentOutOfRange。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), engine, receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6701, 100m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 100m)), token));

        var changed = await Assert.ThrowsAsync<ConflictException>(() =>
            DecideSettlementAsync(provider, draft.Id, ApprovalStatus.Approved, token));
        Assert.Equal(CollectionSettlementWorkflowErrors.ApprovalStateChanged, changed.ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => DecideSettlementAsync(provider, 999_999, ApprovalStatus.Rejected, token));
        await SettlementSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        await DecideSettlementAsync(provider, draft.Id, ApprovalStatus.Approved, token);
        await Assert.ThrowsAsync<ConflictException>(() => DecideSettlementAsync(provider, draft.Id, ApprovalStatus.Approved, token));
        Assert.Equal(("Posted", 4, false), await SettlementDocRowAsync(draft.Id));
        Assert.Equal((1, 1, 2, 900m), await WriteOffStateAsync(companyId, token));

        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IApprovalResultHandler>().Single(x => x.DocumentType == "AR.SETTLEMENT");
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new(601, "AR.SETTLEMENT", draft.Id, ApprovalStatus.Approved, 9, null), token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync(
            new(601, "AR.SETTLEMENT", draft.Id, ApprovalStatus.Cancelled, 9, null), token));
    }

    [Fact]
    public async Task ProductionSettlementSourceReadsTheSavedDocumentAndLinesThroughTheCallerTransaction()
    {
        // FI-COLL-SWF-009（來源埠）：經呼叫者交易讀取；表頭與行（依 line_no）；Draft／Deleted 映射；其他公司與不存在 null；
        // 指紋穩定、不因狀態改變而變，行金額改變即不同；版本＝列版本；本位幣為 MD 目前本位幣；非呼叫者交易 InvalidOperation。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var receipts = new FakeCollectionReceipts();
        await using var provider = SettlementWorkflowProvider(companyId, SettlementWorkflowActor(), new FakeApprovalEngine(), receipts);
        await SeedWriteOffAsync(provider, receipts, companyId, token);
        var a = await AddReceivableAsync(provider, 6801, 400m, token: token);
        var b = await AddReceivableAsync(provider, 6802, 400m, token: token);
        var draft = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((b, 150m), (a, 200.5m)), token));
        var removed = await SettlementDocAsync(provider, s => s.CreateAsync(SettlementInput((a, 10m)), token));
        await SettlementDocAsync(provider, s => s.DeleteAsync(removed.Id, 1, token));

        async Task<FinanceCollectionWriteOffSnapshot?> ReadAsync(long company, long id)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var source = Assert.Single(scope.ServiceProvider.GetServices<IFinanceCollectionWriteOffSource>());
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            return await source.ReadWriteOffAsync(company, id, transaction.GetDbTransaction(), token);
        }

        var first = await ReadAsync(companyId, draft.Id);
        Assert.NotNull(first);
        Assert.Equal((companyId, draft.Id, 1, CollectionCustomerId, FinanceCollectionWriteOffState.Draft, BusinessDate,
                "TWD", "TWD", WriteOffReceiptId, 350.5m),
            (first.CompanyId, first.SettlementId, first.SourceVersion, first.CustomerId, first.State, first.BusinessDate,
                first.Currency, first.BaseCurrency, first.ReceiptId, first.TotalAmount));
        Assert.Equal([new FinanceCollectionWriteOffLine(a, 200.5m), new FinanceCollectionWriteOffLine(b, 150m)], first.Lines);
        Assert.Matches("^FI-COLLECTION-SETTLEMENT-v1:[0-9a-f]{64}$", first.Fingerprint);
        var again = await ReadAsync(companyId, draft.Id);
        Assert.Equal(first.Fingerprint, again!.Fingerprint);
        Assert.Equal(first.Lines, again.Lines);
        var gone = await ReadAsync(companyId, removed.Id);
        Assert.Equal((FinanceCollectionWriteOffState.Deleted, 1), (gone!.State, gone.Lines.Count));
        Assert.Null(await ReadAsync(companyId + 1, draft.Id));
        Assert.Null(await ReadAsync(companyId, 999_999_999));

        await SetSettlementDocStatusAsync(draft.Id, "Approved");
        var approved = await ReadAsync(companyId, draft.Id);
        Assert.Equal((FinanceCollectionWriteOffState.Approved, 2, first.Fingerprint),
            (approved!.State, approved.SourceVersion, approved.Fingerprint));
        await SetSettlementDocStatusAsync(draft.Id, "Draft");
        var edited = await SettlementDocAsync(provider, s => s.ReplaceAsync(draft.Id, 3,
            SettlementInput((b, 150m), (a, 200.4m)), token));
        Assert.Equal(4, edited.Version);
        Assert.NotEqual(first.Fingerprint, (await ReadAsync(companyId, draft.Id))!.Fingerprint);

        await using var outer = provider.CreateAsyncScope();
        var source = outer.ServiceProvider.GetRequiredService<IFinanceCollectionWriteOffSource>();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        await using var foreign = await connection.BeginTransactionAsync(token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ReadWriteOffAsync(companyId, draft.Id, foreign, token));
    }
}
