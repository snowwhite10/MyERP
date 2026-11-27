using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-006b1（REQ_F4114.1.4、BR-PUR-023，PUR-PO-003／007／009～011／014／015／018）：採購單 SUBMIT 以真實 SQL
/// 與真實 SYS 審批引擎驗收——聚合同單來源量、原子消耗請購餘量、零步驟立即核准只消耗一次、整單零變更失敗、
/// 雙連線競爭餘量最多一單成功、請購行更新或 SYS 實例寫入故障全回滾。
/// </summary>
public sealed partial class PurchaseOrderSubmissionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Today = new(2028, 2, 29, 9, 30, 0);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Today);

    [Fact]
    public async Task ZeroStepSubmitAggregatesLinesConsumesOnceAndTwoOrdersAddUp()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 12m, ordered: 1m);
        // Two lines of one order reference the same requisition line (2 + 3) plus a manual line without source.
        var first = await SeedOrderAsync(s, (11, source, 2m), (11, source, 3m), (12, null, 7m));
        var second = await SeedOrderAsync(s, (11, source, 4m));

        await using (var db = NewDb(s))
        {
            var result = await Service(db, s).SubmitAsync(first.Id, first.Version, Ct);
            Assert.Equal("Approved", result.Status);
            Assert.Equal(ApprovalStatus.Approved, result.Approval.Status);
            Assert.Equal(0, result.Approval.StepCount);
            Assert.Equal(first.DocumentNo, (await StateDocumentNoAsync(db, first.Id)));
        }
        await using (var db = NewDb(s))
            await Service(db, s).SubmitAsync(second.Id, second.Version, Ct);

        await using var check = NewDb(s);
        Assert.Equal(10m, await OrderedAsync(check, source));
        var orders = await check.Set<PurchaseOrderSource>().AsNoTracking()
            .Where(x => x.Id == first.Id || x.Id == second.Id).ToListAsync(Ct);
        Assert.All(orders, x => Assert.Equal("Approved", x.Status));
        Assert.All(orders, x => Assert.True(x.RequisitionConsumptionActive));
        var instances = await Instances(check, first.Id).ToListAsync(Ct);
        var instance = Assert.Single(instances);
        Assert.Equal(ApprovalStatus.Approved, instance.Status);
        Assert.Equal(first.BaseGross, instance.Amount);
        Assert.Equal("PUR.ORDER", instance.DocumentType);
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}", s.CompanyId).SingleAsync(Ct));
    }

    [Fact]
    public async Task PendingFlowKeepsOrderPendingWithOneConsumptionAndResubmitIsRejected()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 4m);
        var order = await SeedOrderAsync(s, (11, source, 3m), (11, source, 3m));

        PurchaseOrderSubmissionResult result;
        await using (var db = NewDb(s))
            result = await Service(db, s).SubmitAsync(order.Id, order.Version, Ct);
        Assert.Equal("PendingApproval", result.Status);
        Assert.Equal(ApprovalStatus.Pending, result.Approval.Status);
        Assert.Equal(1, result.Approval.StepCount);

        // Exactly the remaining quantity (10 − 4 = 6) succeeds; a second SUBMIT can neither consume nor open SYS again.
        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(order.Id, result.Version, Ct));
            Assert.Equal("PUR.ORDER.NOT_SUBMITTABLE", error.ErrorCode);
            await Assert.ThrowsAsync<ConflictException>(() => Service(db, s).SubmitAsync(order.Id, order.Version, Ct));
        }
        await using var check = NewDb(s);
        Assert.Equal(10m, await OrderedAsync(check, source));
        var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == order.Id, Ct);
        Assert.Equal("PendingApproval", saved.Status);
        Assert.Equal(result.Version, saved.Version);
        Assert.True(saved.RequisitionConsumptionActive);
        Assert.Equal(ApprovalStatus.Pending, Assert.Single(await Instances(check, order.Id).ToListAsync(Ct)).Status);
    }

    [Fact]
    public async Task OneMinimumUnitOverRemainingRejectsWholeOrderWithShortageAndNoChanges()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, enough) = await SeedRequisitionAsync(s, quantity: 5m, ordered: 0m, itemId: 12);
        var (_, tight) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 4m);
        var order = await SeedOrderAsync(s, (12, enough, 5m), (11, tight, 3m), (11, tight, 3.000001m));

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(order.Id, order.Version, Ct));
            Assert.Equal("PUR.ORDER.REQUISITION_INSUFFICIENT", error.ErrorCode);
            var shortage = Assert.Single((PurchaseOrderRequisitionShortage[])error.Extensions!["shortages"]!);
            Assert.Equal(tight, shortage.RequisitionLineId);
            Assert.Equal([2, 3], shortage.OrderLineNos);
            Assert.Equal(6.000001m, shortage.Requested);
            Assert.Equal(6m, shortage.Available);
        }
        await AssertUntouchedAsync(s, order, (enough, 0m), (tight, 4m));
    }

    [Theory]
    [InlineData("Draft", false, 11)]
    [InlineData("Closed", false, 11)]
    [InlineData("Draft", true, 11)] // ck_pur_requisition_deleted: only drafts can be soft-deleted
    [InlineData("Rejected", false, 11)]
    [InlineData("Approved", false, 13)]
    public async Task InvalidRequisitionSourceRejectsWholeOrderWithLineNumbers(string status, bool deleted, long orderItem)
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, valid) = await SeedRequisitionAsync(s, quantity: 5m, ordered: 0m, itemId: 12);
        var (requisitionId, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m);
        var order = await SeedOrderAsync(s, (12, valid, 1m), (orderItem, source, 1m));
        await using (var db = NewDb(s))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_requisition SET status = {status}, is_deleted = {deleted}, deleted_at = {(deleted ? Today : (DateTime?)null)} WHERE id = {requisitionId}", Ct);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(order.Id, order.Version, Ct));
            Assert.Equal("PUR.ORDER.INVALID_REQUISITION_SOURCE", error.ErrorCode);
            Assert.Equal([2], (int[])error.Extensions!["lineNos"]!);
        }
        await AssertUntouchedAsync(s, order, (valid, 0m), (source, 0m));
    }

    [Fact]
    public async Task VersionStatusScopePermissionAndCompanyGuardsLeaveDraftUntouched()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 0m);
        var order = await SeedOrderAsync(s, (11, source, 1m));
        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<ConflictException>(() => Service(db, s).SubmitAsync(order.Id, order.Version + 1, Ct));
            await Assert.ThrowsAsync<ConflictException>(() => Service(db, s).SubmitAsync(order.Id, 0, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Service(db, s, userId: 999, scope: DataScope.Self).SubmitAsync(order.Id, order.Version, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Service(db, s, scope: DataScope.Branch, branchId: 77).SubmitAsync(order.Id, order.Version, Ct));
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Service(db, s, scope: null).SubmitAsync(order.Id, order.Version, Ct));
        }
        await using (var other = NewDb(s with { CompanyId = s.OtherCompanyId }))
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Service(other, s with { CompanyId = s.OtherCompanyId }).SubmitAsync(order.Id, order.Version, Ct));
        await AssertUntouchedAsync(s, order, (source, 0m));

        foreach (var status in new[] { "Rejected", "Cancelled" })
        {
            await using var db = NewDb(s);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET status = {status} WHERE id = {order.Id}", Ct);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(order.Id, order.Version, Ct));
            Assert.Equal("PUR.ORDER.NOT_SUBMITTABLE", error.ErrorCode);
            Assert.Equal(0m, await OrderedAsync(db, source));
            Assert.Empty(await Instances(db, order.Id).ToListAsync(Ct));
        }
        await using (var db = NewDb(s))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET status = 'Draft', is_deleted = 1, deleted_at = {Today} WHERE id = {order.Id}", Ct);
            await Assert.ThrowsAsync<NotFoundException>(() => Service(db, s).SubmitAsync(order.Id, order.Version, Ct));
        }
    }

    [Fact]
    public async Task CompetingSubmitsOnTwoConnectionsCannotOverConsumeRequisition()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 2m);
        var a = await SeedOrderAsync(s, (11, source, 5m));
        var b = await SeedOrderAsync(s, (11, source, 5m));

        await using var first = NewDb(s);
        await using var second = NewDb(s);
        var outcomes = await Task.WhenAll(
            Capture(() => Service(first, s).SubmitAsync(a.Id, a.Version, Ct)),
            Capture(() => Service(second, s).SubmitAsync(b.Id, b.Version, Ct)));

        var winner = Assert.Single(outcomes, x => x.Result is not null);
        var loser = Assert.Single(outcomes, x => x.Error is not null);
        Assert.True(loser.Error is ConflictException
            || loser.Error is BusinessRuleException { ErrorCode: "PUR.ORDER.REQUISITION_INSUFFICIENT" }, loser.Error?.ToString());
        await using var check = NewDb(s);
        Assert.Equal(7m, await OrderedAsync(check, source));
        var loserId = winner.Result!.Id == a.Id ? b.Id : a.Id;
        var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == loserId, Ct);
        Assert.Equal("Draft", saved.Status);
        Assert.False(saved.RequisitionConsumptionActive);
        Assert.Empty(await Instances(check, loserId).ToListAsync(Ct));
    }

    [Theory]
    [InlineData("UPDATE [pur_requisition_line]")]
    [InlineData("INSERT INTO [sys_approval_instance]")]
    public async Task FailureWhileConsumingOrCreatingApprovalRollsBackEverythingAndRetrySucceeds(string statement)
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (_, source) = await SeedRequisitionAsync(s, quantity: 10m, ordered: 1m);
        var order = await SeedOrderAsync(s, (11, source, 2m), (11, source, 1m));
        var fault = new FailCommand(statement);
        await using (var failing = NewDb(s, fault))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                Service(failing, s).SubmitAsync(order.Id, order.Version, Ct));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        Assert.True(fault.Fired);
        await AssertUntouchedAsync(s, order, (source, 1m));

        await using var retry = NewDb(s);
        var result = await Service(retry, s).SubmitAsync(order.Id, order.Version, Ct);
        Assert.Equal("PendingApproval", result.Status);
        Assert.Equal(4m, await OrderedAsync(retry, source));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Seeded(long CompanyId, long OtherCompanyId, long UserId, long ApproverId);

    private sealed record Outcome(PurchaseOrderSubmissionResult? Result, Exception? Error);

    private static async Task<Outcome> Capture(Func<Task<PurchaseOrderSubmissionResult>> action)
    {
        try { return new Outcome(await action(), null); }
        catch (Exception ex) { return new Outcome(null, ex); }
    }

    private static IQueryable<ApprovalInstance> Instances(MyErpDbContext db, long orderId) =>
        db.Set<ApprovalInstance>().AsNoTracking().Where(x => x.DocumentType == "PUR.ORDER" && x.DocumentId == orderId);

    private static Task<decimal> OrderedAsync(MyErpDbContext db, long sourceId) =>
        db.Set<RequisitionLine>().AsNoTracking().Where(x => x.Id == sourceId).Select(x => x.OrderedQuantity).SingleAsync(Ct);

    private static Task<string?> StateDocumentNoAsync(MyErpDbContext db, long orderId) =>
        Instances(db, orderId).Select(x => (string?)x.DocumentNo).SingleAsync(Ct);

    private async Task AssertUntouchedAsync(Seeded s, PurchaseOrderSource order, params (long Id, decimal Ordered)[] sources)
    {
        await using var check = NewDb(s);
        var saved = await check.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == order.Id, Ct);
        Assert.Equal("Draft", saved.Status);
        Assert.Equal(order.Version, saved.Version);
        Assert.False(saved.RequisitionConsumptionActive);
        foreach (var (id, ordered) in sources) Assert.Equal(ordered, await OrderedAsync(check, id));
        Assert.Empty(await Instances(check, order.Id).ToListAsync(Ct));
    }

    private async Task<Seeded> SeedCompanyAsync(bool withFlow, int steps = 1)
    {
        var name = "pos" + Guid.NewGuid().ToString("N")[..16];
        var user = new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
        var approverName = "poa" + Guid.NewGuid().ToString("N")[..16];
        var approver = new SystemUser { Username = approverName, NormalizedUsername = approverName.ToUpperInvariant(),
            PasswordHash = "x" };
        SystemCompany company, other;
        await using (var setup = NewDb((long?)null))
        {
            company = new SystemCompany { Code = "PS" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "PO submit test" };
            other = new SystemCompany { Code = "PX" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "PO submit other" };
            setup.AddRange(company, other, user, approver);
            await setup.SaveChangesAsync(Ct);
            setup.AddRange(new SystemUserCompany { CompanyId = company.Id, UserId = user.Id },
                new SystemUserCompany { CompanyId = company.Id, UserId = approver.Id },
                new SystemUserCompany { CompanyId = other.Id, UserId = user.Id });
            await setup.SaveChangesAsync(Ct);
        }
        var seeded = new Seeded(company.Id, other.Id, user.Id, approver.Id);
        if (withFlow)
        {
            await using var db = NewDb(seeded);
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("PUR.ORDER", "Purchase order", false,
                Enumerable.Range(1, steps).Select(i => new ApprovalStepInput($"Buyer manager {i}", ApproverType.User,
                    null, approver.Id, null)).ToList()), Ct);
        }
        return seeded;
    }

    private async Task<(long RequisitionId, long LineId)> SeedRequisitionAsync(Seeded s, decimal quantity,
        decimal ordered, long itemId = 11)
    {
        await using var db = NewDb(s);
        var requisition = new Requisition { CompanyId = s.CompanyId, DocumentNo = $"RQ-{Guid.NewGuid():N}"[..20],
            BusinessDate = Day.AddDays(-1), RequesterId = s.UserId, Status = RequisitionStatus.Approved };
        requisition.Lines.Add(new RequisitionLine { CompanyId = s.CompanyId, LineNo = 1, ItemId = itemId,
            RequiredDate = Day.AddDays(10), Quantity = quantity, OrderedQuantity = ordered });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        return (requisition.Id, requisition.Lines[0].Id);
    }

    /// <summary>Saved Draft owned by the submitter (branch 10 / department 20); each line costs 10 + 5% tax.</summary>
    private async Task<PurchaseOrderSource> SeedOrderAsync(Seeded s, params (long Item, long? Source, decimal Quantity)[] lines)
    {
        await using var db = NewDb(s);
        var nets = lines.Select(x => x.Quantity * 10m).ToArray();
        var order = new PurchaseOrderSource
        {
            CompanyId = s.CompanyId, SupplierId = 101, DocumentNo = $"PO-{Guid.NewGuid():N}"[..20],
            BusinessDate = Day, Currency = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m, Status = "Draft",
            OwnerId = s.UserId, BranchId = 10, DepartmentId = 20, BaseNet = nets.Sum(),
            BaseTax = nets.Sum(x => Math.Round(x * 0.05m, 4)), BaseGross = nets.Sum(x => x + Math.Round(x * 0.05m, 4))
        };
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        db.AddRange(lines.Select((line, i) => new PurchaseOrderSourceLine
        {
            CompanyId = s.CompanyId, PurchaseOrderId = order.Id, LineNo = i + 1, MaterialId = line.Item,
            RequisitionLineId = line.Source, Quantity = line.Quantity, UnitPrice = 10m, TaxRateId = 51,
            TaxRatePercent = 5m, ExchangeRate = 1m, OriginalNet = nets[i], OriginalTax = Math.Round(nets[i] * 0.05m, 4),
            OriginalGross = nets[i] + Math.Round(nets[i] * 0.05m, 4), BaseNet = nets[i],
            BaseTax = Math.Round(nets[i] * 0.05m, 4), BaseGross = nets[i] + Math.Round(nets[i] * 0.05m, 4)
        }));
        await db.SaveChangesAsync(Ct);
        return order;
    }

    private static PurchaseOrderSubmissionService Service(MyErpDbContext db, Seeded s, long? userId = null,
        DataScope? scope = DataScope.Company, long branchId = 10)
    {
        var user = userId ?? s.UserId;
        var grants = new Dictionary<string, DataScope>();
        if (scope is { } granted) grants[PurchasingPermissions.OrderSubmit] = granted;
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(user, s.CompanyId, branchId, 20, false, grants));
        var items = new Mock<IRequisitionItemGuard>();
        items.Setup(x => x.RunAsync(It.IsAny<IReadOnlyCollection<long>>(),
                It.IsAny<Func<Task<PurchaseOrderSubmissionResult>>>(), It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyCollection<long>, Func<Task<PurchaseOrderSubmissionResult>>, CancellationToken>(
                (_, action, _) => action());
        var suppliers = new Mock<ISupplierLookup>();
        suppliers.Setup(x => x.RequireApprovedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => new SupplierIdentity(id, "SUP", "Supplier"));
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(user), new AuditTrail(), []);
        return new PurchaseOrderSubmissionService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, items.Object, suppliers.Object, workflow);
    }

    private MyErpDbContext NewDb(Seeded s, DbCommandInterceptor? fault = null) => NewDb(s.CompanyId, fault, s.UserId);

    private MyErpDbContext NewDb(long? companyId, DbCommandInterceptor? fault = null, long userId = 7)
    {
        var company = new FixedCompany(companyId);
        var audit = new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Today), new FixedUser(userId), company));
        return new(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(fault is null ? [audit] : [audit, fault]).Options,
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel(), new SystemOrganizationDbModel()], company);
    }

    private sealed class FailCommand(string statement) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains(statement, StringComparison.OrdinalIgnoreCase))
            {
                Fired = true;
                throw new InvalidOperationException($"Injected {statement} failure.");
            }
        }
    }
}
