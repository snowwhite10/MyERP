using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Application;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// FI-OP-WF-001～007（WU-FI-008b2）：其他付款送審／撤回（<see cref="OtherPaymentSubmissionService"/>）與 SYS 最終審批
/// 結果處理器（同交易以已存版本＋<see cref="OtherPaymentFingerprint"/> 呼叫 <see cref="IOtherPaymentPostingService"/>），
/// 對真實 SQL。SYS 審批引擎沿用收款流程的替身（送審在呼叫者 DbContext 上保存、一張單據只可有一個審批中輪次）；
/// 處理器以 SYS 的方式在交易內、SaveChanges 之前呼叫。HTTP 端到端在 008b3。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private static readonly string[] AllOtherPaymentWorkflowPermissions =
        [.. AllOtherPaymentPermissions, "AP.OTHER_PAYMENT.SUBMIT"];

    private ServiceProvider OtherPaymentWorkflowProvider(long companyId, OtherPaymentActor actor,
        FakeApprovalEngine engine) =>
        Provider(companyId, mapping: null, services =>
        {
            services.AddSingleton(actor);
            services.AddSingleton(engine);
            services.AddScoped<ICurrentAuthorization, OtherPaymentAuthorization>();
            services.AddScoped<INumberGenerator, OtherPaymentNumbers>();
            services.AddScoped<IApprovalWorkflowService, FakeApprovalWorkflow>();
        });

    private static OtherPaymentActor OtherPaymentWorkflowActor() =>
        new() { Permissions = AllOtherPaymentWorkflowPermissions };

    private static async Task<T> OtherPaymentSubmitAsync<T>(ServiceProvider provider,
        Func<OtherPaymentSubmissionService, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<OtherPaymentSubmissionService>());
    }

    /// <summary>以 SYS 的方式交付最終決定：交易內、SaveChanges 之前呼叫處理器，成功才提交。</summary>
    private static async Task DecideOtherPaymentAsync(ServiceProvider provider, long documentId, ApprovalStatus status,
        CancellationToken token)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MyErpDbContext>();
        var handler = sp.GetServices<IApprovalResultHandler>().Single(x => x.DocumentType == "AP.OTHER_PAYMENT");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await handler.HandleAsync(new ApprovalCompleted(501, "AP.OTHER_PAYMENT", documentId, status, 9,
            status == ApprovalStatus.Rejected ? "缺少發票" : null), token);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    [Fact]
    public async Task OtherPaymentSubmitMovesDraftToPendingAndFinalApprovalPostsInTheSameTransaction()
    {
        // FI-OP-WF-001／002（正常，BR-FI-060／064）：送審 → Pending（v2），SYS 收到 AP.OTHER_PAYMENT、單號、金額、截斷備註；
        // 未過帳。最終核准 → 同交易過帳（借 6101／貸 1102），單據 Posted（v4）；來源版本＝核准時版本 3、指紋＝已存指紋。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = OtherPaymentWorkflowProvider(companyId, OtherPaymentWorkflowActor(), engine);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var remark = new string('運', 260);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, "WF-OP-1", 750.25m, remark: remark), token));

        var submitted = await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, draft.Version, token));
        Assert.Equal((OtherPaymentDocumentStatus.Pending, 2, ApprovalStatus.Pending, (OtherPaymentPostingReceipt?)null),
            (submitted.Status, submitted.Version, submitted.Approval.Status, submitted.Posting));
        Assert.Equal(new ApprovalSubmission("AP.OTHER_PAYMENT", draft.Id, draft.DocumentNo, 750.25m, remark[..200]),
            Assert.Single(engine.Submissions));
        Assert.Equal(("Pending", 2), await OtherPaymentStateAsync(draft.Id, token));
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));

        await DecideOtherPaymentAsync(provider, draft.Id, ApprovalStatus.Approved, token);

        Assert.Equal(("Posted", 4), await OtherPaymentStateAsync(draft.Id, token));
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var source = await db.Set<OtherPaymentSource>().AsNoTracking().SingleAsync(token);
        Assert.Equal((draft.Id, 3, 750.25m, setup.ExpenseLedgerId, "WF-OP-1"),
            (source.SourceId, source.SourceVersion, source.Amount, source.ExpenseAccountId, source.BankReference));
        Assert.StartsWith(OtherPaymentFingerprint.Prefix, source.Fingerprint, StringComparison.Ordinal);
        var lines = await db.Set<GlJournalLine>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(token);
        Assert.Equal([("6101", 750.25m, 0m), ("1102", 0m, 750.25m)],
            lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
        var detail = await OtherPaymentAsync(provider, s => s.GetAsync(draft.Id, token));
        Assert.Equal((OtherPaymentDocumentStatus.Posted, (long?)source.GlJournalId), (detail.Status, detail.JournalId));
    }

    [Fact]
    public async Task OtherPaymentRejectionReturnsTheDocumentForEditingOrResubmissionWithoutLedgerEffect()
    {
        // FI-OP-WF-003（駁回，BR-FI-064）：駁回 → Rejected（v3）、無 GL／來源列（原因留在 SYS 審批歷程）；
        // Rejected 可直接再送審（新輪次），也可先修改回 Draft 再送審。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = OtherPaymentWorkflowProvider(companyId, OtherPaymentWorkflowActor(), engine);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WF-OP-R"), token));
        await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        engine.Pending.Clear(); // SYS 完成該輪

        await DecideOtherPaymentAsync(provider, draft.Id, ApprovalStatus.Rejected, token);

        Assert.Equal(("Rejected", 3), await OtherPaymentStateAsync(draft.Id, token));
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));
        var resubmitted = await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 3, token));
        Assert.Equal((OtherPaymentDocumentStatus.Pending, 4, 2),
            (resubmitted.Status, resubmitted.Version, engine.Submissions.Count));

        engine.Pending.Clear();
        await DecideOtherPaymentAsync(provider, draft.Id, ApprovalStatus.Rejected, token);
        var edited = await OtherPaymentAsync(provider, s => s.ReplaceAsync(draft.Id, 5,
            OtherPaymentInput(setup, "WF-OP-R", 520m), token));
        Assert.Equal(OtherPaymentDocumentStatus.Draft, edited.Status);
        var again = await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, edited.Version, token));
        Assert.Equal((OtherPaymentDocumentStatus.Pending, 3, 520m),
            (again.Status, engine.Submissions.Count, engine.Submissions[^1].Amount));
    }

    [Fact]
    public async Task OtherPaymentZeroStepWorkflowPostsDuringSubmit()
    {
        // FI-OP-WF-004（零步驟）：SYS 立即核准 → 送審交易內過帳，回傳 Posted（v4）與過帳結果（來源版本 3）。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine { SubmitStatus = ApprovalStatus.Approved };
        await using var provider = OtherPaymentWorkflowProvider(companyId, OtherPaymentWorkflowActor(), engine);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WF-OP-0", 99m), token));

        var result = await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));

        Assert.Equal((OtherPaymentDocumentStatus.Posted, 4, ApprovalStatus.Approved),
            (result.Status, result.Version, result.Approval.Status));
        Assert.NotNull(result.Posting);
        Assert.Equal((draft.Id, 99m, 3, setup.CourierId), (result.Posting.PaymentId, result.Posting.Amount,
            result.Posting.SourceVersion, result.Posting.ExpenseCategoryId));
        Assert.Equal(("Posted", 4), await OtherPaymentStateAsync(draft.Id, token));
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));
    }

    [Fact]
    public async Task OtherPaymentPostingFailureOnFinalApprovalRollsBackTheWholeDecision()
    {
        // FI-OP-WF-005（錯誤／回滾，BR-FI-065）：類別無費用科目對照 → 過帳 422 MAPPING_MISSING，處理器拋出；審批交易回滾：
        // 單據仍 Pending v2、無 GL／來源列。零步驟時送審整筆回滾，單據仍 Draft v1。補對照後同一輪最終核准成功。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = OtherPaymentWorkflowProvider(companyId, OtherPaymentWorkflowActor(), engine);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token, withMap: false);
        var pending = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WF-OP-F"), token));
        await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(pending.Id, 1, token));

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            DecideOtherPaymentAsync(provider, pending.Id, ApprovalStatus.Approved, token));
        Assert.Equal(OtherPaymentPostingErrors.MappingMissing, failure.ErrorCode);
        Assert.Equal(("Pending", 2), await OtherPaymentStateAsync(pending.Id, token));
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));

        engine.SubmitStatus = ApprovalStatus.Approved;
        var zero = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WF-OP-F0"), token));
        var zeroFailure = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(zero.Id, 1, token)));
        Assert.Equal(OtherPaymentPostingErrors.MappingMissing, zeroFailure.ErrorCode);
        Assert.Equal(("Draft", 1), await OtherPaymentStateAsync(zero.Id, token));
        Assert.Equal((0, 0, 0, 0), await OtherPaymentPostingCountsAsync(companyId, token));

        await SaveOtherPaymentMapAsync(provider, setup.CourierId, setup.ExpenseLedgerId, token);
        await DecideOtherPaymentAsync(provider, pending.Id, ApprovalStatus.Approved, token);
        Assert.Equal(("Posted", 4), await OtherPaymentStateAsync(pending.Id, token));
    }

    [Fact]
    public async Task OtherPaymentWithdrawCancelsThePendingRoundAndReturnsToDraft()
    {
        // FI-OP-WF-006（撤回／狀態限制／版本）：Draft 撤回 422；舊版本 409（不呼叫 SYS）；Pending 撤回 → SYS 輪次取消、
        // 單據 Draft v3；撤回後可修改、可再送審。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = OtherPaymentWorkflowProvider(companyId, OtherPaymentWorkflowActor(), engine);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WF-OP-W"), token));

        var notPending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        Assert.Equal(OtherPaymentDocumentErrors.InvalidStatus, notPending.ErrorCode);
        await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        await Assert.ThrowsAsync<ConflictException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        Assert.Empty(engine.Cancels);

        var withdrawn = await OtherPaymentSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 2, token));
        Assert.Equal((OtherPaymentDocumentStatus.Draft, 3, ApprovalStatus.Cancelled, (OtherPaymentPostingReceipt?)null),
            (withdrawn.Status, withdrawn.Version, withdrawn.Approval.Status, withdrawn.Posting));
        Assert.Equal([draft.Id], engine.Cancels);
        Assert.Equal(("Draft", 3), await OtherPaymentStateAsync(draft.Id, token));
        var edited = await OtherPaymentAsync(provider, s => s.ReplaceAsync(draft.Id, 3,
            OtherPaymentInput(setup, "WF-OP-W2"), token));
        Assert.Equal(OtherPaymentDocumentStatus.Pending,
            (await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, edited.Version, token))).Status);
    }

    [Fact]
    public async Task OtherPaymentSubmitGuardsPermissionCompanyVersionStatusAndMasterData()
    {
        // FI-OP-WF-007（權限／公司／版本／狀態／關聯，BR-FI-065）：缺 SUBMIT 403（送審與撤回）、其他公司 404、舊版本 409、
        // SYS 回傳不屬於本單的狀態 409 且整筆回滾；送審時費用類別已停用 422（不送 SYS、不改單）；Pending 再送審 422；
        // 已刪除 404；Posted 不可送審或撤回 422。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        var actor = OtherPaymentWorkflowActor();
        await using var provider = OtherPaymentWorkflowProvider(companyId, actor, engine);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WF-OP-G"), token));

        actor.Permissions = AllOtherPaymentPermissions;
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.WithdrawAsync(draft.Id, 1, token)));
        actor.Permissions = AllOtherPaymentWorkflowPermissions;
        await using (var other = OtherPaymentWorkflowProvider(NewCompany(), OtherPaymentWorkflowActor(), engine))
            await Assert.ThrowsAsync<NotFoundException>(() =>
                OtherPaymentSubmitAsync(other, s => s.SubmitAsync(draft.Id, 1, token)));
        await Assert.ThrowsAsync<ConflictException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 7, token)));
        Assert.Empty(engine.Submissions);

        engine.ReturnForeignDocument = true;
        var invalid = await Assert.ThrowsAsync<ConflictException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        Assert.Equal(OtherPaymentWorkflowErrors.ApprovalResultInvalid, invalid.ErrorCode);
        Assert.Equal(("Draft", 1), await OtherPaymentStateAsync(draft.Id, token));
        engine.ReturnForeignDocument = false;
        engine.Pending.Clear();
        engine.Submissions.Clear();

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IncomeExpenseCategoryService>()
                .UpdateAsync(setup.CourierId, "快遞費", setup.ExpenseLedgerId, isActive: false, version: 1, token);
        var inactive = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token)));
        Assert.Equal(OtherPaymentDocumentErrors.ExpenseCategoryInactive, inactive.ErrorCode);
        Assert.Empty(engine.Submissions);
        Assert.Equal(("Draft", 1), await OtherPaymentStateAsync(draft.Id, token));

        var rent = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, "WF-OP-G2", categoryId: setup.RentId), token));
        await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(rent.Id, 1, token));
        var pending = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(rent.Id, 2, token)));
        Assert.Equal(OtherPaymentDocumentErrors.InvalidStatus, pending.ErrorCode);

        var deleted = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, "WF-OP-G3", categoryId: setup.RentId), token));
        await OtherPaymentAsync(provider, s => s.DeleteAsync(deleted.Id, 1, token));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(deleted.Id, 1, token)));

        var posted = await OtherPaymentAsync(provider, s => s.CreateAsync(
            OtherPaymentInput(setup, "WF-OP-G4", categoryId: setup.RentId), token));
        await SetOtherPaymentStatusAsync(posted.Id, "Posted");
        var postedSubmit = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(posted.Id, 2, token)));
        Assert.Equal(OtherPaymentDocumentErrors.InvalidStatus, postedSubmit.ErrorCode);
        var postedWithdraw = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OtherPaymentSubmitAsync(provider, s => s.WithdrawAsync(posted.Id, 2, token)));
        Assert.Equal(OtherPaymentDocumentErrors.InvalidStatus, postedWithdraw.ErrorCode);
    }

    [Fact]
    public async Task OtherPaymentResultHandlerOnlyActsOnAPendingDocumentInsideATransaction()
    {
        // FI-OP-WF-008（狀態限制）：處理器類型 AP.OTHER_PAYMENT；單據非 Pending（Draft、已 Posted 再次核准）409 且不改；
        // 不存在 404；無交易 InvalidOperation；撤回等非最終狀態 ArgumentOutOfRange。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var engine = new FakeApprovalEngine();
        await using var provider = OtherPaymentWorkflowProvider(companyId, OtherPaymentWorkflowActor(), engine);
        var setup = await SeedOtherPaymentPostingAsync(provider, companyId, token);
        var draft = await OtherPaymentAsync(provider, s => s.CreateAsync(OtherPaymentInput(setup, "WF-OP-H"), token));

        var changed = await Assert.ThrowsAsync<ConflictException>(() =>
            DecideOtherPaymentAsync(provider, draft.Id, ApprovalStatus.Approved, token));
        Assert.Equal(OtherPaymentWorkflowErrors.ApprovalStateChanged, changed.ErrorCode);
        Assert.Equal(("Draft", 1), await OtherPaymentStateAsync(draft.Id, token));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            DecideOtherPaymentAsync(provider, 999_999, ApprovalStatus.Rejected, token));
        await OtherPaymentSubmitAsync(provider, s => s.SubmitAsync(draft.Id, 1, token));
        await DecideOtherPaymentAsync(provider, draft.Id, ApprovalStatus.Approved, token);
        await Assert.ThrowsAsync<ConflictException>(() =>
            DecideOtherPaymentAsync(provider, draft.Id, ApprovalStatus.Approved, token));
        Assert.Equal(("Posted", 4), await OtherPaymentStateAsync(draft.Id, token));
        Assert.Equal((1, 1, 2, 1), await OtherPaymentPostingCountsAsync(companyId, token));

        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IApprovalResultHandler>()
            .Single(x => x.DocumentType == "AP.OTHER_PAYMENT");
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new(501, "AP.OTHER_PAYMENT", draft.Id, ApprovalStatus.Approved, 9, null), token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync(
            new(501, "AP.OTHER_PAYMENT", draft.Id, ApprovalStatus.Cancelled, 9, null), token));
    }
}
