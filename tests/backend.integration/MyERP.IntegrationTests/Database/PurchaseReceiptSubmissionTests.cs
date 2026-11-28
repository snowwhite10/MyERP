using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
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
/// WU-PUR-007a9（REQ_F4114.2.4～5、ADR-218／219，PUR-RECV-SUB-001～008）：收貨 SUBMIT 以真實 SQL 與真實 SYS 審批引擎
/// 驗收——保存行聚合預留訂單行待審量、整單零變更失敗、雙連線同訂單行最多一單成功、SYS 失敗回滾、駁回精確釋回。
/// </summary>
public sealed class PurchaseReceiptSubmissionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Today = new(2028, 2, 29, 9, 30, 0);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Today);

    [Fact]
    public async Task SubmitAggregatesSavedLinesReservesCapacityAndCreatesPendingApproval()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, 5m);
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 1m, 4m), (lineIds[0], 2m, 4.333333m));

        await using (var db = NewDb(s))
        {
            var result = await Service(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct);
            Assert.Equal("PendingApproval", result.Status);
            Assert.Equal(ApprovalStatus.Pending, result.Approval.Status);
            Assert.True(result.Version > receipt.Version);
        }
        await using var check = NewDb(s);
        Assert.Equal((3m, 0m), await CountersAsync(check, lineIds[0]));
        var instance = await Instances(check, receipt.Id).SingleAsync(Ct);
        Assert.Equal(ApprovalStatus.Pending, instance.Status);
        Assert.Equal(4m + 8.6667m, instance.Amount);
        Assert.Equal(receipt.DocumentNo, instance.DocumentNo);
    }

    [Fact]
    public async Task ExactRemainingCapacitySucceedsAndAnyShortageLeavesZeroChanges()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, 5m, 5m);
        await SetCountersAsync(lineIds[0], posted: 0m, pending: 2m);
        await SetCountersAsync(lineIds[1], posted: 1m, pending: 0m);
        var over = await SeedReceiptAsync(s, orderId, (lineIds[0], 1m, 4m), (lineIds[1], 4.000001m, 4m));
        var exact = await SeedReceiptAsync(s, orderId, (lineIds[0], 3m, 4m));

        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(over.Id, over.Version, Ct));
            Assert.Equal("PUR.RECEIPT.QUANTITY_EXCEEDED", error.ErrorCode);
        }
        await AssertUntouchedAsync(s, over, (lineIds[0], 2m), (lineIds[1], 0m));

        await using (var db = NewDb(s))
            Assert.Equal("PendingApproval", (await Service(db, s).SubmitAsync(exact.Id, exact.Version, Ct)).Status);
        await using var check = NewDb(s);
        Assert.Equal((5m, 0m), await CountersAsync(check, lineIds[0]));
    }

    [Fact]
    public async Task VersionPermissionScopeCompanyAndStatusGuardsLeaveDraftUntouched()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, 5m);
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));
        await using (var db = NewDb(s))
        {
            await Assert.ThrowsAsync<ConflictException>(() => Service(db, s).SubmitAsync(receipt.Id, receipt.Version + 1, Ct));
            await Assert.ThrowsAsync<ConflictException>(() => Service(db, s).SubmitAsync(receipt.Id, 0, Ct));
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Service(db, s, scope: null).SubmitAsync(receipt.Id, receipt.Version, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Service(db, s, userId: 999, scope: DataScope.Self).SubmitAsync(receipt.Id, receipt.Version, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Service(db, s, scope: DataScope.Branch, branchId: 77).SubmitAsync(receipt.Id, receipt.Version, Ct));
        }
        var other = s with { CompanyId = s.OtherCompanyId };
        await using (var db = NewDb(other))
            await Assert.ThrowsAsync<NotFoundException>(() => Service(db, other).SubmitAsync(receipt.Id, receipt.Version, Ct));
        await AssertUntouchedAsync(s, receipt, (lineIds[0], 0m));

        foreach (var status in new[] { "PendingApproval", "Rejected", "Posted" })
        {
            await Raw($"UPDATE dbo.pur_purchase_receipt SET status = {status} WHERE id = {receipt.Id}");
            await using var db = NewDb(s);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct));
            Assert.Equal("PUR.RECEIPT.NOT_SUBMITTABLE", error.ErrorCode);
            Assert.Equal((0m, 0m), await CountersAsync(db, lineIds[0]));
            Assert.Empty(await Instances(db, receipt.Id).ToListAsync(Ct));
        }

        await Raw($"UPDATE dbo.pur_purchase_receipt SET status = 'Draft' WHERE id = {receipt.Id}");
        await Raw($"UPDATE dbo.pur_purchase_order SET status = 'Cancelled', cancel_reason = N'gone' WHERE id = {orderId}");
        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct));
            Assert.Equal("PUR.RECEIPT.INVALID_SOURCE", error.ErrorCode);
        }
        await AssertUntouchedAsync(s, receipt, (lineIds[0], 0m));
    }

    [Fact]
    public async Task ZeroStepAutoApprovalIsRefusedUntilInboundPostingExists()
    {
        var s = await SeedCompanyAsync(withFlow: false);
        var (orderId, lineIds) = await SeedOrderAsync(s, 5m);
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));
        await using (var db = NewDb(s))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct));
            Assert.Equal("PUR.RECEIPT.APPROVAL_FLOW_REQUIRED", error.ErrorCode);
        }
        await AssertUntouchedAsync(s, receipt, (lineIds[0], 0m));
    }

    [Fact]
    public async Task CompetingSubmitsOnTwoConnectionsCannotOverReserveOrderLine()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, 5m);
        var a = await SeedReceiptAsync(s, orderId, (lineIds[0], 3m, 4m));
        var b = await SeedReceiptAsync(s, orderId, (lineIds[0], 3m, 4m));

        await using var first = NewDb(s);
        await using var second = NewDb(s);
        var outcomes = await Task.WhenAll(
            Capture(() => Service(first, s).SubmitAsync(a.Id, a.Version, Ct)),
            Capture(() => Service(second, s).SubmitAsync(b.Id, b.Version, Ct)));

        var winner = Assert.Single(outcomes, x => x.Result is not null);
        var loser = Assert.Single(outcomes, x => x.Error is not null);
        Assert.True(loser.Error is ConflictException
            || loser.Error is BusinessRuleException { ErrorCode: "PUR.RECEIPT.QUANTITY_EXCEEDED" }, loser.Error?.ToString());
        await using var check = NewDb(s);
        Assert.Equal((3m, 0m), await CountersAsync(check, lineIds[0]));
        var loserId = winner.Result!.Id == a.Id ? b.Id : a.Id;
        Assert.Equal("Draft", await check.Set<PurchaseReceiptSource>().AsNoTracking()
            .Where(x => x.Id == loserId).Select(x => x.Status).SingleAsync(Ct));
        Assert.Empty(await Instances(check, loserId).ToListAsync(Ct));
    }

    [Theory]
    [InlineData("UPDATE [pur_purchase_order_line]")]
    [InlineData("INSERT INTO [sys_approval_instance]")]
    public async Task FailureWhileReservingOrCreatingApprovalRollsBackEverythingAndRetrySucceeds(string statement)
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, 5m);
        var receipt = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));
        var fault = new FailCommand(statement);
        await using (var failing = NewDb(s, fault))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                Service(failing, s).SubmitAsync(receipt.Id, receipt.Version, Ct));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        Assert.True(fault.Fired);
        await AssertUntouchedAsync(s, receipt, (lineIds[0], 0m));

        await using var retry = NewDb(s);
        Assert.Equal("PendingApproval", (await Service(retry, s).SubmitAsync(receipt.Id, receipt.Version, Ct)).Status);
        Assert.Equal((2m, 0m), await CountersAsync(retry, lineIds[0]));
    }

    [Fact]
    public async Task SysRejectionReleasesOnlyThisReceiptAndApprovalIsRefusedWithoutChanges()
    {
        var s = await SeedCompanyAsync(withFlow: true);
        var (orderId, lineIds) = await SeedOrderAsync(s, 5m);
        var other = await SeedReceiptAsync(s, orderId, (lineIds[0], 2m, 4m));
        var mine = await SeedReceiptAsync(s, orderId, (lineIds[0], 1m, 4m), (lineIds[0], 2m, 4m));
        await SubmitPendingAsync(s, other);
        await SubmitPendingAsync(s, mine);
        await using (var check = NewDb(s)) Assert.Equal((5m, 0m), await CountersAsync(check, lineIds[0]));

        // Final approval needs INV posting (007b): the SYS action rolls back and leaves the instance pending.
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => DecideAsync(s, other.Id, approve: true));
        Assert.Equal("PUR.RECEIPT.POSTING_NOT_AVAILABLE", refused.ErrorCode);
        await using (var check = NewDb(s))
        {
            Assert.Equal(ApprovalStatus.Pending, (await Instances(check, other.Id).SingleAsync(Ct)).Status);
            Assert.Equal("PendingApproval", await StatusAsync(check, other.Id));
            Assert.Equal((5m, 0m), await CountersAsync(check, lineIds[0]));
        }

        var rejected = await DecideAsync(s, mine.Id, approve: false);
        Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
        await using (var check = NewDb(s))
        {
            Assert.Equal("Rejected", await StatusAsync(check, mine.Id));
            Assert.Equal("PendingApproval", await StatusAsync(check, other.Id));
            Assert.Equal((2m, 0m), await CountersAsync(check, lineIds[0]));
        }

        // A repeated or stale decision must not release a second time.
        await using (var db = NewDb(s))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            var handler = Assert.Single(Handlers(db), x => x.DocumentType == "PUR.RECEIPT");
            await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(
                new ApprovalCompleted(1, "PUR.RECEIPT", mine.Id, ApprovalStatus.Rejected, s.ApproverId, null, null), Ct));
        }
        await using (var check = NewDb(s)) Assert.Equal((2m, 0m), await CountersAsync(check, lineIds[0]));

        // The rejected receipt is revised back to Draft by EDIT and can then reserve again.
        await Raw($"UPDATE dbo.pur_purchase_receipt SET status = 'Draft' WHERE id = {mine.Id}");
        await using (var db = NewDb(s))
        {
            var version = await db.Set<PurchaseReceiptSource>().Where(x => x.Id == mine.Id).Select(x => x.Version).SingleAsync(Ct);
            Assert.Equal("PendingApproval", (await Service(db, s).SubmitAsync(mine.Id, version, Ct)).Status);
        }
        await using (var check = NewDb(s)) Assert.Equal((5m, 0m), await CountersAsync(check, lineIds[0]));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Seeded(long CompanyId, long OtherCompanyId, long UserId, long ApproverId);

    private sealed record Outcome(PurchaseReceiptSubmissionResult? Result, Exception? Error);

    private static async Task<Outcome> Capture(Func<Task<PurchaseReceiptSubmissionResult>> action)
    {
        try { return new Outcome(await action(), null); }
        catch (Exception ex) { return new Outcome(null, ex); }
    }

    private static IQueryable<ApprovalInstance> Instances(MyErpDbContext db, long receiptId) =>
        db.Set<ApprovalInstance>().AsNoTracking().Where(x => x.DocumentType == "PUR.RECEIPT" && x.DocumentId == receiptId);

    private static async Task<(decimal Pending, decimal Posted)> CountersAsync(MyErpDbContext db, long lineId)
    {
        var line = await db.Set<PurchaseOrderSourceLine>().AsNoTracking().SingleAsync(x => x.Id == lineId, Ct);
        return (line.PendingReceiptReserved, line.PostedReceived);
    }

    private static Task<string> StatusAsync(MyErpDbContext db, long receiptId) =>
        db.Set<PurchaseReceiptSource>().AsNoTracking().Where(x => x.Id == receiptId).Select(x => x.Status).SingleAsync(Ct);

    private async Task AssertUntouchedAsync(Seeded s, PurchaseReceiptSource receipt, params (long Id, decimal Pending)[] lines)
    {
        await using var check = NewDb(s);
        var saved = await check.Set<PurchaseReceiptSource>().AsNoTracking().SingleAsync(x => x.Id == receipt.Id, Ct);
        Assert.Equal("Draft", saved.Status);
        Assert.Equal(receipt.Version, saved.Version);
        foreach (var (id, pending) in lines) Assert.Equal(pending, (await CountersAsync(check, id)).Pending);
        Assert.Empty(await Instances(check, receipt.Id).ToListAsync(Ct));
    }

    private async Task Raw(FormattableString sql)
    {
        await using var raw = database.CreateRawContext();
        await raw.Database.ExecuteSqlInterpolatedAsync(sql, Ct);
    }

    private Task SetCountersAsync(long lineId, decimal posted, decimal pending) =>
        Raw($"UPDATE dbo.pur_purchase_order_line SET posted_received = {posted}, pending_receipt_reserved = {pending} WHERE id = {lineId}");

    private async Task SubmitPendingAsync(Seeded s, PurchaseReceiptSource receipt)
    {
        await using var db = NewDb(s);
        Assert.Equal("PendingApproval", (await Service(db, s).SubmitAsync(receipt.Id, receipt.Version, Ct)).Status);
    }

    private async Task<ApprovalInstanceDetail> DecideAsync(Seeded s, long receiptId, bool approve)
    {
        await using var db = NewDb(s);
        var instance = await Instances(db, receiptId).SingleAsync(Ct);
        var decision = new ApprovalDecision(approve ? null : "Quantity mismatch", instance.Version);
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(s.ApproverId),
            new AuditTrail(), Handlers(db));
        return approve
            ? await workflow.ApproveAsync(instance.Id, decision, Ct)
            : await workflow.RejectAsync(instance.Id, decision, Ct);
    }

    /// <summary>Resolve decision handlers exactly as the API does, through the PUR module registration.</summary>
    private static IApprovalResultHandler[] Handlers(MyErpDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        new PurchasingModule().AddServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetServices<IApprovalResultHandler>().ToArray();
    }

    private async Task<Seeded> SeedCompanyAsync(bool withFlow)
    {
        var name = "prs" + Guid.NewGuid().ToString("N")[..16];
        var user = new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
        var approverName = "pra" + Guid.NewGuid().ToString("N")[..16];
        var approver = new SystemUser { Username = approverName, NormalizedUsername = approverName.ToUpperInvariant(),
            PasswordHash = "x" };
        SystemCompany company, other;
        await using (var setup = NewDb((long?)null))
        {
            company = new SystemCompany { Code = "RS" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Receipt submit test" };
            other = new SystemCompany { Code = "RX" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Receipt submit other" };
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
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("PUR.RECEIPT", "Purchase receipt", false,
                [new ApprovalStepInput("Warehouse manager", ApproverType.User, null, approver.Id, null)]), Ct);
        }
        return seeded;
    }

    /// <summary>Approved order; each line costs 4 base currency per unit.</summary>
    private async Task<(long OrderId, long[] LineIds)> SeedOrderAsync(Seeded s, params decimal[] quantities)
    {
        await using var db = NewDb(s);
        var order = new PurchaseOrderSource
        {
            CompanyId = s.CompanyId, SupplierId = 101, DocumentNo = $"PO-{Guid.NewGuid():N}"[..20], BusinessDate = Day,
            Currency = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m, Status = "Approved", OwnerId = s.UserId,
            BranchId = 10, DepartmentId = 20, BaseNet = quantities.Sum() * 4m, BaseTax = 0m, BaseGross = quantities.Sum() * 4m
        };
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        var lines = quantities.Select((q, i) => new PurchaseOrderSourceLine
        {
            CompanyId = s.CompanyId, PurchaseOrderId = order.Id, LineNo = i + 1, MaterialId = 11 + i, Quantity = q,
            UnitPrice = 4m, TaxRatePercent = 0m, ExchangeRate = 1m, BaseNet = q * 4m, BaseTax = 0m, BaseGross = q * 4m
        }).ToArray();
        db.AddRange(lines);
        await db.SaveChangesAsync(Ct);
        return (order.Id, lines.Select(x => x.Id).ToArray());
    }

    /// <summary>Saved Draft owned by the submitter (branch 10 / department 20).</summary>
    private async Task<PurchaseReceiptSource> SeedReceiptAsync(Seeded s, long orderId,
        params (long LineId, decimal Quantity, decimal UnitCost)[] lines)
    {
        await using var db = NewDb(s);
        var sources = await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .Where(x => x.PurchaseOrderId == orderId).ToDictionaryAsync(x => x.Id, Ct);
        var receipt = new PurchaseReceiptSource
        {
            CompanyId = s.CompanyId, PurchaseOrderId = orderId, SupplierId = 101, Currency = "TWD",
            DocumentNo = $"GR-{Guid.NewGuid():N}"[..20], BusinessDate = Day, Status = "Draft",
            OwnerId = s.UserId, BranchId = 10, DepartmentId = 20
        };
        db.Add(receipt);
        await db.SaveChangesAsync(Ct);
        db.AddRange(lines.Select((line, i) => new PurchaseReceiptSourceLine
        {
            CompanyId = s.CompanyId, PurchaseReceiptId = receipt.Id, LineNo = i + 1, PurchaseOrderLineId = line.LineId,
            MaterialId = sources[line.LineId].MaterialId, Quantity = line.Quantity, WarehouseId = 91, UnitCost = line.UnitCost
        }));
        await db.SaveChangesAsync(Ct);
        return receipt;
    }

    private static PurchaseReceiptSubmissionService Service(MyErpDbContext db, Seeded s, long? userId = null,
        DataScope? scope = DataScope.Company, long branchId = 10)
    {
        var user = userId ?? s.UserId;
        var grants = new Dictionary<string, DataScope>();
        if (scope is { } granted) grants[PurchasingPermissions.ReceiptSubmit] = granted;
        else grants[PurchasingPermissions.ReceiptEdit] = DataScope.Company;
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(user, s.CompanyId, branchId, 20, false, grants));
        var workflow = new ApprovalWorkflowService(db, new FixedClock(Today), new FixedUser(user), new AuditTrail(), []);
        return new PurchaseReceiptSubmissionService(db, new RequisitionCompanyAccess(new FixedCompany(s.CompanyId)),
            authorization.Object, workflow);
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
