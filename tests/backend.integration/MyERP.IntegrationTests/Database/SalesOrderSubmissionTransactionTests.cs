using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
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
/// WU-SD-003b1: SD-ORDER-009/010/011, SD-CREDIT-001/003/004/005/008/015 — order submit and SYS final decision
/// recheck FI credit under the FI credit lock on real SQL; WU-SD-006b1/b2 add the reasoned override.
/// </summary>
public sealed class SalesOrderSubmissionTransactionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Now = new(2026, 11, 4, 9, 0, 0);
    private static readonly DateOnly Day = new(2026, 10, 1);
    private const long Customer = 21;

    [Fact]
    public async Task WithinLimitSubmitsPendingAndFinalApprovalRechecksCreditBeforeApproving()
    {
        var companyId = await SeedCompanyAsync("OA");
        // Limit 1000; open AR 300; another Approved order 200; this order 400 → headroom 100.
        await SeedLimitAsync(companyId, 1000m);
        await SeedReceivableAsync(companyId, 300m);
        await SeedOrderAsync(companyId, SalesOrderStatus.Approved, 200m);
        await SeedOrderAsync(companyId, SalesOrderStatus.Cancelled, 5000m);
        await SeedOrderAsync(companyId, SalesOrderStatus.Pending, 5000m);
        var order = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 400m);
        var (submitter, approver) = await SeedFlowAsync(companyId);

        long instanceId;
        await using (var db = Context(companyId))
        {
            var result = await Submission(db, companyId, Live(db, submitter)).SubmitAsync(order.Id, 1, Ct);
            Assert.Equal((SalesOrderStatus.Pending, 2), (result.Order.Status, result.Order.Version));
            Assert.Equal(ApprovalStatus.Pending, result.Approval.Status);
            Assert.Equal(result.Approval.InstanceId, result.Order.ApprovalInstanceId);
            Assert.Equal((SalesCreditStatus.WithinLimit, 1000m, 300m, 200m, 400m, 100m),
                (result.Credit.Status, result.Credit.Limit, result.Credit.PostedOpenAr,
                    result.Credit.OtherCommitments, result.Credit.Candidate, result.Credit.Headroom));
            Assert.Equal(new DateOnly(2026, 11, 4), result.Credit.EvaluationDate);
            Assert.Equal(2, result.Order.Lines.Count);
            instanceId = result.Approval.InstanceId;
        }

        await using (var approveDb = Context(companyId))
            await Live(approveDb, approver).ApproveAsync(instanceId, new ApprovalDecision(null, 1), Ct);

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesOrder>().SingleAsync(x => x.Id == order.Id, Ct);
        Assert.Equal((SalesOrderStatus.Approved, (DateTime?)Now), (saved.Status, saved.ApprovedAt));
    }

    [Fact]
    public async Task FinalApprovalIsRefusedWhenExposureGrewAfterSubmitAndRejectionNeedsNoCredit()
    {
        var companyId = await SeedCompanyAsync("OB");
        await SeedLimitAsync(companyId, 1000m);
        var first = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 600m);
        var second = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 600m);
        var (submitter, approver) = await SeedFlowAsync(companyId);

        long firstInstance, secondInstance;
        await using (var db = Context(companyId))
        {
            // Pending orders do not reserve credit: both pass the submit check alone (BR-SD-032).
            firstInstance = (await Submission(db, companyId, Live(db, submitter)).SubmitAsync(first.Id, 1, Ct))
                .Approval.InstanceId;
            secondInstance = (await Submission(db, companyId, Live(db, submitter)).SubmitAsync(second.Id, 1, Ct))
                .Approval.InstanceId;
        }

        await using (var db = Context(companyId))
            await Live(db, approver).ApproveAsync(firstInstance, new ApprovalDecision(null, 1), Ct);

        await using (var db = Context(companyId))
        {
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Live(db, approver).ApproveAsync(secondInstance, new ApprovalDecision(null, 1), Ct));
            Assert.Equal(SalesCreditErrors.LimitExceeded, refused.ErrorCode);
            Assert.Equal(200m, refused.Extensions!["excess"]);
            Assert.Equal("TWD", refused.Extensions["currency"]);
        }

        await using (var db = Context(companyId))
        {
            // The SYS decision rolled back with the order: still Pending in both owners.
            Assert.Equal(ApprovalStatus.Pending,
                (await Live(db, submitter).GetStateAsync("SAL.ORDER", second.Id, Ct))!.Status);
            Assert.Equal(SalesOrderStatus.Pending,
                (await db.Set<SalesOrder>().SingleAsync(x => x.Id == second.Id, Ct)).Status);
            await Live(db, approver).RejectAsync(secondInstance, new ApprovalDecision("Over credit", 1), Ct);
        }

        await using var verify = Context(companyId);
        var states = await verify.Set<SalesOrder>().Where(x => x.Id == first.Id || x.Id == second.Id)
            .OrderBy(x => x.Id).Select(x => new { x.Status, x.ApprovedAt }).ToListAsync(Ct);
        Assert.Equal((SalesOrderStatus.Approved, (DateTime?)Now), (states[0].Status, states[0].ApprovedAt));
        Assert.Equal((SalesOrderStatus.Rejected, (DateTime?)null), (states[1].Status, states[1].ApprovedAt));
    }

    [Fact]
    public async Task OverLimitAndBlockedCreditRefuseSubmitWithoutSysInstanceOrOrderChange()
    {
        var companyId = await SeedCompanyAsync("OC");
        await SeedLimitAsync(companyId, 500m);
        await SeedReceivableAsync(companyId, 100m);
        var over = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 400.0001m);
        var exact = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 400m);
        var noLimit = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 1m, customerId: 22);
        await using var db = Context(companyId);
        var live = Live(db, 7);

        var exceeded = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, live).SubmitAsync(over.Id, 1, Ct));
        Assert.Equal(SalesCreditErrors.LimitExceeded, exceeded.ErrorCode);
        Assert.Equal(0.0001m, exceeded.Extensions!["excess"]);
        Assert.Null(await live.GetStateAsync("SAL.ORDER", over.Id, Ct));

        Assert.Equal(SalesCreditErrors.LimitNotSet, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, live).SubmitAsync(noLimit.Id, 1, Ct))).ErrorCode);
        Assert.Equal(SalesCreditErrors.CustomerInactive, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, live, inactiveCustomer: true).SubmitAsync(exact.Id, 1, Ct))).ErrorCode);
        Assert.Equal("SAL.ORDER.BASE_CURRENCY_CHANGED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, live, baseCurrency: "USD").SubmitAsync(exact.Id, 1, Ct))).ErrorCode);

        // Exactly zero headroom passes. No active flow: SYS approves in the submit transaction.
        var zeroStep = await Submission(db, companyId, live).SubmitAsync(exact.Id, 1, Ct);
        Assert.Equal((SalesOrderStatus.Approved, (DateTime?)Now, 0m),
            (zeroStep.Order.Status, zeroStep.Order.ApprovedAt, zeroStep.Credit.Headroom));
        Assert.Equal(ApprovalStatus.Approved, zeroStep.Approval.Status);

        await using var verify = Context(companyId);
        var refused = await verify.Set<SalesOrder>().Where(x => x.Id == over.Id || x.Id == noLimit.Id)
            .Select(x => new { x.Status, x.Version, x.ApprovalInstanceId }).ToListAsync(Ct);
        Assert.All(refused, x => Assert.Equal((SalesOrderStatus.Draft, 1, (long?)null),
            (x.Status, x.Version, x.ApprovalInstanceId)));
    }

    [Fact]
    public async Task SubmitRequiresActionScopeCurrentVersionAndDraftStatus()
    {
        var companyId = await SeedCompanyAsync("OD");
        var foreignCompanyId = await SeedCompanyAsync("OE");
        var foreign = await SeedOrderAsync(foreignCompanyId, SalesOrderStatus.Draft, 1m);
        await SeedLimitAsync(companyId, 1000m);
        var draft = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 10m);
        var rejected = await SeedOrderAsync(companyId, SalesOrderStatus.Rejected, 10m);
        var editOnly = new Dictionary<string, DataScope> { ["SAL.ORDER.EDIT"] = DataScope.Company };
        var self = new Dictionary<string, DataScope> { ["SAL.ORDER.SUBMIT"] = DataScope.Self };
        await using var db = Context(companyId);
        var fake = new FakeWorkflow();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Submission(db, companyId, fake, permissions: editOnly).SubmitAsync(draft.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submission(db, companyId, fake, permissions: self, userId: 8).SubmitAsync(draft.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submission(db, companyId, fake).SubmitAsync(foreign.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submission(db, companyId, fake).SubmitAsync(long.MaxValue, 1, Ct));
        await Assert.ThrowsAsync<ConflictException>(() =>
            Submission(db, companyId, fake).SubmitAsync(draft.Id, 2, Ct));
        Assert.Equal("SAL.ORDER.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, fake).SubmitAsync(rejected.Id, 1, Ct))).ErrorCode);

        var pending = await Submission(db, companyId, fake, permissions: self).SubmitAsync(draft.Id, 1, Ct);
        Assert.Equal(SalesOrderStatus.Pending, pending.Order.Status);
        Assert.Equal("SAL.ORDER.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, fake).SubmitAsync(draft.Id, pending.Order.Version, Ct))).ErrorCode);
    }

    [Fact]
    public async Task ConcurrentApprovalsOfOneCustomerCannotBothConsumeTheSameHeadroom()
    {
        // BR-SD-033: the FI credit key serializes approvals per company/customer; other customers are independent.
        var companyId = await SeedCompanyAsync("OF");
        await SeedLimitAsync(companyId, 1000m);
        await SeedLimitAsync(companyId, 1000m, customerId: 23);
        var winner = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 600m);
        var loser = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 600m);
        var otherCustomer = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 600m, customerId: 23);

        var outcomes = new List<string>();
        Task<string>? contender = null;
        var workflow = new ApprovingWorkflow
        {
            // Runs inside the winner's open transaction, after its credit check and before its commit.
            OnSubmit = async () =>
            {
                outcomes.Add(await TrySubmitAsync(companyId, otherCustomer.Id, lockTimeout: true));
                contender = Task.Run(() => TrySubmitAsync(companyId, loser.Id, lockTimeout: false));
                await Task.Delay(1500, Ct);
                outcomes.Add(contender.IsCompleted ? "not waiting" : "waiting");
            }
        };
        await using (var db = Context(companyId))
            await Submission(db, companyId, workflow).SubmitAsync(winner.Id, 1, Ct);
        outcomes.Add(await contender!);

        Assert.Equal(["done", "waiting", SalesCreditErrors.LimitExceeded], outcomes);
        await using var verify = Context(companyId);
        var states = await verify.Set<SalesOrder>()
            .Where(x => x.Id == winner.Id || x.Id == loser.Id || x.Id == otherCustomer.Id)
            .OrderBy(x => x.Id).Select(x => x.Status).ToListAsync(Ct);
        Assert.Equal([SalesOrderStatus.Approved, SalesOrderStatus.Draft, SalesOrderStatus.Approved], states);
    }

    [Fact]
    public async Task FailureAfterTheCreditCheckRollsBackSoTheWaitingOrderTakesTheHeadroom()
    {
        var companyId = await SeedCompanyAsync("OG");
        await SeedLimitAsync(companyId, 1000m);
        var failing = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 600m);
        var waiting = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 600m);
        Task<string>? contender = null;
        var workflow = new ApprovingWorkflow
        {
            OnSubmit = async () =>
            {
                contender = Task.Run(() => TrySubmitAsync(companyId, waiting.Id, lockTimeout: false));
                await Task.Delay(1000, Ct);
                throw new InvalidOperationException("Simulated SYS failure after the credit check.");
            }
        };
        await using (var db = Context(companyId))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Submission(db, companyId, workflow).SubmitAsync(failing.Id, 1, Ct));

        Assert.Equal("done", await contender!);
        await using var verify = Context(companyId);
        var states = await verify.Set<SalesOrder>().Where(x => x.Id == failing.Id || x.Id == waiting.Id)
            .OrderBy(x => x.Id).Select(x => new { x.Status, x.Version, x.ApprovalInstanceId }).ToListAsync(Ct);
        Assert.Equal((SalesOrderStatus.Draft, 1, (long?)null),
            (states[0].Status, states[0].Version, states[0].ApprovalInstanceId));
        Assert.Equal(SalesOrderStatus.Approved, states[1].Status);
    }

    [Fact]
    public async Task OverLimitSubmitWithGrantAndReasonSavesOneOverrideBoundToTheSysRound()
    {
        // WU-SD-006b1 (BR-SD-034/036, SD-CREDIT-019/010): reason trimmed; actor/time from the server; one row.
        var companyId = await SeedCompanyAsync("OH");
        await SeedLimitAsync(companyId, 500m);
        await SeedReceivableAsync(companyId, 100m);
        await SeedOrderAsync(companyId, SalesOrderStatus.Approved, 50m);
        var zeroStep = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 400.5m);
        var pending = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 700m);

        SalesOrderSubmissionResult approved, waiting;
        await using (var db = Context(companyId))
        {
            approved = await Submission(db, companyId, new ApprovingWorkflow())
                .SubmitAsync(zeroStep.Id, 1, "  Customer prepaid by wire  ", Ct);
            waiting = await Submission(db, companyId, new FakeWorkflow())
                .SubmitAsync(pending.Id, 1, "Year-end campaign", Ct);
        }

        Assert.Equal((SalesOrderStatus.Approved, 2), (approved.Order.Status, approved.Order.Version));
        Assert.Equal(SalesCreditStatus.OverLimit, approved.Credit.Status);
        Assert.Equal(50.5m, approved.Credit.Excess);
        Assert.NotNull(approved.Override);
        Assert.Equal(SalesOrderStatus.Pending, waiting.Order.Status);

        await using var verify = Context(companyId);
        var rows = await verify.Set<SalesCreditOverride>().AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Id).ToListAsync(Ct);
        Assert.Equal(2, rows.Count);
        var first = rows[0];
        Assert.Equal(approved.Override!.Id, first.Id);
        Assert.Equal((zeroStep.Id, 1, approved.Approval.InstanceId, SalesCreditDecisionStage.Submit),
            (first.OrderId, first.OrderVersion, first.ApprovalInstanceId, first.Stage));
        Assert.Equal(("Customer prepaid by wire", 7L, Now, new DateOnly(2026, 11, 4), 3, "TWD"),
            (first.Reason, first.CreatedBy, first.CreatedAt, first.EvaluationDate, first.CreditLimitVersion,
                first.BaseCurrencyCode));
        Assert.Equal((500m, 100m, 50m, 400.5m, 50.5m),
            (first.CreditLimit, first.PostedOpenAr, first.OtherCommitments, first.Candidate, first.Excess));
        // The Pending order also counts the zero-step order approved just before it: 100 + 50 + 400.5 + 700 − 500.
        Assert.Equal((pending.Id, waiting.Approval.InstanceId, 450.5m, 750.5m),
            (rows[1].OrderId, rows[1].ApprovalInstanceId, rows[1].OtherCommitments, rows[1].Excess));
    }

    [Fact]
    public async Task OverrideIsRefusedWithoutReasonGrantValidReasonOrUsableCreditAndChangesNothing()
    {
        // SD-CREDIT-013/016/020: no reason 422; no OVERRIDE 403 (even with SUBMIT); scope must cover the order;
        // blank/oversize 400; blocked credit is never overridable; within limit ignores the reason.
        var companyId = await SeedCompanyAsync("OI");
        await SeedLimitAsync(companyId, 100m);
        var over = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 150m);
        var noLimit = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 150m, customerId: 24);
        var within = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 10m);
        var submitOnly = new Dictionary<string, DataScope> { ["SAL.ORDER.SUBMIT"] = DataScope.Company };
        var selfOverride = new Dictionary<string, DataScope>
        {
            ["SAL.ORDER.SUBMIT"] = DataScope.Company, ["SAL.CREDIT.OVERRIDE"] = DataScope.Self
        };
        await using var db = Context(companyId);
        var fake = new FakeWorkflow();

        var noReason = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, fake).SubmitAsync(over.Id, 1, null, Ct));
        Assert.Equal((SalesCreditErrors.LimitExceeded, (object?)50m), (noReason.ErrorCode, noReason.Extensions!["excess"]));
        Assert.Equal(SalesCreditErrors.OverrideForbidden, (await Assert.ThrowsAsync<ForbiddenException>(() =>
            Submission(db, companyId, fake, permissions: submitOnly).SubmitAsync(over.Id, 1, "ok", Ct))).ErrorCode);
        // OVERRIDE is Self scope and the order belongs to user 7, while user 8 submits.
        Assert.Equal(SalesCreditErrors.OverrideForbidden, (await Assert.ThrowsAsync<ForbiddenException>(() =>
            Submission(db, companyId, fake, permissions: selfOverride, userId: 8)
                .SubmitAsync(over.Id, 1, "ok", Ct))).ErrorCode);
        Assert.Equal("creditOverrideReason", (await Assert.ThrowsAsync<RequestValidationException>(() =>
            Submission(db, companyId, fake).SubmitAsync(over.Id, 1, "   ", Ct))).Errors.Keys.Single());
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            Submission(db, companyId, fake).SubmitAsync(over.Id, 1, new string('x', 201), Ct));
        Assert.Equal(SalesCreditErrors.LimitNotSet, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, fake).SubmitAsync(noLimit.Id, 1, "ok", Ct))).ErrorCode);
        Assert.Equal(SalesCreditErrors.CustomerInactive, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, fake, inactiveCustomer: true).SubmitAsync(over.Id, 1, "ok", Ct))).ErrorCode);

        var ignored = await Submission(db, companyId, fake, permissions: submitOnly)
            .SubmitAsync(within.Id, 1, "not needed", Ct);
        Assert.Equal((SalesCreditStatus.WithinLimit, (SalesCreditOverride?)null),
            (ignored.Credit.Status, ignored.Override));

        // A 200-character reason is the inclusive maximum.
        var maximal = await Submission(db, companyId, fake).SubmitAsync(over.Id, 1, new string('y', 200), Ct);
        Assert.Equal(200, maximal.Override!.Reason.Length);

        await using var verify = Context(companyId);
        Assert.Equal([over.Id], await verify.Set<SalesCreditOverride>().Where(x => x.CompanyId == companyId)
            .Select(x => x.OrderId).ToListAsync(Ct));
        Assert.Equal((SalesOrderStatus.Draft, 1), await verify.Set<SalesOrder>().Where(x => x.Id == noLimit.Id)
            .Select(x => ValueTuple.Create(x.Status, x.Version)).SingleAsync(Ct));
    }

    [Fact]
    public async Task OverrideRollsBackWithTheSubmitAndTheSavedRowIsAppendOnlyAndUniquePerRound()
    {
        // SD-CREDIT-010/011/024: a SYS failure leaves no override; replaying the version is 409 with one row;
        // UPDATE/DELETE are refused (51031) and a second row for the same order/round/stage violates the key.
        var companyId = await SeedCompanyAsync("OJ");
        await SeedLimitAsync(companyId, 100m);
        var order = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 120m);
        var failing = new ApprovingWorkflow
        {
            OnSubmit = () => throw new InvalidOperationException("Simulated SYS failure after the override decision.")
        };
        await using (var db = Context(companyId))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Submission(db, companyId, failing).SubmitAsync(order.Id, 1, "first try", Ct));
            Assert.False(await db.Set<SalesCreditOverride>().AnyAsync(x => x.OrderId == order.Id, Ct));

            await Submission(db, companyId, new FakeWorkflow()).SubmitAsync(order.Id, 1, "second try", Ct);
            await Assert.ThrowsAsync<ConflictException>(() =>
                Submission(db, companyId, new FakeWorkflow()).SubmitAsync(order.Id, 1, "replay", Ct));
        }

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesCreditOverride>().AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Ct);
        Assert.Equal(("second try", 20m), (saved.Reason, saved.Excess));
        foreach (var sql in new[]
                 {
                     "UPDATE dbo.sal_credit_override SET reason = N'changed' WHERE id = {0}",
                     "DELETE dbo.sal_credit_override WHERE id = {0}"
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() =>
                verify.Database.ExecuteSqlRawAsync(sql, [saved.Id], Ct));
            Assert.Equal(51031, refused.Number);
        }

        verify.Add(new SalesCreditOverride
        {
            CompanyId = companyId, OrderId = order.Id, OrderVersion = 2, ApprovalInstanceId = saved.ApprovalInstanceId,
            Stage = SalesCreditDecisionStage.Submit, EvaluationDate = saved.EvaluationDate, CreditLimitVersion = 3,
            BaseCurrencyCode = "TWD", CreditLimit = 100m, PostedOpenAr = 0m, OtherCommitments = 0m,
            Candidate = 120m, Excess = 20m, Reason = "duplicate"
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync(Ct));
        Assert.Equal(2601, Assert.IsType<SqlException>(duplicate.InnerException).Number);
    }

    [Fact]
    public async Task FinalApprovalOverridesOverLimitOnlyWithTheApproversReasonAndSavesFinalApprovalEvidence()
    {
        // WU-SD-006b2 (ADR-171, BR-SD-034/036, SD-CREDIT-OVR-004): a submit override does not carry over; the
        // approver's own reason and SAL.CREDIT.OVERRIDE approve the order and add one FinalApproval row bound to the
        // Pending version and the same SYS round. A replay is SYS NOT_PENDING and adds nothing.
        var companyId = await SeedCompanyAsync("OK");
        await SeedLimitAsync(companyId, 500m);
        await SeedReceivableAsync(companyId, 100m);
        var order = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 450m);
        var (submitter, approver) = await SeedFlowAsync(companyId);

        long instanceId;
        await using (var db = Context(companyId))
            instanceId = (await Submission(db, companyId, Live(db, submitter))
                .SubmitAsync(order.Id, 1, "Submitted with prepayment", Ct)).Approval.InstanceId;
        // Exposure grows after submit: another order of the customer is approved meanwhile.
        await SeedOrderAsync(companyId, SalesOrderStatus.Approved, 30m);

        await using (var db = Context(companyId))
        {
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Live(db, approver, companyId).ApproveAsync(instanceId, new ApprovalDecision(null, 1), Ct));
            Assert.Equal((SalesCreditErrors.LimitExceeded, (object?)80m),
                (refused.ErrorCode, refused.Extensions!["excess"]));
        }

        await using (var db = Context(companyId))
            await Live(db, approver, companyId).ApproveAsync(instanceId,
                new ApprovalDecision("CFO approved", 1, "  Customer paid a deposit  "), Ct);

        await using (var db = Context(companyId))
            Assert.Equal(ApprovalWorkflowService.NotPending, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Live(db, approver, companyId).ApproveAsync(instanceId,
                    new ApprovalDecision(null, 2, "replay"), Ct))).ErrorCode);

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesOrder>().SingleAsync(x => x.Id == order.Id, Ct);
        Assert.Equal((SalesOrderStatus.Approved, (DateTime?)Now, 3), (saved.Status, saved.ApprovedAt, saved.Version));
        var rows = await verify.Set<SalesCreditOverride>().AsNoTracking().Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.Id).ToListAsync(Ct);
        Assert.Equal([SalesCreditDecisionStage.Submit, SalesCreditDecisionStage.FinalApproval],
            rows.Select(x => x.Stage));
        var final = rows[1];
        Assert.Equal((2, instanceId, "Customer paid a deposit", Now, new DateOnly(2026, 11, 4), 3, "TWD"),
            (final.OrderVersion, final.ApprovalInstanceId, final.Reason, final.CreatedAt, final.EvaluationDate,
                final.CreditLimitVersion, final.BaseCurrencyCode));
        Assert.Equal((500m, 100m, 30m, 450m, 80m),
            (final.CreditLimit, final.PostedOpenAr, final.OtherCommitments, final.Candidate, final.Excess));
        Assert.Equal((50m, "Submitted with prepayment"), (rows[0].Excess, rows[0].Reason));
    }

    [Fact]
    public async Task FinalApprovalOverrideIsRefusedWithoutGrantValidReasonOrUsableCreditAndChangesNothing()
    {
        // SD-CREDIT-OVR-005: no OVERRIDE (or a scope that misses the order) 403; blank/oversize 400 on
        // overrideReason; blocked credit is never overridable; within limit and rejection ignore the reason.
        var companyId = await SeedCompanyAsync("OL");
        await SeedLimitAsync(companyId, 100m);
        var over = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 90m);
        var within = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 5m);
        var rejected = await SeedOrderAsync(companyId, SalesOrderStatus.Draft, 90m);
        var (submitter, approver) = await SeedFlowAsync(companyId);
        var processOnly = new Dictionary<string, DataScope> { ["SYS.APPROVAL.PROCESS"] = DataScope.Company };
        var selfOverride = new Dictionary<string, DataScope>
        {
            ["SYS.APPROVAL.PROCESS"] = DataScope.Company, ["SAL.CREDIT.OVERRIDE"] = DataScope.Self
        };

        long overInstance, withinInstance, rejectedInstance;
        await using (var db = Context(companyId))
        {
            overInstance = (await Submission(db, companyId, Live(db, submitter)).SubmitAsync(over.Id, 1, Ct))
                .Approval.InstanceId;
            withinInstance = (await Submission(db, companyId, Live(db, submitter)).SubmitAsync(within.Id, 1, Ct))
                .Approval.InstanceId;
            rejectedInstance = (await Submission(db, companyId, Live(db, submitter))
                .SubmitAsync(rejected.Id, 1, Ct)).Approval.InstanceId;
        }

        // Within limit the reason is ignored and needs no OVERRIDE. Pending orders reserve nothing (BR-SD-032), so
        // `over` exceeds only after `within` (5) and a later Approved order (30) are counted: 5 + 30 + 90 − 100 = 25.
        await using (var db = Context(companyId))
            await Live(db, approver, companyId, processOnly).ApproveAsync(withinInstance,
                new ApprovalDecision(null, 1, "not needed"), Ct);
        await SeedOrderAsync(companyId, SalesOrderStatus.Approved, 30m);

        async Task<Exception> Refuse(ApprovalDecision decision,
            IReadOnlyDictionary<string, DataScope>? permissions = null, bool inactiveCustomer = false)
        {
            await using var db = Context(companyId);
            return await Assert.ThrowsAnyAsync<Exception>(() =>
                Live(db, approver, companyId, permissions, inactiveCustomer).ApproveAsync(overInstance, decision, Ct));
        }

        var noReason = Assert.IsType<BusinessRuleException>(await Refuse(new ApprovalDecision(null, 1)));
        Assert.Equal((SalesCreditErrors.LimitExceeded, (object?)25m), (noReason.ErrorCode, noReason.Extensions!["excess"]));
        Assert.Equal(SalesCreditErrors.OverrideForbidden, Assert.IsType<ForbiddenException>(
            await Refuse(new ApprovalDecision(null, 1, "ok"), processOnly)).ErrorCode);
        // OVERRIDE is Self scope and the order belongs to user 7, not the approver.
        Assert.Equal(SalesCreditErrors.OverrideForbidden, Assert.IsType<ForbiddenException>(
            await Refuse(new ApprovalDecision(null, 1, "ok"), selfOverride)).ErrorCode);
        Assert.Equal("overrideReason", Assert.IsType<RequestValidationException>(
            await Refuse(new ApprovalDecision(null, 1, "   "))).Errors.Keys.Single());
        Assert.Equal("overrideReason", Assert.IsType<RequestValidationException>(
            await Refuse(new ApprovalDecision(null, 1, new string('x', 201)))).Errors.Keys.Single());
        Assert.Equal(SalesCreditErrors.CustomerInactive, Assert.IsType<BusinessRuleException>(
            await Refuse(new ApprovalDecision(null, 1, "ok"), inactiveCustomer: true)).ErrorCode);

        await using (var db = Context(companyId))
            await Live(db, approver, companyId).RejectAsync(rejectedInstance,
                new ApprovalDecision("Over credit", 1, "ignored on rejection"), Ct);

        await using var verify = Context(companyId);
        var states = await verify.Set<SalesOrder>().Where(x => x.Id == over.Id || x.Id == within.Id ||
                x.Id == rejected.Id).OrderBy(x => x.Id).Select(x => x.Status).ToListAsync(Ct);
        Assert.Equal([SalesOrderStatus.Pending, SalesOrderStatus.Approved, SalesOrderStatus.Rejected], states);
        Assert.Equal(ApprovalStatus.Pending,
            (await Live(verify, submitter).GetStateAsync("SAL.ORDER", over.Id, Ct))!.Status);
        Assert.False(await verify.Set<SalesCreditOverride>().AnyAsync(x => x.CompanyId == companyId, Ct));
    }

    private async Task<string> TrySubmitAsync(long companyId, long orderId, bool lockTimeout)
    {
        await using var db = Context(companyId);
        await db.Database.OpenConnectionAsync(Ct);
        if (lockTimeout)
            await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000", Ct);
        try
        {
            await Submission(db, companyId, new ApprovingWorkflow()).SubmitAsync(orderId, 1, Ct);
            return "done";
        }
        catch (BusinessRuleException error)
        {
            return error.ErrorCode;
        }
        catch (ConflictException error)
        {
            return error.ErrorCode;
        }
    }

    private async Task<long> SeedCompanyAsync(string prefix)
    {
        await using var setup = Context(null);
        var company = new SystemCompany { Code = prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Sales order submission test" };
        setup.Add(company);
        await setup.SaveChangesAsync(Ct);
        return company.Id;
    }

    private async Task<SalesOrder> SeedOrderAsync(long companyId, SalesOrderStatus status, decimal baseGross,
        long customerId = Customer)
    {
        await using var db = Context(companyId);
        var order = new SalesOrder { CompanyId = companyId, OwnerUserId = 7,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10], CustomerId = customerId, CustomerCode = "C21",
            CustomerName = "Customer", BusinessDate = Day, CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
            ExchangeRate = 1m, OriginalNet = baseGross, OriginalGross = baseGross, BaseNet = baseGross,
            BaseGross = baseGross };
        order.Lines.Add(Line(companyId, 1, baseGross - 1m));
        order.Lines.Add(Line(companyId, 2, 1m));
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        // Lines are inserted under Draft (trigger 51004); the header then follows the allowed transitions (51005).
        string[] path = status switch
        {
            SalesOrderStatus.Pending => ["Pending"],
            SalesOrderStatus.Rejected => ["Pending", "Rejected"],
            SalesOrderStatus.Approved => ["Approved"],
            SalesOrderStatus.Cancelled => ["Approved", "Cancelled"],
            _ => []
        };
        foreach (var next in path)
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = {1}, approved_at = CASE WHEN {1} IN ('Approved', 'Cancelled') " +
                "THEN '2026-10-27' END WHERE id = {0}", [order.Id, next], Ct);
        return order;
    }

    private static SalesOrderLine Line(long companyId, int lineNo, decimal gross) => new()
    {
        CompanyId = companyId, LineNo = lineNo, ItemId = 30 + lineNo, ItemCode = "I" + lineNo, ItemName = "Item",
        Quantity = 1m, UnitPrice = gross, TaxRateId = 41, OriginalNet = gross, OriginalGross = gross,
        BaseNet = gross, BaseGross = gross
    };

    private Task SeedLimitAsync(long companyId, decimal amount, long customerId = Customer) => ExecuteAsync(
        "IF NOT EXISTS (SELECT 1 FROM dbo.ar_credit_key WHERE company_id = {0} AND customer_id = {1}) " +
        "INSERT dbo.ar_credit_key (company_id, customer_id, created_at, created_by, version) " +
        "VALUES ({0}, {1}, '2026-11-01', 1, 1); " +
        "INSERT dbo.ar_credit_limit (company_id, customer_id, base_currency, effective_from, effective_to, amount, " +
        "status, created_at, created_by, version) VALUES ({0}, {1}, 'TWD', '2026-01-01', '2026-12-31', {2}, " +
        "'Active', '2026-11-01', 1, 3)", companyId, customerId, amount);

    private Task SeedReceivableAsync(long companyId, decimal open) => ExecuteAsync(
        "INSERT dbo.ar_posted_receivable (company_id, customer_id, source_type, source_id, base_currency, " +
        "original_base_amount, open_base_amount, posting_date, created_at, created_by, version) " +
        "VALUES ({0}, {1}, 'SD_INVOICE', {0}, 'TWD', {2}, {2}, '2026-10-31', '2026-11-01', 1, 1)",
        companyId, Customer, open);

    private async Task ExecuteAsync(string sql, long companyId, long customerId, decimal amount)
    {
        await using var db = Context(null);
        await db.Database.ExecuteSqlRawAsync(sql, [companyId, customerId, amount], Ct);
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
        await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("SAL.ORDER", "Order", false,
            [new ApprovalStepInput("Approver", ApproverType.User, null, approver.Id, null)]), Ct);
        return (submitter.Id, approver.Id);
    }

    private static SystemUser NewUser()
    {
        var name = "sdo" + Guid.NewGuid().ToString("N")[..16];
        return new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(Now), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesDeliveryDbModel(), new SalesOrderDbModel(), new SalesQuotationDbModel(),
            new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(), new CustomerDbModel(),
            new PaymentSetupDbModel(), new CurrencyDbModel(), new TaxDbModel()],
        new FixedCompany(companyId));

    private static SalesOrderCreditCheck Credit(bool inactiveCustomer = false, string baseCurrency = "TWD")
    {
        var customers = new Mock<ICustomerLookup>();
        if (inactiveCustomer)
            customers.Setup(x => x.RequireApprovedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new BusinessRuleException("MD.CUSTOMER.NOT_APPROVED", "Customer is not approved."));
        else
            customers.Setup(x => x.RequireApprovedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long id, CancellationToken _) => new CustomerIdentity(id, "C" + id, "Customer"));
        var currencies = new Mock<ICurrencyLookup>();
        currencies.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo(baseCurrency, baseCurrency, null, 0, true, true));
        var clock = new FixedClock(Now);
        return new SalesOrderCreditCheck(new CustomerCreditService(clock, new FixedUser(7)), customers.Object,
            currencies.Object, clock);
    }

    private static ApprovalWorkflowService Live(MyErpDbContext db, long userId, long? companyId = null,
        IReadOnlyDictionary<string, DataScope>? permissions = null, bool inactiveCustomer = false) =>
        new(db, new FixedClock(Now), new FixedUser(userId), new AuditTrail(),
            [new SalesOrderApprovalResultHandler(db, () => Credit(inactiveCustomer),
                Access(companyId, userId, permissions), new FixedClock(Now))]);

    private static SalesActionAccess Access(long? companyId, long userId,
        IReadOnlyDictionary<string, DataScope>? permissions)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(companyId is { } id
            ? new UserCompanyAuthorization(userId, id, null, null, permissions is null,
                permissions ?? new Dictionary<string, DataScope>())
            : null);
        return new SalesActionAccess(new SalesCompanyAccess(new FixedCompany(companyId)), auth.Object);
    }

    private static SalesOrderSubmissionService Submission(MyErpDbContext db, long companyId,
        IApprovalWorkflowService workflow, IReadOnlyDictionary<string, DataScope>? permissions = null,
        long userId = 7, bool inactiveCustomer = false, string baseCurrency = "TWD")
    {
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesOrderSubmissionService(db, Access(companyId, userId, permissions), company,
            Credit(inactiveCustomer, baseCurrency), workflow, new FixedClock(Now));
    }

    /// <summary>Zero-step SYS stand-in: approves immediately after an optional in-transaction callback.</summary>
    private sealed class ApprovingWorkflow : IApprovalWorkflowService
    {
        private static long next = 3_000_000;

        public Func<Task>? OnSubmit { get; init; }

        public async Task<ApprovalState> SubmitAsync(ApprovalSubmission submission,
            CancellationToken cancellationToken = default)
        {
            if (OnSubmit is { } callback) await callback();
            return new ApprovalState(Interlocked.Increment(ref next), submission.DocumentType,
                submission.DocumentId, ApprovalStatus.Approved, null, 1);
        }

        public Task<ApprovalState> CancelAsync(string documentType, long documentId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId,
            CancellationToken cancellationToken = default) => Task.FromResult<ApprovalState?>(null);
    }

    /// <summary>Always returns a Pending round so status/scope checks do not depend on SYS flows.</summary>
    private sealed class FakeWorkflow : IApprovalWorkflowService
    {
        private static long next = 2_000_000;

        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(Interlocked.Increment(ref next), submission.DocumentType,
                submission.DocumentId, ApprovalStatus.Pending, 1, 1));

        public Task<ApprovalState> CancelAsync(string documentType, long documentId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId,
            CancellationToken cancellationToken = default) => Task.FromResult<ApprovalState?>(null);
    }
}
