using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-006c2（REQ_F4114.1.6、BR-PUR-025，PUR-PO-008／012～014／016／017）：採購單 CANCEL 以真實 SQL 與真實 SYS
/// 審批引擎驗收——只取消尚無收貨的已核准訂單，同交易聚合釋回一次請購量、清旗標、保存原因；SYS 歷史、單號與保存行保留。
/// 重複／併發取消不二次釋回；Draft／PendingApproval／Rejected／有收貨 422；請購行或訂單 UPDATE 故障及釋回不一致整筆回滾後
/// 可原版本重試且只釋回一次；原因、版本、權限、範圍、他公司各自守門且零變更。
/// </summary>
public sealed partial class PurchaseOrderSubmissionTests
{
    [Fact]
    public async Task CancelApprovedOrderReleasesAggregatedQuantityOnceAndKeepsHistory()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 12m, ordered: 1m);
        var order = await SeedOrderAsync(s, (11, source, 2m), (11, source, 3m), (12, null, 7m));
        var version = await ApproveZeroStepAsync(s, order);
        await AssertOrderAsync(s, order.Id, "Approved", consumptionActive: true, (source, 6m));

        PurchaseOrderCancelResult result;
        await using (var db = NewDb(s))
            result = await Canceller(db, s).CancelAsync(order.Id, new(version, "  Supplier closed  "), Ct);
        Assert.Equal(order.Id, result.Id);
        Assert.Equal(order.DocumentNo, result.DocumentNo);
        Assert.Equal("Cancelled", result.Status);
        Assert.True(result.Version > version);

        await using (var check = NewDb(s))
        {
            var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == order.Id, Ct);
            Assert.Equal("Cancelled", saved.Status);
            Assert.False(saved.RequisitionConsumptionActive);
            Assert.Equal("Supplier closed", saved.CancelReason);
            Assert.False(saved.IsDeleted);
            Assert.Equal(result.Version, saved.Version);
            Assert.Equal(order.DocumentNo, saved.DocumentNo);
            Assert.Equal(order.BaseGross, saved.BaseGross);
            Assert.Equal(3, await check.Set<PurchaseOrderSourceLine>().CountAsync(x => x.PurchaseOrderId == order.Id, Ct));
            Assert.Equal(1m, await OrderedAsync(check, source));
            // SYS keeps the original approval; cancelling is a PUR action, not a new SYS decision.
            Assert.Equal(ApprovalStatus.Approved, Assert.Single(await Instances(check, order.Id).ToListAsync(Ct)).Status);
        }

        // A repeated cancel, with either the old or the new version, never releases a second time.
        await using (var db = NewDb(s))
        {
            var again = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Canceller(db, s).CancelAsync(order.Id, new(result.Version, "Again"), Ct));
            Assert.Equal("PUR.ORDER.NOT_CANCELLABLE", again.ErrorCode);
            await Assert.ThrowsAsync<ConflictException>(() =>
                Canceller(db, s).CancelAsync(order.Id, new(version, "Again"), Ct));
        }
        await AssertOrderAsync(s, order.Id, "Cancelled", consumptionActive: false, (source, 1m));
    }

    [Fact]
    public async Task CancelRejectsDraftPendingAndRejectedOrdersWithoutChanges()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 20m, ordered: 0m);
        var draft = await SeedOrderAsync(s, (11, source, 1m));
        var pending = await SeedOrderAsync(s, (11, source, 2m));
        var rejected = await SeedOrderAsync(s, (11, source, 4m));
        await SubmitPendingAsync(s, pending);
        await SubmitPendingAsync(s, rejected);
        await DecideAsync(s, rejected.Id, approve: false, comment: "No budget");
        await AssertOrderAsync(s, pending.Id, "PendingApproval", consumptionActive: true, (source, 2m));

        foreach (var (order, status, consumed) in new[]
                 { (draft, "Draft", false), (pending, "PendingApproval", true), (rejected, "Rejected", false) })
        {
            await using (var db = NewDb(s))
            {
                var version = await VersionAsync(db, order.Id);
                var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                    Canceller(db, s).CancelAsync(order.Id, new(version, "Not needed"), Ct));
                Assert.Equal("PUR.ORDER.NOT_CANCELLABLE", error.ErrorCode);
            }
            await AssertOrderAsync(s, order.Id, status, consumed, (source, 2m));
            await using var check = NewDb(s);
            Assert.Null(await check.Set<PurchaseOrderSource>().Where(x => x.Id == order.Id)
                .Select(x => x.CancelReason).SingleAsync(Ct));
        }
    }

    [Fact]
    public async Task CancelRejectsOrderWithLiveReceiptButAllowsAfterReceiptIsSoftDeleted()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m);
        var order = await SeedOrderAsync(s, (11, source, 4m));
        var version = await ApproveZeroStepAsync(s, order);
        await using (var db = NewDb(s))
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.pur_purchase_receipt(company_id,purchase_order_id,supplier_id,currency,status,is_deleted,created_at,created_by,version)
                VALUES ({s.CompanyId},{order.Id},101,'TWD','Draft',0,{Today},{s.UserId},1)
                """, Ct);

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Canceller(db, s).CancelAsync(order.Id, new(version, "Late"), Ct));
            Assert.Equal("PUR.ORDER.HAS_RECEIPTS", error.ErrorCode);
        }
        await AssertOrderAsync(s, order.Id, "Approved", consumptionActive: true, (source, 4m));

        // A discarded (soft-deleted) draft receipt no longer references the order.
        await using (var db = NewDb(s))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt SET is_deleted = 1, deleted_at = {Today} WHERE purchase_order_id = {order.Id}", Ct);
        await using (var db = NewDb(s))
            await Canceller(db, s).CancelAsync(order.Id, new(version, "Late"), Ct);
        await AssertOrderAsync(s, order.Id, "Cancelled", consumptionActive: false, (source, 0m));
    }

    [Fact]
    public async Task CancelGuardsReasonVersionPermissionScopeAndCompanyWithoutChanges()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m);
        var order = await SeedOrderAsync(s, (11, source, 3m));
        var version = await ApproveZeroStepAsync(s, order);
        var foreign = await SeedOrderAsync(s with { CompanyId = s.OtherCompanyId }, (11, null, 1m));

        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<RequestValidationException>(() =>
                Canceller(db, s).CancelAsync(0, new(version, "x"), Ct));
            foreach (var reason in new[] { null, "", "   ", new string('r', PurchaseOrderLifecycleService.MaxReasonLength + 1) })
                await Assert.ThrowsAsync<RequestValidationException>(() =>
                    Canceller(db, s).CancelAsync(order.Id, new(version, reason), Ct));
            await Assert.ThrowsAsync<ConflictException>(() =>
                Canceller(db, s).CancelAsync(order.Id, new(version + 1, "x"), Ct));
            await Assert.ThrowsAsync<ConflictException>(() => Canceller(db, s).CancelAsync(order.Id, new(0, "x"), Ct));
            // DELETE does not imply CANCEL.
            var denied = await Assert.ThrowsAsync<ForbiddenException>(() =>
                Lifecycle(db, s).CancelAsync(order.Id, new(version, "x"), Ct));
            Assert.Equal("COMMON.PERMISSION.DENIED", denied.ErrorCode);
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Canceller(db, s, userId: s.ApproverId, scope: DataScope.Self).CancelAsync(order.Id, new(version, "x"), Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Canceller(db, s, scope: DataScope.Branch, branchId: 99).CancelAsync(order.Id, new(version, "x"), Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Canceller(db, s).CancelAsync(foreign.Id, new(foreign.Version, "x"), Ct));
        }
        await AssertOrderAsync(s, order.Id, "Approved", consumptionActive: true, (source, 3m));
        await using (var check = NewDb(s))
            Assert.Equal(version, await VersionAsync(check, order.Id));

        // Exactly 500 characters is accepted; the owner with Own scope may cancel.
        var longest = new string('r', PurchaseOrderLifecycleService.MaxReasonLength);
        await using (var db = NewDb(s))
            await Canceller(db, s, scope: DataScope.Self).CancelAsync(order.Id, new(version, longest), Ct);
        await using var after = NewDb(s);
        Assert.Equal(longest, await after.Set<PurchaseOrderSource>().Where(x => x.Id == order.Id)
            .Select(x => x.CancelReason).SingleAsync(Ct));
        Assert.Equal(0m, await OrderedAsync(after, source));
    }

    [Fact]
    public async Task DeletedApprovedOrderCannotBeCancelledAndCancelReasonRequiresCancelledStatus()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var order = await SeedOrderAsync(s, (11, null, 1m));
        await using (var db = NewDb(s))
        {
            // ck_pur_order_cancel_reason: only a Cancelled order may carry a reason.
            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_order SET cancel_reason = 'x' WHERE id = {order.Id}", Ct));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_order SET status = 'Approved', is_deleted = 1, deleted_at = {Today} WHERE id = {order.Id}", Ct);
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Canceller(db, s).CancelAsync(order.Id, new(order.Version, "x"), Ct));
        }
    }

    [Fact]
    public async Task CompetingCancelsOnTwoConnectionsReleaseOnlyOnce()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 2m);
        var order = await SeedOrderAsync(s, (11, source, 5m));
        var version = await ApproveZeroStepAsync(s, order);

        await using var first = NewDb(s);
        await using var second = NewDb(s);
        var outcomes = await Task.WhenAll(
            CaptureCancel(() => Canceller(first, s).CancelAsync(order.Id, new(version, "A"), Ct)),
            CaptureCancel(() => Canceller(second, s).CancelAsync(order.Id, new(version, "B"), Ct)));

        Assert.Single(outcomes, x => x is null);
        Assert.IsType<ConflictException>(Assert.Single(outcomes, x => x is not null));
        await AssertOrderAsync(s, order.Id, "Cancelled", consumptionActive: false, (source, 2m));
    }

    [Theory]
    [InlineData("UPDATE [pur_requisition_line]")]
    [InlineData("UPDATE [pur_purchase_order]")]
    public async Task CancelUpdateFailureRollsBackOrderAndRequisitionThenRetryReleasesOnce(string statement)
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, a) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 1m);
        var (_, b) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m, itemId: 12);
        var order = await SeedOrderAsync(s, (12, b, 2m), (11, a, 3m));
        var version = await ApproveZeroStepAsync(s, order);
        var fault = new FailCommand(statement);
        await using (var failing = NewDb(s, fault))
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                Canceller(failing, s).CancelAsync(order.Id, new(version, "Fault"), Ct));
            Assert.IsType<InvalidOperationException>(error as InvalidOperationException ?? error.InnerException);
        }
        Assert.True(fault.Fired);
        await AssertOrderAsync(s, order.Id, "Approved", consumptionActive: true, (a, 4m), (b, 2m));

        await using (var retry = NewDb(s))
            await Canceller(retry, s).CancelAsync(order.Id, new(version, "Fault"), Ct);
        await AssertOrderAsync(s, order.Id, "Cancelled", consumptionActive: false, (a, 1m), (b, 0m));
    }

    [Fact]
    public async Task InconsistentRequisitionQuantityRefusesCancelWithConflictAndNoPartialRelease()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, a) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m);
        var (_, b) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m, itemId: 12);
        var order = await SeedOrderAsync(s, (11, a, 3m), (12, b, 5m));
        var version = await ApproveZeroStepAsync(s, order);
        // Simulated drift: line b now records less than this order consumed.
        await using (var db = NewDb(s))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_requisition_line SET ordered_quantity = 4 WHERE id = {b}", Ct);

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() =>
                Canceller(db, s).CancelAsync(order.Id, new(version, "Drift"), Ct));
            Assert.Equal(PurchaseOrderApprovalResultHandler.ReleaseInvalid, error.ErrorCode);
        }
        await AssertOrderAsync(s, order.Id, "Approved", consumptionActive: true, (a, 3m), (b, 4m));
    }

    private async Task<int> ApproveZeroStepAsync(Seeded s, PurchaseOrderSource order)
    {
        await using var db = NewDb(s);
        var result = await Service(db, s).SubmitAsync(order.Id, order.Version, Ct);
        Assert.Equal("Approved", result.Status);
        return result.Version;
    }

    private static Task<int> VersionAsync(MyERP.Shared.Infrastructure.Persistence.MyErpDbContext db, long orderId) =>
        db.Set<PurchaseOrderSource>().Where(x => x.Id == orderId).Select(x => x.Version).SingleAsync(Ct);

    private static async Task<Exception?> CaptureCancel(Func<Task<PurchaseOrderCancelResult>> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static PurchaseOrderLifecycleService Canceller(MyERP.Shared.Infrastructure.Persistence.MyErpDbContext db,
        Seeded s, long? userId = null, DataScope? scope = DataScope.Company, long branchId = 10) =>
        Lifecycle(db, s, userId, scope, branchId, PurchasingPermissions.OrderCancel);
}
