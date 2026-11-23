using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-SD-004a2b13: SD-DEL-003/005/009/010/011 — submission, withdrawal and SYS decisions on real SQL.</summary>
public sealed partial class SalesDeliveryCreateTransactionTests
{
    [Fact]
    public async Task SubmitReservesBalanceWithdrawReleasesItAndSysDecisionsFollowTheDelivery()
    {
        var (companyId, order) = await SeedAsync("SA", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id, lineB = order.Lines[1].Id;
        var (submitter, approver) = await SeedFlowAsync(companyId);
        await SeedDeliveryAsync(companyId, order.Id, "W1", SalesDeliveryStatus.Pending, lineA, 4m);
        var first = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m), (lineB, 1.5m)]);
        var second = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 0.000001m)]);
        var guard = new RecordingGuard();
        var inventory = new RecordingInventory();

        await using (var db = Context(companyId))
        {
            var submitted = await Submission(db, companyId, Live(db, submitter), guard).SubmitAsync(first.Id, 1, Ct);
            Assert.Equal((SalesDeliveryStatus.Pending, 2), (submitted.Delivery.Status, submitted.Delivery.Version));
            Assert.Equal(ApprovalStatus.Pending, submitted.Approval.Status);
            Assert.Equal(submitted.Approval.InstanceId, submitted.Delivery.ApprovalInstanceId);
            // MD references are rechecked at submission, once per distinct item/warehouse/location.
            Assert.Equal([(31L, Warehouse, (long?)null)], guard.Calls);
        }

        // Pending 4 + the submitted 1 exhaust line A: one micro-unit more is refused and nothing changes.
        await using (var db = Context(companyId))
        {
            var over = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Submission(db, companyId, Live(db, submitter)).SubmitAsync(second.Id, 1, Ct));
            Assert.Equal("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED", over.ErrorCode);
            Assert.Null(await Live(db, submitter).GetStateAsync("SAL.DELIVERY", second.Id, Ct));
            await Assert.ThrowsAsync<ConflictException>(() =>
                Submission(db, companyId, Live(db, submitter)).WithdrawAsync(first.Id, 1, Ct));
            var withdrawn = await Submission(db, companyId, Live(db, submitter)).WithdrawAsync(first.Id, 2, Ct);
            Assert.Equal((SalesDeliveryStatus.Draft, 3, ApprovalStatus.Cancelled),
                (withdrawn.Delivery.Status, withdrawn.Delivery.Version, withdrawn.Approval.Status));
            var released = await Submission(db, companyId, Live(db, submitter)).SubmitAsync(second.Id, 1, Ct);
            Assert.Equal(SalesDeliveryStatus.Pending, released.Delivery.Status);

            // Rejection releases the reservation; the Rejected delivery keeps its SYS link.
            await using var decideDb = Context(companyId);
            await Live(decideDb, approver).RejectAsync(released.Approval.InstanceId,
                new ApprovalDecision("Short stock", 1), Ct);
            var resubmitted = await Submission(db, companyId, Live(db, submitter)).SubmitAsync(first.Id, 3, Ct);
            Assert.Equal(SalesDeliveryStatus.Pending, resubmitted.Delivery.Status);

            // BR-SD-024: final approval posts the saved lines through INV in the SYS transaction.
            await using var approveDb = Context(companyId);
            await Live(approveDb, approver, inventory)
                .ApproveAsync(resubmitted.Approval.InstanceId, new ApprovalDecision(null, 1), Ct);
            Assert.Equal(ApprovalStatus.Approved,
                (await Live(db, submitter).GetStateAsync("SAL.DELIVERY", first.Id, Ct))!.Status);
        }

        // Saved lines go to INV as SALES_OUTBOUND of this delivery on its saved date, without any price or cost.
        var call = Assert.Single(inventory.Calls);
        Assert.Equal((first.Id, Day), (call.SourceId, call.Date));
        Assert.Equal([new SalesOutboundLine(1, 31, Warehouse, null, 1m), new SalesOutboundLine(2, 31, Warehouse, null, 1.5m)],
            call.Lines);
        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesDelivery>().Where(x => x.Id == first.Id || x.Id == second.Id)
            .OrderBy(x => x.Id).Select(x => new { x.Status, x.ApprovalInstanceId, x.PostedAt }).ToListAsync(Ct);
        Assert.Equal([SalesDeliveryStatus.Posted, SalesDeliveryStatus.Rejected], saved.Select(x => x.Status));
        Assert.Equal([PostedAt, null], saved.Select(x => x.PostedAt));
        Assert.All(saved, x => Assert.NotNull(x.ApprovalInstanceId));
    }

    [Fact]
    public async Task ZeroStepApprovalPostsAndFailedChecksRollBackSubmissionWithoutSysInstance()
    {
        var (companyId, order) = await SeedAsync("SB", SalesOrderStatus.Approved);
        var (otherCompanyId, foreign) = await SeedAsync("SC", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id, lineB = order.Lines[1].Id;
        var draft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m)]);
        var zeroStep = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 2m)]);
        var tracked = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineB, 1m)]);
        var foreignDraft = await SeedSavedAsync(otherCompanyId, foreign.Id, SalesDeliveryStatus.Draft,
            [(foreign.Lines[0].Id, 1m)]);
        var submitOnly = new Dictionary<string, DataScope> { ["SAL.DELIVERY.SUBMIT"] = DataScope.Company };
        var self = new Dictionary<string, DataScope> { ["SAL.DELIVERY.SUBMIT"] = DataScope.Self };
        var editOnly = new Dictionary<string, DataScope> { ["SAL.DELIVERY.EDIT"] = DataScope.Company };
        await using var db = Context(companyId);
        var live = Live(db, 7);

        // No active flow: SYS approves immediately and SD posts through INV itself (BR-SD-024).
        // An INV refusal or an INV replay rolls back SYS and SD; the delivery stays Draft.
        var refusing = new RecordingInventory
        {
            Failure = new BusinessRuleException("INV.STOCK.INSUFFICIENT", "Insufficient stock.")
        };
        Assert.Equal("INV.STOCK.INSUFFICIENT", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, live, permissions: submitOnly, inventory: refusing)
                .SubmitAsync(zeroStep.Id, 1, Ct))).ErrorCode);
        Assert.Equal("SAL.DELIVERY.POSTING_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            Submission(db, companyId, live, inventory: new RecordingInventory { Replay = true })
                .SubmitAsync(zeroStep.Id, 1, Ct))).ErrorCode);
        Assert.Null(await live.GetStateAsync("SAL.DELIVERY", zeroStep.Id, Ct));
        await using (var check = Context(companyId))
            Assert.Equal((SalesDeliveryStatus.Draft, 1, (long?)null, (DateTime?)null), await check.Set<SalesDelivery>()
                .Where(x => x.Id == zeroStep.Id)
                .Select(x => ValueTuple.Create(x.Status, x.Version, x.ApprovalInstanceId, x.PostedAt)).SingleAsync(Ct));

        var inventory = new RecordingInventory();
        var posted = await Submission(db, companyId, live, permissions: submitOnly, inventory: inventory)
            .SubmitAsync(zeroStep.Id, 1, Ct);
        Assert.Equal((SalesDeliveryStatus.Posted, 2, ApprovalStatus.Approved, PostedAt),
            (posted.Delivery.Status, posted.Delivery.Version, posted.Approval.Status, posted.Delivery.PostedAt!.Value));
        Assert.Equal(posted.Approval.InstanceId, posted.Delivery.ApprovalInstanceId);
        Assert.Equal([new SalesOutboundLine(1, 31, Warehouse, null, 2m)], Assert.Single(inventory.Calls).Lines);
        // Posted is final: it can be neither resubmitted nor withdrawn.
        foreach (var attempt in new Func<Task>[]
                 {
                     () => Submission(db, companyId, live).SubmitAsync(zeroStep.Id, 2, Ct),
                     () => Submission(db, companyId, live).WithdrawAsync(zeroStep.Id, 2, Ct)
                 })
            Assert.Equal("SAL.DELIVERY.INVALID_STATUS",
                (await Assert.ThrowsAsync<BusinessRuleException>(attempt)).ErrorCode);
        Assert.Single(inventory.Calls);

        var trackedError = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, new FakeWorkflow(), new RecordingGuard { TrackedItem = 31 })
                .SubmitAsync(tracked.Id, 1, Ct));
        // ADR-145: a saved tracked line without selections totalling its quantity cannot be submitted.
        Assert.Equal(("SAL.DELIVERY.TRACKING_REQUIRED", (object?)1), (trackedError.ErrorCode, trackedError.Extensions!["lineNo"]));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Submission(db, companyId, new FakeWorkflow(), permissions: editOnly).SubmitAsync(draft.Id, 1, Ct));
        // Headers have no owner: the source order (owner 7) decides; hidden and foreign documents are 404.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submission(db, companyId, new FakeWorkflow(), permissions: self, userId: 8).SubmitAsync(draft.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submission(db, companyId, new FakeWorkflow()).SubmitAsync(foreignDraft.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submission(db, companyId, new FakeWorkflow()).WithdrawAsync(long.MaxValue, 1, Ct));
        await Assert.ThrowsAsync<ConflictException>(() =>
            Submission(db, companyId, new FakeWorkflow()).SubmitAsync(draft.Id, 2, Ct));
        Assert.Equal("SAL.DELIVERY.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, new FakeWorkflow()).WithdrawAsync(draft.Id, 1, Ct))).ErrorCode);

        var pending = await Submission(db, companyId, new FakeWorkflow(), permissions: self)
            .SubmitAsync(draft.Id, 1, Ct);
        Assert.Equal("SAL.DELIVERY.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, new FakeWorkflow()).SubmitAsync(draft.Id, pending.Delivery.Version, Ct))).ErrorCode);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [order.Id], Ct);
        Assert.Equal("SAL.DELIVERY.ORDER_NOT_APPROVED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, new FakeWorkflow()).SubmitAsync(tracked.Id, 1, Ct))).ErrorCode);
        // A cancelled order no longer blocks releasing the reservation.
        var withdrawn = await Submission(db, companyId, new FakeWorkflow())
            .WithdrawAsync(draft.Id, pending.Delivery.Version, Ct);
        Assert.Equal(SalesDeliveryStatus.Draft, withdrawn.Delivery.Status);

        await using var verify = Context(companyId);
        Assert.Equal(1, (await verify.Set<SalesDelivery>().SingleAsync(x => x.Id == tracked.Id, Ct)).Version);
        await using var other = Context(otherCompanyId);
        Assert.Equal((SalesDeliveryStatus.Draft, 1), await other.Set<SalesDelivery>()
            .Where(x => x.Id == foreignDraft.Id).Select(x => ValueTuple.Create(x.Status, x.Version)).SingleAsync(Ct));
    }

    [Fact]
    public async Task LastBalanceRaceHasOneWinnerAndOtherOrdersAndCompaniesAreNotBlocked()
    {
        var (companyId, order) = await SeedAsync("SD", SalesOrderStatus.Approved);
        var sibling = await SeedOrderAsync(companyId, SalesOrderStatus.Approved);
        var (otherCompanyId, foreign) = await SeedAsync("SE", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id;
        await SeedDeliveryAsync(companyId, order.Id, "R1", SalesDeliveryStatus.Pending, lineA, 4m);
        var winner = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m)]);
        var loser = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 1m)]);
        var siblingDraft = await SeedSavedAsync(companyId, sibling.Id, SalesDeliveryStatus.Draft,
            [(sibling.Lines[0].Id, 5m)]);
        var foreignDraft = await SeedSavedAsync(otherCompanyId, foreign.Id, SalesDeliveryStatus.Draft,
            [(foreign.Lines[0].Id, 5m)]);

        var outcomes = new List<string>();
        Task<string>? contender = null;
        var guard = new RecordingGuard
        {
            // Runs after the winner saved Pending and called SYS, while its transaction is still open.
            AfterSave = async () =>
            {
                outcomes.Add(await TrySubmitAsync(otherCompanyId, foreignDraft.Id, lockTimeout: true));
                outcomes.Add(await TrySubmitAsync(companyId, siblingDraft.Id, lockTimeout: true));
                contender = Task.Run(() => TrySubmitAsync(companyId, loser.Id, lockTimeout: false));
                await Task.Delay(1500, Ct);
                outcomes.Add(contender.IsCompleted ? "not waiting" : "waiting");
            }
        };
        await using (var first = Context(companyId))
            await Submission(first, companyId, new FakeWorkflow(), guard).SubmitAsync(winner.Id, 1, Ct);
        outcomes.Add(await contender!);

        // The contender waited on the source-order lock, then saw the committed reservation.
        Assert.Equal(["done", "done", "waiting", "SAL.DELIVERY.ORDER_BALANCE_EXCEEDED"], outcomes);
        await using var verify = Context(companyId);
        var states = await verify.Set<SalesDelivery>()
            .Where(x => x.Id == winner.Id || x.Id == loser.Id || x.Id == siblingDraft.Id)
            .OrderBy(x => x.Id).Select(x => x.Status).ToListAsync(Ct);
        Assert.Equal([SalesDeliveryStatus.Pending, SalesDeliveryStatus.Draft, SalesDeliveryStatus.Pending], states);
        await using var other = Context(otherCompanyId);
        Assert.Equal(SalesDeliveryStatus.Pending,
            (await other.Set<SalesDelivery>().SingleAsync(x => x.Id == foreignDraft.Id, Ct)).Status);
    }

    [Fact]
    public async Task FailedWinnerRollsBackSoTheWaitingContenderTakesTheBalance()
    {
        var (companyId, order) = await SeedAsync("SF", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id;
        var failing = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 5m)]);
        var contenderDraft = await SeedSavedAsync(companyId, order.Id, SalesDeliveryStatus.Draft, [(lineA, 5m)]);
        Task<string>? contender = null;
        var guard = new RecordingGuard
        {
            AfterSave = async () =>
            {
                contender = Task.Run(() => TrySubmitAsync(companyId, contenderDraft.Id, lockTimeout: false));
                await Task.Delay(1000, Ct);
                throw new InvalidOperationException("Simulated failure after the Pending save.");
            }
        };
        await using (var first = Context(companyId))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Submission(first, companyId, new FakeWorkflow(), guard).SubmitAsync(failing.Id, 1, Ct));

        Assert.Equal("done", await contender!);
        await using var verify = Context(companyId);
        var states = await verify.Set<SalesDelivery>().Where(x => x.Id == failing.Id || x.Id == contenderDraft.Id)
            .OrderBy(x => x.Id).Select(x => new { x.Status, x.Version, x.ApprovalInstanceId }).ToListAsync(Ct);
        Assert.Equal((SalesDeliveryStatus.Draft, 1, (long?)null), (states[0].Status, states[0].Version, states[0].ApprovalInstanceId));
        Assert.Equal(SalesDeliveryStatus.Pending, states[1].Status);
    }

    private async Task<string> TrySubmitAsync(long companyId, long deliveryId, bool lockTimeout)
    {
        await using var db = Context(companyId);
        await db.Database.OpenConnectionAsync(Ct);
        if (lockTimeout)
            await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000", Ct);
        try
        {
            await Submission(db, companyId, new FakeWorkflow()).SubmitAsync(deliveryId, 1, Ct);
            return "done";
        }
        catch (Exception error) when (IsLockTimeout(error))
        {
            return "blocked";
        }
        catch (BusinessRuleException error)
        {
            return error.ErrorCode;
        }
    }

    private async Task<(long Submitter, long Approver)> SeedFlowAsync(long companyId)
    {
        SystemUser submitter = NewUser(), approver = NewUser();
        await using (var setup = Context(null))
        {
            setup.AddRange(submitter, approver);
            await setup.SaveChangesAsync(Ct);
            setup.AddRange(new SystemUserCompany { CompanyId = companyId, UserId = submitter.Id },
                new SystemUserCompany { CompanyId = companyId, UserId = approver.Id });
            await setup.SaveChangesAsync(Ct);
        }
        await using var db = Context(companyId);
        await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("SAL.DELIVERY", "Delivery", false,
            [new ApprovalStepInput("Approver", ApproverType.User, null, approver.Id, null)]), Ct);
        return (submitter.Id, approver.Id);
    }

    private static SystemUser NewUser()
    {
        var name = "sdl" + Guid.NewGuid().ToString("N")[..16];
        return new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
    }

    private static ApprovalWorkflowService Live(MyErpDbContext db, long userId,
        IExternalInventoryPostingService? inventory = null) =>
        new(db, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(userId), new AuditTrail(),
            [new SalesDeliveryApprovalResultHandler(db, () => Posting(db, inventory))]);

    private static SalesDeliverySubmissionService Submission(MyErpDbContext db, long companyId,
        IApprovalWorkflowService workflow, IInventoryReferenceGuard? guard = null,
        IReadOnlyDictionary<string, DataScope>? permissions = null, long userId = 7,
        IExternalInventoryPostingService? inventory = null)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, permissions is null,
                permissions ?? new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesDeliverySubmissionService(db, new SalesActionAccess(company, auth.Object), company,
            Lines(db, guard), Posting(db, inventory), workflow);
    }

    private static SalesDeliveryPosting Posting(MyErpDbContext db, IExternalInventoryPostingService? inventory) =>
        new(db, new SalesDeliverySelectionStore(db),
            new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27))),
            inventory ?? new RecordingInventory(), new FixedClock(PostedAt));

    /// <summary>Real INV posting through its public contract, as the composed API would build it.</summary>
    private static ExternalInventoryPostingService RealInventory(MyErpDbContext db) =>
        new(db, new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
            new FixedClock(PostedAt)));

    /// <summary>Records SD's INV calls; can refuse like INV or report a replay of an earlier posting.</summary>
    private sealed class RecordingInventory : IExternalInventoryPostingService
    {
        private static long next = 5_000_000;
        public Exception? Failure { get; set; }
        public bool Replay { get; set; }
        public List<(long SourceId, DateOnly Date, SalesOutboundLine[] Lines)> Calls { get; } = [];

        public Task<InventoryPostingResult> PostPurchaseInboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<PurchaseInboundLine> lines, CancellationToken token = default) =>
            throw new NotSupportedException();

        public Task<InventoryPostingResult> PostSalesOutboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<SalesOutboundLine> lines, CancellationToken token = default)
        {
            Calls.Add((sourceId, businessDate, [.. lines]));
            if (Failure is { } failure) throw failure;
            return Task.FromResult(new InventoryPostingResult(Replay,
                [.. lines.Select(_ => Interlocked.Increment(ref next))]));
        }

        public Task<SalesReturnInboundResult> PostSalesReturnInboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<SalesReturnInboundLine> lines, CancellationToken token = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Isolates SD locking from SYS tables in the race tests; always returns a Pending round.</summary>
    private sealed class FakeWorkflow : IApprovalWorkflowService
    {
        private static long next = 1_000_000;

        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(Interlocked.Increment(ref next), submission.DocumentType,
                submission.DocumentId, ApprovalStatus.Pending, 1, 1));

        public Task<ApprovalState> CancelAsync(string documentType, long documentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(Interlocked.Increment(ref next), documentType, documentId,
                ApprovalStatus.Cancelled, null, 1));

        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId,
            CancellationToken cancellationToken = default) => Task.FromResult<ApprovalState?>(null);
    }
}
