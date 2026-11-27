using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-006c1（REQ_F4114.1.6、BR-PUR-025，PUR-PO-008／014／017）：採購單 DELETE 以真實 SQL 驗收——只軟刪未消耗的
/// Draft／Rejected（駁回已釋回），請購已轉量、保存行與單號原樣保留且單號不可重用；待審／已核准／已取消 422、舊版本 409、
/// 範圍外／他公司／已刪 404、缺 DELETE 403，皆零變更；UPDATE 故障整筆回滾後原版本可重試。
/// </summary>
public sealed partial class PurchaseOrderSubmissionTests
{
    [Fact]
    public async Task DeleteSoftDeletesDraftKeepsNumberLinesAndRequisitionAndNumberIsNotReused()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 4m);
        var order = await SeedOrderAsync(s, (11, source, 2m), (12, null, 3m));

        await using (var db = NewDb(s))
            await Lifecycle(db, s).DeleteAsync(order.Id, order.Version, Ct);

        await using (var check = NewDb(s))
        {
            Assert.False(await check.Set<PurchaseOrderSource>().AnyAsync(x => x.Id == order.Id, Ct));
            var saved = await check.Set<PurchaseOrderSource>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == order.Id, Ct);
            Assert.True(saved.IsDeleted);
            Assert.Equal(Today, saved.DeletedAt);
            Assert.Equal("Draft", saved.Status);
            Assert.Equal(order.DocumentNo, saved.DocumentNo);
            Assert.Equal(order.BaseGross, saved.BaseGross);
            Assert.False(saved.RequisitionConsumptionActive);
            Assert.Equal(2, await check.Set<PurchaseOrderSourceLine>().CountAsync(x => x.PurchaseOrderId == order.Id, Ct));
            Assert.Equal(4m, await OrderedAsync(check, source));
            Assert.Empty(await Instances(check, order.Id).ToListAsync(Ct));
        }

        // Repeating the delete (any version) cannot see the deleted order, and its number stays reserved forever.
        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => Lifecycle(db, s).DeleteAsync(order.Id, order.Version, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => Lifecycle(db, s).DeleteAsync(order.Id, order.Version + 1, Ct));
        }
        await using (var db = NewDb(s))
        {
            db.Add(new PurchaseOrderSource
            {
                CompanyId = s.CompanyId, SupplierId = 101, DocumentNo = order.DocumentNo, BusinessDate = Day,
                Currency = "TWD", Status = "Draft", OwnerId = s.UserId
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task DeleteRejectedOrderAfterReleaseDoesNotReleaseAgain()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 1m);
        var order = await SeedOrderAsync(s, (11, source, 2m), (11, source, 3m));
        await SubmitPendingAsync(s, order);
        await DecideAsync(s, order.Id, approve: false, comment: "Re-quote");
        await AssertOrderAsync(s, order.Id, "Rejected", consumptionActive: false, (source, 1m));

        int version;
        await using (var db = NewDb(s))
            version = await db.Set<PurchaseOrderSource>().Where(x => x.Id == order.Id).Select(x => x.Version).SingleAsync(Ct);
        await using (var db = NewDb(s))
            await Lifecycle(db, s).DeleteAsync(order.Id, version, Ct);

        await using var check = NewDb(s);
        var saved = await check.Set<PurchaseOrderSource>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == order.Id, Ct);
        Assert.True(saved.IsDeleted);
        Assert.Equal("Rejected", saved.Status);
        Assert.Equal(1m, await OrderedAsync(check, source));
        // SYS keeps the rejected decision history of the deleted order.
        Assert.Single(await Instances(check, order.Id).ToListAsync(Ct));
    }

    [Theory]
    [InlineData("PendingApproval", true)]
    [InlineData("Approved", true)]
    [InlineData("Approved", false)]
    [InlineData("Cancelled", false)]
    public async Task DeleteRejectsSubmittedApprovedOrCancelledOrdersWithoutChanges(string status, bool consumed)
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 4m);
        var order = await SeedOrderAsync(s, (11, source, 2m));
        int version;
        await using (var db = NewDb(s))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET status = {status}, requisition_consumption_active = {consumed} WHERE id = {order.Id}", Ct);
            version = await db.Set<PurchaseOrderSource>().Where(x => x.Id == order.Id).Select(x => x.Version).SingleAsync(Ct);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Lifecycle(db, s).DeleteAsync(order.Id, version, Ct));
            Assert.Equal("PUR.ORDER.NOT_DELETABLE", error.ErrorCode);
        }
        await using var check = NewDb(s);
        var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == order.Id, Ct);
        Assert.False(saved.IsDeleted);
        Assert.Equal(status, saved.Status);
        Assert.Equal(version, saved.Version);
        Assert.Equal(consumed, saved.RequisitionConsumptionActive);
        Assert.Equal(4m, await OrderedAsync(check, source));
    }

    [Fact]
    public async Task DeleteGuardsVersionPermissionScopeAndCompanyWithoutChanges()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var order = await SeedOrderAsync(s, (11, null, 2m));
        // Same owner, but saved under the other company the user also belongs to.
        var foreign = await SeedOrderAsync(s with { CompanyId = s.OtherCompanyId }, (11, null, 1m));

        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<RequestValidationException>(() => Lifecycle(db, s).DeleteAsync(0, 1, Ct));
            await Assert.ThrowsAsync<ConflictException>(() => Lifecycle(db, s).DeleteAsync(order.Id, order.Version + 1, Ct));
            await Assert.ThrowsAsync<ConflictException>(() => Lifecycle(db, s).DeleteAsync(order.Id, 0, Ct));
            var denied = await Assert.ThrowsAsync<ForbiddenException>(() =>
                Lifecycle(db, s, scope: null).DeleteAsync(order.Id, order.Version, Ct));
            Assert.Equal("COMMON.PERMISSION.DENIED", denied.ErrorCode);
            // Own scope for another user, a different branch, and another company's order are all invisible.
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Lifecycle(db, s, userId: s.ApproverId, scope: DataScope.Self).DeleteAsync(order.Id, order.Version, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Lifecycle(db, s, scope: DataScope.Branch, branchId: 99).DeleteAsync(order.Id, order.Version, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => Lifecycle(db, s).DeleteAsync(foreign.Id, foreign.Version, Ct));
        }
        await using (var check = NewDb(s))
        {
            var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == order.Id, Ct);
            Assert.False(saved.IsDeleted);
            Assert.Equal(order.Version, saved.Version);
            Assert.False(await check.Set<PurchaseOrderSource>().IgnoreQueryFilters()
                .AnyAsync(x => x.Id == foreign.Id && x.IsDeleted, Ct));
        }

        // The owner with Own scope and a matching branch scope may delete.
        await using (var db = NewDb(s))
            await Lifecycle(db, s, scope: DataScope.Self).DeleteAsync(order.Id, order.Version, Ct);
        await using var after = NewDb(s);
        Assert.True(await after.Set<PurchaseOrderSource>().IgnoreQueryFilters()
            .AnyAsync(x => x.Id == order.Id && x.IsDeleted, Ct));
    }

    [Fact]
    public async Task DeleteUpdateFailureRollsBackAndSameVersionCanRetry()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var order = await SeedOrderAsync(s, (11, null, 2m));
        var fault = new FailCommand("UPDATE [pur_purchase_order]");
        await using (var db = NewDb(s, fault))
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => Lifecycle(db, s).DeleteAsync(order.Id, order.Version, Ct));
            Assert.IsType<InvalidOperationException>(error as InvalidOperationException ?? error.InnerException);
        }
        Assert.True(fault.Fired);
        await using (var check = NewDb(s))
        {
            var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == order.Id, Ct);
            Assert.False(saved.IsDeleted);
            Assert.Null(saved.DeletedAt);
            Assert.Equal(order.Version, saved.Version);
        }
        await using (var db = NewDb(s))
            await Lifecycle(db, s).DeleteAsync(order.Id, order.Version, Ct);
        await using var after = NewDb(s);
        Assert.True(await after.Set<PurchaseOrderSource>().IgnoreQueryFilters()
            .AnyAsync(x => x.Id == order.Id && x.IsDeleted, Ct));
    }

    private static PurchaseOrderLifecycleService Lifecycle(MyErpDbContext db, Seeded s, long? userId = null,
        DataScope? scope = DataScope.Company, long branchId = 10)
    {
        var grants = new Dictionary<string, DataScope>();
        if (scope is { } granted) grants[PurchasingPermissions.OrderDelete] = granted;
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId ?? s.UserId, s.CompanyId, branchId, 20, false, grants));
        return new PurchaseOrderLifecycleService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, new FixedClock(Today));
    }
}
