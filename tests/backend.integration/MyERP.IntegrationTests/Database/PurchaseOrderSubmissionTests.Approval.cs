using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-006b2（REQ_F4114.1.4／1.5、BR-PUR-024，PUR-PO-004／015／022）：採購單 SYS 最終決定以真實 SQL 與真實 SYS
/// <see cref="ApprovalWorkflowService"/> 驗收——處理器經 <see cref="PurchasingModule"/> 註冊解析；多級中間核准不重消耗、
/// 最終核准保留消耗；駁回同交易聚合釋回一次並清旗標；重複／過時決定 409 不二次釋回；釋回途中故障整筆回滾後只釋回一次。
/// </summary>
public sealed partial class PurchaseOrderSubmissionTests
{
    [Fact]
    public async Task IntermediateApprovalDoesNotReconsumeAndFinalApprovalKeepsConsumption()
    {
        var s = await SeedCompanyAsync(withFlow: true, steps: 2);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 1m);
        var order = await SeedOrderAsync(s, (11, source, 2m), (11, source, 3m), (12, null, 4m));
        await SubmitPendingAsync(s, order);

        var instance = await DecideAsync(s, order.Id, approve: true, comment: null);
        Assert.Equal(ApprovalStatus.Pending, instance.Status);
        await AssertOrderAsync(s, order.Id, "PendingApproval", consumptionActive: true, (source, 6m));

        instance = await DecideAsync(s, order.Id, approve: true, comment: "OK");
        Assert.Equal(ApprovalStatus.Approved, instance.Status);
        await AssertOrderAsync(s, order.Id, "Approved", consumptionActive: true, (source, 6m));

        // A repeated decision on the completed instance cannot touch the order or the requisition again.
        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Workflow(db, s).RejectAsync(instance.Id, new ApprovalDecision("Too late", instance.Version), Ct));
            Assert.Equal("SYS.APPROVAL.NOT_PENDING", error.ErrorCode);
        }
        await AssertOrderAsync(s, order.Id, "Approved", consumptionActive: true, (source, 6m));
    }

    [Fact]
    public async Task RejectReleasesAggregatedQuantityOnceAndResubmitUsesRemainingAtThatTime()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 1m);
        var (_, other) = await SeedRequisitionAsync(s, quantity: 5m, ordered: 0m, itemId: 12);
        var order = await SeedOrderAsync(s, (11, source, 2m), (12, other, 5m), (11, source, 3m), (13, null, 1m));
        await SubmitPendingAsync(s, order);
        await AssertOrderAsync(s, order.Id, "PendingApproval", consumptionActive: true, (source, 6m), (other, 5m));

        var instance = await DecideAsync(s, order.Id, approve: false, comment: "Wrong supplier");
        Assert.Equal(ApprovalStatus.Rejected, instance.Status);
        Assert.Equal("Wrong supplier", Assert.Single(instance.Steps).Comment);
        await AssertOrderAsync(s, order.Id, "Rejected", consumptionActive: false, (source, 1m), (other, 0m));

        // Another order takes most of the released quantity before the rejected order is revised and resubmitted.
        var competitor = await SeedOrderAsync(s, (11, source, 7m));
        await SubmitPendingAsync(s, competitor);
        await using (var db = NewDb(s))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET status = 'Draft' WHERE id = {order.Id}", Ct);
            var current = await db.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == order.Id, Ct);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(order.Id, current.Version, Ct));
            Assert.Equal("PUR.ORDER.REQUISITION_INSUFFICIENT", error.ErrorCode);
            var shortage = Assert.Single((PurchaseOrderRequisitionShortage[])error.Extensions!["shortages"]!);
            Assert.Equal(source, shortage.RequisitionLineId);
            Assert.Equal(5m, shortage.Requested);
            Assert.Equal(2m, shortage.Available);
        }
        await AssertOrderAsync(s, order.Id, "Draft", consumptionActive: false, (source, 8m), (other, 0m));
    }

    [Fact]
    public async Task HandlerRefusesStaleOrMismatchedStateWithConflictAndReleasesNothing()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m);
        var order = await SeedOrderAsync(s, (11, source, 4m));
        await SubmitPendingAsync(s, order);

        foreach (var (status, active) in new[] { ("Rejected", false), ("Approved", true), ("PendingApproval", false), ("Draft", false) })
        {
            await using var db = NewDb(s);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET status = {status}, requisition_consumption_active = {active} WHERE id = {order.Id}", Ct);
            foreach (var decision in new[] { ApprovalStatus.Approved, ApprovalStatus.Rejected })
            {
                var error = await Assert.ThrowsAsync<ConflictException>(() =>
                    Handler(db).HandleAsync(Completed(order.Id, decision), Ct));
                Assert.Equal(PurchaseOrderApprovalResultHandler.StateChanged, error.ErrorCode);
            }
            Assert.Equal(4m, await OrderedAsync(db, source));
        }

        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Handler(db).HandleAsync(Completed(long.MaxValue, ApprovalStatus.Rejected), Ct));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                Handler(db).HandleAsync(Completed(order.Id, ApprovalStatus.Pending), Ct));
        }

        // Consumption already gone (data drift): the SYS rejection rolls back as a whole instead of going below zero.
        await using (var db = NewDb(s))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET status = 'PendingApproval', requisition_consumption_active = 1 WHERE id = {order.Id}; UPDATE pur_requisition_line SET ordered_quantity = 3 WHERE id = {source}", Ct);
        await using (var db = NewDb(s))
        {
            var instance = await Instances(db, order.Id).SingleAsync(Ct);
            var error = await Assert.ThrowsAsync<ConflictException>(() =>
                Workflow(db, s).RejectAsync(instance.Id, new ApprovalDecision("No", instance.Version), Ct));
            Assert.Equal(PurchaseOrderApprovalResultHandler.ReleaseInvalid, error.ErrorCode);
        }
        await AssertOrderAsync(s, order.Id, "PendingApproval", consumptionActive: true, (source, 3m));
        await using (var check = NewDb(s))
            Assert.Equal(ApprovalStatus.Pending, (await Instances(check, order.Id).SingleAsync(Ct)).Status);
    }

    [Theory]
    [InlineData("UPDATE [pur_requisition_line]")]
    [InlineData("UPDATE [pur_purchase_order]")]
    [InlineData("UPDATE [sys_approval_instance]")]
    public async Task FailureDuringRejectionRollsBackApprovalOrderAndRequisitionThenRetryReleasesOnce(string statement)
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 2m);
        var order = await SeedOrderAsync(s, (11, source, 1m), (11, source, 2m));
        await SubmitPendingAsync(s, order);

        var fault = new FailCommand(statement);
        await using (var failing = NewDb(s, fault))
        {
            var instance = await Instances(failing, order.Id).SingleAsync(Ct);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                Workflow(failing, s).RejectAsync(instance.Id, new ApprovalDecision("Rework", instance.Version), Ct));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        Assert.True(fault.Fired);
        await AssertOrderAsync(s, order.Id, "PendingApproval", consumptionActive: true, (source, 5m));
        await using (var check = NewDb(s))
            Assert.Equal(ApprovalStatus.Pending, (await Instances(check, order.Id).SingleAsync(Ct)).Status);

        var rejected = await DecideAsync(s, order.Id, approve: false, comment: "Rework");
        Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
        await AssertOrderAsync(s, order.Id, "Rejected", consumptionActive: false, (source, 2m));
    }

    // ------------------------------------------------------------------ approval helpers

    private async Task SubmitPendingAsync(Seeded s, PurchaseOrderSource order)
    {
        await using var db = NewDb(s);
        var result = await Service(db, s).SubmitAsync(order.Id, order.Version, Ct);
        Assert.Equal("PendingApproval", result.Status);
    }

    private async Task<ApprovalInstanceDetail> DecideAsync(Seeded s, long orderId, bool approve, string? comment)
    {
        await using var db = NewDb(s);
        var instance = await Instances(db, orderId).SingleAsync(Ct);
        var decision = new ApprovalDecision(comment, instance.Version);
        return approve
            ? await Workflow(db, s).ApproveAsync(instance.Id, decision, Ct)
            : await Workflow(db, s).RejectAsync(instance.Id, decision, Ct);
    }

    private async Task AssertOrderAsync(Seeded s, long orderId, string status, bool consumptionActive,
        params (long Id, decimal Ordered)[] sources)
    {
        await using var check = NewDb(s);
        var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == orderId, Ct);
        Assert.Equal(status, saved.Status);
        Assert.Equal(consumptionActive, saved.RequisitionConsumptionActive);
        foreach (var (id, ordered) in sources) Assert.Equal(ordered, await OrderedAsync(check, id));
    }

    private static ApprovalCompleted Completed(long orderId, ApprovalStatus status) =>
        new(1, PurchaseOrderSubmissionService.DocumentType, orderId, status, 1, null, null);

    /// <summary>Resolve the decision handlers exactly as the API does, through the PUR module registration.</summary>
    private static IApprovalResultHandler[] Handlers(MyErpDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        new PurchasingModule().AddServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetServices<IApprovalResultHandler>().ToArray();
    }

    private static IApprovalResultHandler Handler(MyErpDbContext db) =>
        Assert.Single(Handlers(db), x => x.DocumentType == PurchaseOrderSubmissionService.DocumentType);

    private static ApprovalWorkflowService Workflow(MyErpDbContext db, Seeded s) =>
        new(db, new FixedClock(Today), new FixedUser(s.ApproverId), new AuditTrail(), Handlers(db));
}
