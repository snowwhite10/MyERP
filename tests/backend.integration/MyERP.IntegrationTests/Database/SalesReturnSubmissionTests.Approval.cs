using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-APPROVE-001～004 (WU-SD-008a5b, ADR-191, BR-SD-043/044/047/049): the SYS final decision on a Pending sales
/// return through the real <see cref="ApprovalWorkflowService"/> with the production SD result handlers (resolved from
/// the module registration), real FI credit posting and SD commitments; INV is the recording double.
/// </summary>
public sealed partial class SalesReturnSubmissionTests
{
    [Fact]
    public async Task FinalApprovalPostsInboundCreditAndReleasesCommitmentInTheSysTransaction()
    {
        // SD-RETURN-APPROVE-001: SD-RETURN-010/013/014/017/018 — the last approver's decision posts the Pending return:
        // INV inbound of the saved lines, FI credit of billed portions, unbilled commitment released, Posted with the
        // same SYS link; SYS is Approved; a second decision on the completed instance changes nothing.
        var s = await SeedAsync(withFlow: true);
        var draft = await CreateDraftAsync(s, "A-1",
            new SalesReturnLineInput(1, s.DeliveryA, Warehouse, null, 0.5m, [new(s.InvoiceLineA, 1m)]),
            new SalesReturnLineInput(2, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1.5m)]));
        var commitmentBefore = await CommitmentAsync(s);
        var openBefore = await OpenArAsync(s);
        var pending = await SubmitAsync(s, draft.Id, 1);
        Assert.Empty(s.Inventory.Calls);

        await DecideAsync(s, pending.Approval.InstanceId, approve: true);

        var posted = await LoadAsync(s, draft.Id);
        Assert.Equal((SalesReturnStatus.Posted, Now, pending.Approval.InstanceId),
            (posted.Status, posted.PostedAt, posted.ApprovalInstanceId));
        var call = Assert.Single(s.Inventory.Calls);
        Assert.Equal((draft.Id, Day.AddDays(1)), (call.SourceId, call.Date));
        Assert.Equal(RecordingInventory.Issued(call),
            posted.Lines.OrderBy(x => x.LineNo).Select(x => x.InvMovementId!.Value));
        var billedBase = posted.Lines.SelectMany(x => x.InvoiceAllocations).Sum(x => x.BaseGross);
        Assert.NotNull(posted.FiCreditNoteId);
        Assert.Equal(openBefore - billedBase, await OpenArAsync(s));
        Assert.Equal(commitmentBefore - (posted.BaseGross - billedBase), await CommitmentAsync(s));
        Assert.Equal(ApprovalStatus.Approved, (await StateAsync(s, draft.Id))!.Status);

        var completed = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            DecideAsync(s, pending.Approval.InstanceId, approve: true));
        Assert.Equal(ApprovalWorkflowService.NotPending, completed.ErrorCode);
        Assert.Single(s.Inventory.Calls);
    }

    [Fact]
    public async Task RejectionKeepsTheLinesAndARevisedDraftOpensANewSysRound()
    {
        // SD-RETURN-APPROVE-002: SD-RETURN-010/015 — rejection: Pending → Rejected, no INV/FI/commitment change, lines and
        // SYS link kept; a Rejected return cannot be submitted or deleted, but an edit revises it to Draft (version +2:
        // status then lines); SUBMIT opens a new SYS round whose approval posts.
        var s = await SeedAsync(withFlow: true);
        var line = new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1m)]);
        var draft = await CreateDraftAsync(s, "B-1", line);
        var fi = await FiCountsAsync(s);
        var commitment = await CommitmentAsync(s);
        var first = await SubmitAsync(s, draft.Id, 1);

        await DecideAsync(s, first.Approval.InstanceId, approve: false);

        var rejected = await LoadAsync(s, draft.Id);
        Assert.Equal((SalesReturnStatus.Rejected, 3, first.Approval.InstanceId),
            (rejected.Status, rejected.Version, rejected.ApprovalInstanceId));
        Assert.Equal(1m, Assert.Single(rejected.Lines).Quantity);
        Assert.Empty(s.Inventory.Calls);
        Assert.Equal(fi, await FiCountsAsync(s));
        Assert.Equal(commitment, await CommitmentAsync(s));
        Assert.Equal(ApprovalStatus.Rejected, (await StateAsync(s, draft.Id))!.Status);

        var resubmit = await Assert.ThrowsAsync<BusinessRuleException>(() => SubmitAsync(s, draft.Id, 3));
        Assert.Equal(SalesReturnDraftCommandService.InvalidStatus, resubmit.ErrorCode);
        var delete = await Assert.ThrowsAsync<BusinessRuleException>(() => DeleteAsync(s, draft.Id, 3));
        Assert.Equal(SalesReturnDraftCommandService.InvalidStatus, delete.ErrorCode);
        await Assert.ThrowsAsync<ConflictException>(() => UpdateAsync(s, draft.Id, 2, "B-1", line));
        var key = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateAsync(s, draft.Id, 3, "B-X", line));
        Assert.Equal(SalesReturnDraftCommandService.RequestKeyImmutable, key.ErrorCode);
        Assert.Equal((SalesReturnStatus.Rejected, 3), await StatusAsync(s, draft.Id));

        var revised = await UpdateAsync(s, draft.Id, 3, "B-1",
            new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 0.5m)]));
        Assert.Equal((SalesReturnStatus.Draft, 5, first.Approval.InstanceId),
            (revised.Status, revised.Version, revised.ApprovalInstanceId));
        Assert.Equal(0.5m, Assert.Single(revised.Lines).Quantity);

        var second = await SubmitAsync(s, draft.Id, 5);
        Assert.NotEqual(first.Approval.InstanceId, second.Approval.InstanceId);
        Assert.Equal((SalesReturnStatus.Pending, second.Approval.InstanceId),
            (second.Return.Status, second.Return.ApprovalInstanceId));
        // The decision for the old round is refused by SYS; the new round's approval posts.
        await Assert.ThrowsAsync<BusinessRuleException>(() => DecideAsync(s, first.Approval.InstanceId, approve: true));
        await DecideAsync(s, second.Approval.InstanceId, approve: true);
        Assert.Equal(SalesReturnStatus.Posted, (await LoadAsync(s, draft.Id)).Status);
        Assert.Equal(0.5m, Assert.Single(Assert.Single(s.Inventory.Calls).Lines).Quantity);
    }

    [Fact]
    public async Task ApprovalOfAStaleOrRefusedReturnRollsBackTheSysDecision()
    {
        // SD-RETURN-APPROVE-003: SD-RETURN-011/012/022, BR-SD-047/049 — a Pending return reserves nothing, so approval
        // rechecks under the locks: once another Pending return of the same unbilled remainder posts, approving this one
        // is 409 SNAPSHOT_STALE and SYS stays Pending at its step (the approver rejects; the user revises). An INV
        // refusal also rolls back SYS, INV, FI and SD; the same decision succeeds once INV accepts.
        var s = await SeedAsync(withFlow: true);
        var unbilled = new SalesReturnLineInput(1, s.DeliveryA, Warehouse, null, 0.5m, null);
        var first = await SubmitAsync(s, (await CreateDraftAsync(s, "C-1", unbilled)).Id, 1);
        var second = await SubmitAsync(s, (await CreateDraftAsync(s, "C-2", unbilled)).Id, 1);
        await DecideAsync(s, first.Approval.InstanceId, approve: true);
        var fi = await FiCountsAsync(s);
        var commitment = await CommitmentAsync(s);

        var stale = await Assert.ThrowsAsync<ConflictException>(() =>
            DecideAsync(s, second.Approval.InstanceId, approve: true));
        Assert.Equal(SalesReturnPosting.SnapshotStale, stale.ErrorCode);
        await AssertStillPendingAsync(s, second.Return.Id, second.Approval.InstanceId);
        Assert.Single(s.Inventory.Calls);
        Assert.Equal((fi, commitment), (await FiCountsAsync(s), await CommitmentAsync(s)));

        // A billed return refused by INV: nothing moves and the decision can be repeated.
        var billed = await SubmitAsync(s, (await CreateDraftAsync(s, "C-3",
            new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1m)]))).Id, 1);
        var open = await OpenArAsync(s);
        s.Inventory.Failure = new BusinessRuleException("INV.SALES_RETURN.ORIGINAL_INVALID", "Original issue invalid.");
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            DecideAsync(s, billed.Approval.InstanceId, approve: true));
        Assert.Equal("INV.SALES_RETURN.ORIGINAL_INVALID", refused.ErrorCode);
        await AssertStillPendingAsync(s, billed.Return.Id, billed.Approval.InstanceId);
        Assert.Equal((fi, open), (await FiCountsAsync(s), await OpenArAsync(s)));

        s.Inventory.Failure = null;
        s.TamperReceipt = true;
        var mismatch = await Assert.ThrowsAsync<SalesReturnPostingException>(() =>
            DecideAsync(s, billed.Approval.InstanceId, approve: true));
        Assert.Equal(SalesReturnPosting.ReceiptMismatch, mismatch.ErrorCode);
        await AssertStillPendingAsync(s, billed.Return.Id, billed.Approval.InstanceId);
        Assert.Equal((fi, open), (await FiCountsAsync(s), await OpenArAsync(s)));

        s.TamperReceipt = false;
        await DecideAsync(s, billed.Approval.InstanceId, approve: true);
        Assert.Equal(SalesReturnStatus.Posted, (await LoadAsync(s, billed.Return.Id)).Status);
        Assert.True(await OpenArAsync(s) < open);

        // The stale one is rejected by the approver and revised to the exact remainder, then posts.
        await DecideAsync(s, second.Approval.InstanceId, approve: false);
        var revised = await UpdateAsync(s, second.Return.Id, 3, "C-2", unbilled);
        var round = await SubmitAsync(s, second.Return.Id, revised.Version);
        await DecideAsync(s, round.Approval.InstanceId, approve: true);
        Assert.Equal(SalesReturnStatus.Posted, (await LoadAsync(s, second.Return.Id)).Status);
        Assert.Equal(0m, await CommitmentAsync(s));
    }

    [Fact]
    public async Task HandlerRefusesADecisionForAReturnNoLongerWaitingForThatInstance()
    {
        // SD-RETURN-APPROVE-004: a SYS result whose instance is not the return's current Pending link (a stale or
        // foreign instance) is 409 APPROVAL_STATE_CHANGED for approve and reject; an unknown return is 404; non-final
        // results are refused; nothing changes. The module registers exactly one SAL.RETURN handler.
        var s = await SeedAsync(withFlow: true);
        var draft = await CreateDraftAsync(s, "D-1",
            new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1m)]));
        var pending = await SubmitAsync(s, draft.Id, 1);

        await using (var scope = s.Provider.CreateAsyncScope())
        {
            var handler = Assert.Single(scope.ServiceProvider.GetServices<IApprovalResultHandler>(),
                x => x.DocumentType == SalesReturnCreateService.DocumentType);
            Assert.IsType<SalesReturnApprovalResultHandler>(handler);
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
                Completed(pending.Approval.InstanceId, draft.Id, ApprovalStatus.Rejected), Ct));
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            foreach (var status in new[] { ApprovalStatus.Approved, ApprovalStatus.Rejected })
            {
                var changed = await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(
                    Completed(pending.Approval.InstanceId + 1, draft.Id, status), Ct));
                Assert.Equal(SalesReturnApprovalResultHandler.ApprovalStateChanged, changed.ErrorCode);
                var missing = await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(
                    Completed(pending.Approval.InstanceId, long.MaxValue, status), Ct));
                Assert.Equal(SalesReturnDraftCommandService.NotFoundCode, missing.ErrorCode);
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync(
                Completed(pending.Approval.InstanceId, draft.Id, ApprovalStatus.Pending), Ct));
            await transaction.RollbackAsync(Ct);
        }

        Assert.Empty(s.Inventory.Calls);
        await AssertStillPendingAsync(s, draft.Id, pending.Approval.InstanceId);
    }

    // ---------------------------------------------------------------- approval helpers

    private static ApprovalCompleted Completed(long instanceId, long returnId, ApprovalStatus status) =>
        new(instanceId, SalesReturnCreateService.DocumentType, returnId, status, 1, status == ApprovalStatus.Rejected
            ? "No" : null);

    /// <summary>The approver's decision through the real SYS workflow with the module's registered result handlers.</summary>
    private static async Task DecideAsync(Seeded s, long instanceId, bool approve)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var workflow = new ApprovalWorkflowService(sp.GetRequiredService<MyErpDbContext>(),
            sp.GetRequiredService<IClock>(), new FixedUser(s.ApproverId), new AuditTrail(),
            sp.GetServices<IApprovalResultHandler>());
        if (approve)
            await workflow.ApproveAsync(instanceId, new ApprovalDecision(null, 1), Ct);
        else
            await workflow.RejectAsync(instanceId, new ApprovalDecision("Wrong quantity", 1), Ct);
    }

    private static async Task<ApprovalState?> StateAsync(Seeded s, long returnId)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApprovalWorkflowService>()
            .GetStateAsync(SalesReturnCreateService.DocumentType, returnId, Ct);
    }

    private static async Task<SalesReturn> UpdateAsync(Seeded s, long id, int version, string key,
        params SalesReturnLineInput[] lines)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SalesReturnDraftCommandService>()
            .UpdateAsync(id, version, new SalesReturnDraftInput(key, Day.AddDays(1), null, lines), Ct);
    }

    private static async Task DeleteAsync(Seeded s, long id, int version)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SalesReturnDraftCommandService>().DeleteAsync(id, version, Ct);
    }

    private static async Task<SalesReturn> LoadAsync(Seeded s, long id)
    {
        await using var db = s.Raw();
        return await db.Set<SalesReturn>().AsNoTracking().AsSplitQuery()
            .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations).SingleAsync(x => x.Id == id, Ct);
    }

    private static async Task<(SalesReturnStatus, int)> StatusAsync(Seeded s, long id)
    {
        var row = await LoadAsync(s, id);
        return (row.Status, row.Version);
    }

    private static async Task AssertStillPendingAsync(Seeded s, long id, long instanceId)
    {
        var row = await LoadAsync(s, id);
        Assert.Equal((SalesReturnStatus.Pending, 2, (long?)instanceId, (long?)null),
            (row.Status, row.Version, row.ApprovalInstanceId, row.FiCreditNoteId));
        Assert.All(row.Lines, x => Assert.Null(x.InvMovementId));
        Assert.Equal(ApprovalStatus.Pending, (await StateAsync(s, id))!.Status);
    }
}
