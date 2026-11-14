using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-COLL-WF-001～009（WU-FI-005b3a，ADR-128）：收款單據送審／撤回、SYS 最終審批結果處理器（同交易呼叫收款過帳）、
/// 零步驟流程，以及 FI 正式收款來源埠 <c>CollectionReceiptDocumentSource</c>，對真實 SQL。SYS 審批引擎以替身注入
/// （行為同 SYS：送審在呼叫者 DbContext 上保存、一張單據只可有一個審批中輪次），結果處理器以 SYS 的方式在交易內、
/// SaveChanges 之前呼叫；SYS 本身的引擎在 SYS 測試已驗，HTTP 端到端在 005b4。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static readonly string[] AllReceiptWorkflowPermissions = [.. AllReceiptDocPermissions, "AR.COLLECTION.SUBMIT"];

    /// <summary>SYS 審批引擎替身：送審狀態可設為 Pending／Approved（零步驟）或回傳不屬於本單的狀態。</summary>
    private sealed class FakeApprovalEngine
    {
        public ApprovalStatus SubmitStatus = ApprovalStatus.Pending;
        public bool ReturnForeignDocument;
        public readonly List<ApprovalSubmission> Submissions = [];
        public readonly List<long> Cancels = [];
        public readonly HashSet<long> Pending = [];
        public long NextId = 500;
    }

    private sealed class FakeApprovalWorkflow(FakeApprovalEngine engine, MyErpDbContext db) : IApprovalWorkflowService
    {
        public async Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            if (engine.Pending.Contains(submission.DocumentId))
                throw new ConflictException("SYS.APPROVAL.ALREADY_PENDING", "Already pending.");
            engine.Submissions.Add(submission);
            await db.SaveChangesAsync(cancellationToken); // 與 SYS 相同：呼叫者未保存的單據變更一起保存
            var id = ++engine.NextId;
            if (engine.SubmitStatus == ApprovalStatus.Pending)
                engine.Pending.Add(submission.DocumentId);
            var documentId = engine.ReturnForeignDocument ? submission.DocumentId + 1 : submission.DocumentId;
            return new(id, submission.DocumentType, documentId, engine.SubmitStatus,
                engine.SubmitStatus == ApprovalStatus.Pending ? 1 : null, engine.SubmitStatus == ApprovalStatus.Pending ? 1 : 0);
        }

        public async Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default)
        {
            if (!engine.Pending.Remove(documentId))
                throw new BusinessRuleException("SYS.APPROVAL.NOT_PENDING", "The document has no pending approval.");
            engine.Cancels.Add(documentId);
            await db.SaveChangesAsync(cancellationToken);
            return new(engine.NextId, documentType, documentId, ApprovalStatus.Cancelled, null, 1);
        }

        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalState?>(null);
    }

    private ServiceProvider ReceiptWorkflowProvider(long companyId, ReceiptDocActor actor, FakeApprovalEngine engine) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(actor);
            services.AddSingleton(engine);
            services.AddScoped<ICurrentAuthorization, ReceiptDocAuthorization>();
            services.AddScoped<ICustomerLookup, ReceiptDocCustomers>();
            services.AddScoped<INumberGenerator, ReceiptDocNumbers>();
            services.AddScoped<IApprovalWorkflowService, FakeApprovalWorkflow>();
        });

    private static ReceiptDocActor WorkflowActor() => new() { Permissions = AllReceiptWorkflowPermissions };

    private static async Task<T> ReceiptSubmitAsync<T>(ServiceProvider provider,
        Func<CollectionReceiptSubmissionService, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CollectionReceiptSubmissionService>());
    }

    /// <summary>以 SYS 的方式交付最終決定：交易內、SaveChanges 之前呼叫處理器，成功才提交。</summary>
    private static async Task DecideReceiptAsync(ServiceProvider provider, long documentId, ApprovalStatus status,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        var handler = sp.GetServices<IApprovalResultHandler>().Single(x => x.DocumentType == "AR.COLLECTION");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await handler.HandleAsync(new ApprovalCompleted(501, "AR.COLLECTION", documentId, status, 9,
            status == ApprovalStatus.Rejected ? "金額不符" : null), token);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private async Task<(string Status, int Version, bool Deleted)> ReceiptDocRowAsync(long id)
    {
        await using var db = database.CreateRawContext();
        var row = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(status, '|', version, '|', CAST(is_deleted AS int)) AS [Value] " +
            "FROM dbo.ar_collection_receipt_doc WHERE id = {0}", id).SingleAsync(TestContext.Current.CancellationToken);
        var parts = row.Split('|');
        return (parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2] == "1");
    }

    private sealed record PostedReceiptRow(long CustomerId, int SourceVersion, decimal Amount, decimal UnappliedAmount,
        long BankAccountId, string BankReference, string BaseCurrency, string Fingerprint);

    private async Task<PostedReceiptRow?> PostedReceiptForAsync(long companyId, long documentId)
    {
        await using var db = database.CreateRawContext();
        return await db.Database.SqlQueryRaw<PostedReceiptRow>(
            "SELECT customer_id, source_version, amount, unapplied_amount, bank_account_id, bank_reference, " +
            "base_currency, fingerprint FROM dbo.ar_collection_receipt " +
            "WHERE company_id = {0} AND source_id = {1}", companyId, documentId)
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubmitMovesDraftToPendingAndFinalApprovalPostsInTheSameTransaction()
    {
        // FI-COLL-WF-001／002（正常）：送審 → Pending，SYS 收到 AR.COLLECTION、單號、本位幣金額、備註；未過帳。
        // 最終核准 → 同交易過帳（借銀行／貸暫收、未核銷＝金額），單據 Posted；過帳來源版本＝核准時版本、指紋＝正式來源指紋。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = ReceiptWorkflowProvider(companyId, WorkflowActor(), engine);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(
            ReceiptDocInput(setup.BankId, "WF-1", 750.25m, remark: "十一月款"), token));

        var submitted = await ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, draft.Version, token));
        Assert.Equal((FinanceCollectionReceiptState.Pending, 2, ApprovalStatus.Pending, (CollectionReceiptPostingReceipt?)null),
            (submitted.Status, submitted.Version, submitted.Approval.Status, submitted.Posting));
        Assert.Equal(new ApprovalSubmission("AR.COLLECTION", draft.Id, draft.DocumentNo, 750.25m, "十一月款"),
            Assert.Single(engine.Submissions));
        Assert.Equal(("Pending", 2, false), await ReceiptDocRowAsync(draft.Id));
        Assert.Equal((0, 0), ((await CollectionCountsAsync(companyId, token)).Receipts, (await CollectionCountsAsync(companyId, token)).Journals));

        await DecideReceiptAsync(provider, draft.Id, ApprovalStatus.Approved, token);

        Assert.Equal(("Posted", 4, false), await ReceiptDocRowAsync(draft.Id));
        var posted = await PostedReceiptForAsync(companyId, draft.Id);
        Assert.NotNull(posted);
        Assert.Equal((CollectionCustomerId, 3, 750.25m, 750.25m, setup.BankId, "WF-1", "TWD"),
            (posted.CustomerId, posted.SourceVersion, posted.Amount, posted.UnappliedAmount, posted.BankAccountId,
                posted.BankReference, posted.BaseCurrency));
        Assert.StartsWith(CollectionReceiptFingerprint.Prefix, posted.Fingerprint, StringComparison.Ordinal);
        Assert.Equal((1, 1), ((await CollectionCountsAsync(companyId, token)).Receipts, (await CollectionCountsAsync(companyId, token)).Journals));
        var detail = await ReceiptDocAsync(provider, s => s.GetAsync(draft.Id, token));
        Assert.Equal(FinanceCollectionReceiptState.Posted, detail.Status);
    }

    [Fact]
    public async Task RejectionReturnsTheReceiptForEditingWithoutAnyLedgerEffect()
    {
        // FI-COLL-WF-003（駁回）：駁回 → Rejected、無 GL／收款列；修改回 Draft 後可再送審（新輪次）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = ReceiptWorkflowProvider(companyId, WorkflowActor(), engine);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "WF-R"), token));
        await ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        engine.Pending.Clear(); // SYS 完成該輪

        await DecideReceiptAsync(provider, draft.Id, ApprovalStatus.Rejected, token);

        Assert.Equal(("Rejected", 3, false), await ReceiptDocRowAsync(draft.Id));
        Assert.Null(await PostedReceiptForAsync(companyId, draft.Id));
        Assert.Equal(0, (await CollectionCountsAsync(companyId, token)).Journals);
        var resubmitRejected = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 3, token)));
        Assert.Equal(CollectionReceiptDocumentErrors.InvalidStatus, resubmitRejected.ErrorCode);
        var edited = await ReceiptDocAsync(provider, s => s.ReplaceAsync(draft.Id, 3,
            ReceiptDocInput(setup.BankId, "WF-R", 520m), token));
        var again = await ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, edited.Version, token));
        Assert.Equal((FinanceCollectionReceiptState.Pending, 2), (again.Status, engine.Submissions.Count));
    }

    [Fact]
    public async Task ZeroStepWorkflowPostsDuringSubmit()
    {
        // FI-COLL-WF-004（零步驟）：SYS 立即核准 → 送審交易內過帳，回傳 Posted 與過帳結果。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine { SubmitStatus = ApprovalStatus.Approved };
        await using var provider = ReceiptWorkflowProvider(companyId, WorkflowActor(), engine);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "WF-0", 99m), token));

        var result = await ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));

        Assert.Equal((FinanceCollectionReceiptState.Posted, 4, ApprovalStatus.Approved),
            (result.Status, result.Version, result.Approval.Status));
        Assert.NotNull(result.Posting);
        Assert.Equal((draft.Id, 99m, 99m, 3), (result.Posting.ReceiptId, result.Posting.Amount,
            result.Posting.UnappliedAmount, result.Posting.SourceVersion));
        Assert.Equal(("Posted", 4, false), await ReceiptDocRowAsync(draft.Id));
        Assert.NotNull(await PostedReceiptForAsync(companyId, draft.Id));
    }

    [Fact]
    public async Task PostingFailureOnFinalApprovalRollsBackTheWholeDecision()
    {
        // FI-COLL-WF-005（錯誤／回滾）：資金帳戶無對應科目 → 過帳 422 FUND_ACCOUNT_INVALID，處理器拋出；
        // 審批交易回滾：單據仍 Pending、版本不變、無 GL／收款列。零步驟時送審整筆回滾，單據仍 Draft、SYS 輪次未保存。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = ReceiptWorkflowProvider(companyId, WorkflowActor(), engine);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var cash = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.CashBoxId, "WF-F"), token));
        await ReceiptSubmitAsync(provider, s => s.SubmitAsync(cash.Id, 1, token));

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            DecideReceiptAsync(provider, cash.Id, ApprovalStatus.Approved, token));
        Assert.Equal(CollectionPostingErrors.FundAccountInvalid, failure.ErrorCode);
        Assert.Equal(("Pending", 2, false), await ReceiptDocRowAsync(cash.Id));
        Assert.Equal((0, 0), ((await CollectionCountsAsync(companyId, token)).Receipts, (await CollectionCountsAsync(companyId, token)).Journals));

        engine.SubmitStatus = ApprovalStatus.Approved;
        var zero = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.CashBoxId, "WF-F0"), token));
        var zeroFailure = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ReceiptSubmitAsync(provider, s => s.SubmitAsync(zero.Id, 1, token)));
        Assert.Equal(CollectionPostingErrors.FundAccountInvalid, zeroFailure.ErrorCode);
        Assert.Equal(("Draft", 1, false), await ReceiptDocRowAsync(zero.Id));
        Assert.Equal(0, (await CollectionCountsAsync(companyId, token)).Journals);
    }

    [Fact]
    public async Task WithdrawCancelsThePendingRoundAndReturnsToDraft()
    {
        // FI-COLL-WF-006（撤回／狀態限制／版本）：Pending 撤回 → SYS 輪次取消、單據 Draft；Draft 撤回 422；舊版本 409；
        // 撤回後可修改、可再送審。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = ReceiptWorkflowProvider(companyId, WorkflowActor(), engine);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "WF-W"), token));

        var notPending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ReceiptSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        Assert.Equal(CollectionReceiptDocumentErrors.InvalidStatus, notPending.ErrorCode);
        await ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        await Assert.ThrowsAsync<ConflictException>(() => ReceiptSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        Assert.Empty(engine.Cancels);

        var withdrawn = await ReceiptSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 2, token));
        Assert.Equal((FinanceCollectionReceiptState.Draft, 3, ApprovalStatus.Cancelled),
            (withdrawn.Status, withdrawn.Version, withdrawn.Approval.Status));
        Assert.Equal([draft.Id], engine.Cancels);
        var edited = await ReceiptDocAsync(provider, s => s.ReplaceAsync(draft.Id, 3, ReceiptDocInput(setup.BankId, "WF-W2"), token));
        Assert.Equal(FinanceCollectionReceiptState.Pending,
            (await ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, edited.Version, token))).Status);
    }

    [Fact]
    public async Task SubmitGuardsPermissionCompanyVersionStatusAndMasterData()
    {
        // FI-COLL-WF-007（權限／狀態／關聯）：缺 SUBMIT 403、其他公司 404、已刪除 404、舊版本 409、Pending 422；
        // 送審時資金帳戶已停用 422（不送 SYS、不改單）；SYS 回傳不屬於本單的狀態 409 且整筆回滾。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var actor = WorkflowActor();
        await using var provider = ReceiptWorkflowProvider(companyId, actor, engine);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "WF-G"), token));

        actor.Permissions = AllReceiptDocPermissions;
        await Assert.ThrowsAsync<ForbiddenException>(() => ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => ReceiptSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        actor.Permissions = AllReceiptWorkflowPermissions;
        await using (var other = ReceiptWorkflowProvider(NewCompany(), WorkflowActor(), engine))
            await Assert.ThrowsAsync<NotFoundException>(() => ReceiptSubmitAsync(other, s => s.SubmitAsync(draft.Id, 1, token)));
        await Assert.ThrowsAsync<ConflictException>(() => ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 7, token)));

        engine.ReturnForeignDocument = true;
        var invalid = await Assert.ThrowsAsync<ConflictException>(() => ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        Assert.Equal(CollectionReceiptWorkflowErrors.ApprovalResultInvalid, invalid.ErrorCode);
        Assert.Equal(("Draft", 1, false), await ReceiptDocRowAsync(draft.Id));
        engine.ReturnForeignDocument = false;
        engine.Pending.Clear();
        engine.Submissions.Clear();

        var bank = await FundAsync(provider, setup.BankId, token);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FundAccountService>().UpdateAsync(bank.Id, bank.Name,
                new(FundAccountType.Bank, "Mega", "001", setup.BankLedgerId), isActive: false, bank.Version, token);
        var inactive = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        Assert.Equal(CollectionReceiptDocumentErrors.FundAccountInactive, inactive.ErrorCode);
        Assert.Empty(engine.Submissions);
        Assert.Equal(("Draft", 1, false), await ReceiptDocRowAsync(draft.Id));

        var cash = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.CashBoxId, "WF-G2"), token));
        await ReceiptSubmitAsync(provider, s => s.SubmitAsync(cash.Id, 1, token));
        var pending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ReceiptSubmitAsync(provider, s => s.SubmitAsync(cash.Id, 2, token)));
        Assert.Equal(CollectionReceiptDocumentErrors.InvalidStatus, pending.ErrorCode);
        var deleted = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.CashBoxId, "WF-G3"), token));
        await ReceiptDocAsync(provider, s => s.DeleteAsync(deleted.Id, 1, token));
        await Assert.ThrowsAsync<NotFoundException>(() => ReceiptSubmitAsync(provider, s => s.SubmitAsync(deleted.Id, 1, token)));
    }

    [Fact]
    public async Task ResultHandlerOnlyActsOnAPendingReceiptInsideATransaction()
    {
        // FI-COLL-WF-008（狀態限制）：處理器類型 AR.COLLECTION；單據非 Pending（Draft、已 Posted 再次核准）409 且不改；
        // 無交易 InvalidOperation；撤回等非最終狀態 ArgumentOutOfRange；不存在 404。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = ReceiptWorkflowProvider(companyId, WorkflowActor(), engine);
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "WF-H"), token));

        var changed = await Assert.ThrowsAsync<ConflictException>(() =>
            DecideReceiptAsync(provider, draft.Id, ApprovalStatus.Approved, token));
        Assert.Equal(CollectionReceiptWorkflowErrors.ApprovalStateChanged, changed.ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => DecideReceiptAsync(provider, 999_999, ApprovalStatus.Rejected, token));
        await ReceiptSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        await DecideReceiptAsync(provider, draft.Id, ApprovalStatus.Approved, token);
        await Assert.ThrowsAsync<ConflictException>(() => DecideReceiptAsync(provider, draft.Id, ApprovalStatus.Approved, token));
        Assert.Equal(("Posted", 4, false), await ReceiptDocRowAsync(draft.Id));

        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IApprovalResultHandler>().Single(x => x.DocumentType == "AR.COLLECTION");
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new(501, "AR.COLLECTION", draft.Id, ApprovalStatus.Approved, 9, null), token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync(
            new(501, "AR.COLLECTION", draft.Id, ApprovalStatus.Cancelled, 9, null), token));
    }

    [Fact]
    public async Task ProductionReceiptSourceReadsTheSavedDocumentThroughTheCallerTransaction()
    {
        // FI-COLL-WF-009（來源埠）：經呼叫者交易讀取；Draft／Deleted 狀態映射；其他公司與不存在 null；指紋穩定、
        // 不因狀態改變而變，金額改變即不同；版本＝列版本；本位幣為 MD 目前本位幣；非呼叫者交易 InvalidOperation。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = ReceiptWorkflowProvider(companyId, WorkflowActor(), new FakeApprovalEngine());
        var setup = await SeedCollectionAsync(provider, companyId, token);
        var draft = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "WF-S", 321.5m), token));
        var removed = await ReceiptDocAsync(provider, s => s.CreateAsync(ReceiptDocInput(setup.BankId, "WF-S2"), token));
        await ReceiptDocAsync(provider, s => s.DeleteAsync(removed.Id, 1, token));

        async Task<FinanceCollectionReceiptSnapshot?> ReadAsync(long company, long id)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var source = Assert.Single(scope.ServiceProvider.GetServices<IFinanceCollectionReceiptSource>());
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            return await source.ReadReceiptAsync(company, id, transaction.GetDbTransaction(), token);
        }

        var first = await ReadAsync(companyId, draft.Id);
        Assert.NotNull(first);
        Assert.Equal((companyId, draft.Id, 1, CollectionCustomerId, FinanceCollectionReceiptState.Draft, BusinessDate,
                "TWD", "TWD", 321.5m, setup.BankId, "WF-S"),
            (first.CompanyId, first.ReceiptId, first.SourceVersion, first.CustomerId, first.State, first.BusinessDate,
                first.Currency, first.BaseCurrency, first.Amount, first.BankAccountId, first.BankReference));
        Assert.Matches("^FI-COLLECTION-RECEIPT-v1:[0-9a-f]{64}$", first.Fingerprint);
        Assert.Equal(first, await ReadAsync(companyId, draft.Id));
        Assert.Equal(FinanceCollectionReceiptState.Deleted, (await ReadAsync(companyId, removed.Id))!.State);
        Assert.Null(await ReadAsync(companyId + 1, draft.Id));
        Assert.Null(await ReadAsync(companyId, 999_999_999));

        await SetReceiptDocStatusAsync(draft.Id, "Approved");
        var approved = await ReadAsync(companyId, draft.Id);
        Assert.Equal((FinanceCollectionReceiptState.Approved, 2, first.Fingerprint),
            (approved!.State, approved.SourceVersion, approved.Fingerprint));
        await SetReceiptDocStatusAsync(draft.Id, "Draft");
        var edited = await ReceiptDocAsync(provider, s => s.ReplaceAsync(draft.Id, 3, ReceiptDocInput(setup.BankId, "WF-S", 321.6m), token));
        Assert.NotEqual(first.Fingerprint, (await ReadAsync(companyId, draft.Id))!.Fingerprint);
        Assert.Equal(4, edited.Version);

        await using var outer = provider.CreateAsyncScope();
        var source = outer.ServiceProvider.GetRequiredService<IFinanceCollectionReceiptSource>();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString);
        await connection.OpenAsync(token);
        await using var foreign = await connection.BeginTransactionAsync(token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ReadReceiptAsync(companyId, draft.Id, foreign, token));
    }
}
