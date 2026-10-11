using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.IntegrationTests.Database;

/// <summary>SYS-APR-001～009：審批流程引擎的多級、金額門檻、代理、自審、併發與結果分派（myerp_test）。</summary>
public sealed class ApprovalWorkflowTests(TestDatabase database)
{
    private static readonly FixedClock Clock = new(new DateTime(2026, 10, 11, 9, 30, 0));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AmountThresholdsDecideTheStepsAndTheLastApprovalCompletesTheDocument()
    {
        var org = await CreateOrgAsync();
        await CreateFlowAsync(org.CompanyId, "TST.ORDER",
            new ApprovalStepInput("Supervisor", ApproverType.Role, org.SupervisorRole, null, null),
            new ApprovalStepInput("Manager", ApproverType.User, null, org.Manager, 10_000m),
            new ApprovalStepInput("General manager", ApproverType.Role, org.GmRole, null, 100_000m));
        var handler = new RecordingHandler("TST.ORDER");

        await using var db = Context(org.CompanyId);
        var small = await Engine(db, org.Submitter, handler).SubmitAsync(new ApprovalSubmission("tst.order", 1, "SO-001", 5_000m), Ct);
        Assert.Equal((ApprovalStatus.Pending, 1, 1), (small.Status, small.CurrentStepNo, small.StepCount));
        Assert.Equal(2, (await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.ORDER", 2, "SO-002", 10_000m), Ct)).StepCount);
        var large = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.ORDER", 3, "SO-003", 250_000m, " Big order "), Ct);
        Assert.Equal(3, large.StepCount);

        // 角色步驟：任一擁有該角色的用戶可處理；步驟依序推進
        var first = await Engine(db, org.Supervisor, handler).ApproveAsync(large.InstanceId, new ApprovalDecision(" ok ", 1), Ct);
        Assert.Equal(ApprovalStatus.Pending, first.Status);
        Assert.Equal(2, first.CurrentStepNo);
        Assert.Equal("Big order", first.Summary);
        Assert.Equal([ApprovalStepStatus.Approved, ApprovalStepStatus.Pending, ApprovalStepStatus.Waiting], first.Steps.Select(s => s.Status));
        Assert.Equal((org.Supervisor, (long?)null, "ok"), (first.Steps[0].ActedBy!.Value, first.Steps[0].OnBehalfOfUserId, first.Steps[0].Comment));
        Assert.Equal(Clock.LocalNow, first.Steps[0].ActedAt);
        Assert.Equal("Supervisor role", first.Steps[0].RoleName);
        Assert.Empty(handler.Results);

        // 下一步是指定用戶：主管不能處理
        await Assert.ThrowsAsync<ForbiddenException>(() => Engine(db, org.Supervisor).ApproveAsync(large.InstanceId, new ApprovalDecision(null, first.Version), Ct));
        var second = await Engine(db, org.Manager, handler).ApproveAsync(large.InstanceId, new ApprovalDecision(null, first.Version), Ct);
        var done = await Engine(db, org.Gm, handler).ApproveAsync(large.InstanceId, new ApprovalDecision("Approved", second.Version), Ct);

        Assert.Equal(ApprovalStatus.Approved, done.Status);
        Assert.Null(done.CurrentStepNo);
        Assert.Equal(Clock.LocalNow, done.CompletedAt);
        Assert.All(done.Steps, s => Assert.Equal(ApprovalStepStatus.Approved, s.Status));
        var result = Assert.Single(handler.Results);
        Assert.Equal(new ApprovalCompleted(large.InstanceId, "TST.ORDER", 3, ApprovalStatus.Approved, org.Gm, "Approved"), result);
        var state = await Engine(db, org.Submitter).GetStateAsync("TST.ORDER", 3, Ct);
        Assert.Equal(ApprovalStatus.Approved, state!.Status);
        Assert.Null(await Engine(db, org.Submitter).GetStateAsync("TST.ORDER", 99, Ct));
        var completed = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Engine(db, org.Gm).ApproveAsync(large.InstanceId, new ApprovalDecision(null, done.Version), Ct));
        Assert.Equal(ApprovalWorkflowService.NotPending, completed.ErrorCode);
    }

    [Fact]
    public async Task RejectionNeedsACommentSkipsTheRemainingStepsAndAllowsResubmission()
    {
        var org = await CreateOrgAsync();
        await CreateFlowAsync(org.CompanyId, "TST.REJECT",
            new ApprovalStepInput("Supervisor", ApproverType.Role, org.SupervisorRole, null, null),
            new ApprovalStepInput("Manager", ApproverType.User, null, org.Manager, null));
        var handler = new RecordingHandler("TST.REJECT");
        await using var db = Context(org.CompanyId);
        var submitted = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.REJECT", 7, "PO-7", 100m), Ct);

        var missing = await Assert.ThrowsAsync<RequestValidationException>(() =>
            Engine(db, org.Supervisor, handler).RejectAsync(submitted.InstanceId, new ApprovalDecision("  ", 1), Ct));
        Assert.Contains("comment", missing.Errors.Keys);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.REJECT", 7, "PO-7", 100m), Ct));
        Assert.Equal(ApprovalWorkflowService.AlreadyPending, duplicate.ErrorCode);

        var rejected = await Engine(db, org.Supervisor, handler).RejectAsync(submitted.InstanceId, new ApprovalDecision("Price too high", 1), Ct);
        Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
        Assert.Equal([ApprovalStepStatus.Rejected, ApprovalStepStatus.Skipped], rejected.Steps.Select(s => s.Status));
        Assert.Equal(ApprovalStatus.Rejected, Assert.Single(handler.Results).Status);

        // 駁回後可再送審，建立新實例並保留歷史
        var again = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.REJECT", 7, "PO-7", 80m), Ct);
        Assert.NotEqual(submitted.InstanceId, again.InstanceId);
        Assert.Equal(again.InstanceId, (await Engine(db, org.Submitter).GetStateAsync("TST.REJECT", 7, Ct))!.InstanceId);
        var history = await Engine(db, org.Submitter).ListAsync(new ApprovalInstanceQuery { DocumentType = "tst.reject", DocumentId = 7 }, Ct);
        Assert.Equal([again.InstanceId, submitted.InstanceId], history.Items.Select(x => x.Id));
        Assert.Equal(1, (await Engine(db, org.Submitter).ListAsync(new ApprovalInstanceQuery { Status = ApprovalStatus.Rejected, Keyword = "PO-7" }, Ct)).TotalCount);

        // 撤回：不呼叫結果處理器，步驟全部略過
        var cancelled = await Engine(db, org.Submitter, handler).CancelAsync("TST.REJECT", 7, Ct);
        Assert.Equal(ApprovalStatus.Cancelled, cancelled.Status);
        Assert.Single(handler.Results);
        var detail = await Engine(db, org.Submitter).GetAsync(again.InstanceId, Ct);
        Assert.All(detail.Steps, s => Assert.Equal(ApprovalStepStatus.Skipped, s.Status));
        var nothing = await Assert.ThrowsAsync<BusinessRuleException>(() => Engine(db, org.Submitter).CancelAsync("TST.REJECT", 7, Ct));
        Assert.Equal(ApprovalWorkflowService.NotPending, nothing.ErrorCode);
    }

    [Fact]
    public async Task DocumentsWithoutAnApplicableStepAreApprovedAtOnce()
    {
        var org = await CreateOrgAsync();
        var flowId = await CreateFlowAsync(org.CompanyId, "TST.THRESHOLD",
            new ApprovalStepInput("Large only", ApproverType.User, null, org.Manager, 1_000m));
        var handler = new RecordingHandler("TST.THRESHOLD");
        await using var db = Context(org.CompanyId);

        var noFlow = await Engine(db, org.Submitter, handler).SubmitAsync(new ApprovalSubmission("TST.NOFLOW", 1, "X-1", 9_999m), Ct);
        Assert.Equal((ApprovalStatus.Approved, (int?)null, 0), (noFlow.Status, noFlow.CurrentStepNo, noFlow.StepCount));
        var belowThreshold = await Engine(db, org.Submitter, handler).SubmitAsync(new ApprovalSubmission("TST.THRESHOLD", 1, "T-1", 999.9999m), Ct);
        Assert.Equal(ApprovalStatus.Approved, belowThreshold.Status);
        Assert.Equal(ApprovalStatus.Pending, (await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.THRESHOLD", 2, "T-2", 1_000m), Ct)).Status);

        // 停用流程：送審即自動核准；審批中的實例仍按快照進行
        var flows = new ApprovalFlowService(db);
        var flow = await flows.GetAsync(flowId, Ct);
        await flows.UpdateAsync(flowId, new ApprovalFlowUpdate(flow.Name, false, false,
            [new ApprovalStepInput("Large only", ApproverType.User, null, org.Manager, 1_000m)], flow.Version), Ct);
        Assert.Equal(ApprovalStatus.Approved, (await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.THRESHOLD", 3, "T-3", 5_000m), Ct)).Status);
        Assert.Empty(handler.Results);
        var pending = await Engine(db, org.Manager).ListTasksAsync(new ApprovalTaskQuery(), Ct);
        Assert.Equal(["T-2"], pending.Items.Select(t => t.DocumentNo));

        foreach (var (submission, field) in new[]
        {
            (new ApprovalSubmission("TST.THRESHOLD", 0, "T", 1m), "documentId"),
            (new ApprovalSubmission("TST.THRESHOLD", 9, " ", 1m), "documentNo"),
            (new ApprovalSubmission("TST.THRESHOLD", 9, new string('N', 31), 1m), "documentNo"),
            (new ApprovalSubmission("TST.THRESHOLD", 9, "T", -1m), "amount"),
            (new ApprovalSubmission("TST.THRESHOLD", 9, "T", 0.00001m), "amount"),
            (new ApprovalSubmission("TST.THRESHOLD", 9, "T", 1m, new string('S', 201)), "summary"),
            (new ApprovalSubmission("THRESHOLD", 9, "T", 1m), "documentType"),
        })
        {
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => Engine(db, org.Submitter).SubmitAsync(submission, Ct));
            Assert.Contains(field, error.Errors.Keys);
        }

        await Assert.ThrowsAsync<UnauthenticatedException>(() => Engine(db, null).SubmitAsync(new ApprovalSubmission("TST.THRESHOLD", 9, "T", 1m), Ct));
    }

    [Fact]
    public async Task SubmittersCannotApproveTheirOwnDocumentsUnlessTheFlowAllowsIt()
    {
        var org = await CreateOrgAsync();
        // 送審人本身也擁有主管角色
        await AddRoleAsync(org.CompanyId, org.Submitter, org.SupervisorRole);
        await CreateFlowAsync(org.CompanyId, "TST.STRICT", new ApprovalStepInput("Supervisor", ApproverType.Role, org.SupervisorRole, null, null));
        await CreateFlowAsync(org.CompanyId, "TST.RELAXED", [new ApprovalStepInput("Supervisor", ApproverType.Role, org.SupervisorRole, null, null)], allowSelfApproval: true);
        await using var db = Context(org.CompanyId);
        var strict = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.STRICT", 1, "S-1", 1m), Ct);
        var relaxed = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.RELAXED", 1, "R-1", 1m), Ct);

        Assert.Equal(["R-1"], (await Engine(db, org.Submitter).ListTasksAsync(new ApprovalTaskQuery(), Ct)).Items.Select(t => t.DocumentNo));
        var self = await Assert.ThrowsAsync<BusinessRuleException>(() => Engine(db, org.Submitter).ApproveAsync(strict.InstanceId, new ApprovalDecision(null, 1), Ct));
        Assert.Equal(ApprovalWorkflowService.SelfApproval, self.ErrorCode);
        Assert.Equal(ApprovalStatus.Approved, (await Engine(db, org.Submitter).ApproveAsync(relaxed.InstanceId, new ApprovalDecision(null, 1), Ct)).Status);

        // 非審批人、過期版本、不存在的實例
        var outsider = await Assert.ThrowsAsync<ForbiddenException>(() => Engine(db, org.Outsider).ApproveAsync(strict.InstanceId, new ApprovalDecision(null, 1), Ct));
        Assert.Equal(ApprovalWorkflowService.NotApprover, outsider.ErrorCode);
        Assert.Empty((await Engine(db, org.Outsider).ListTasksAsync(new ApprovalTaskQuery(), Ct)).Items);
        await Assert.ThrowsAsync<ConflictException>(() => Engine(db, org.Supervisor).ApproveAsync(strict.InstanceId, new ApprovalDecision(null, 9), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Engine(db, org.Supervisor).ApproveAsync(long.MaxValue, new ApprovalDecision(null, 1), Ct));

        // 停用角色後不可再處理該角色步驟
        await using (var admin = Context(null))
        {
            var role = await admin.Set<SystemRole>().SingleAsync(r => r.Id == org.SupervisorRole, Ct);
            role.IsActive = false;
            await admin.SaveChangesAsync(Ct);
        }

        Assert.Empty((await Engine(db, org.Supervisor).ListTasksAsync(new ApprovalTaskQuery(), Ct)).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => Engine(db, org.Supervisor).ApproveAsync(strict.InstanceId, new ApprovalDecision(null, 1), Ct));
    }

    [Fact]
    public async Task DelegatesActOnBehalfOfAbsentApproversWithinTheDelegationPeriod()
    {
        var org = await CreateOrgAsync();
        await CreateFlowAsync(org.CompanyId, "TST.DELEGATE",
            new ApprovalStepInput("Manager", ApproverType.User, null, org.Manager, null),
            new ApprovalStepInput("General manager", ApproverType.Role, org.GmRole, null, null));
        await using var db = Context(org.CompanyId);
        var delegations = new ApprovalDelegationService(db);
        var managerAway = await delegations.CreateAsync(new ApprovalDelegationInput(org.Manager, org.Outsider, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), " Leave "), Ct);
        Assert.Equal("Leave", managerAway.Remark);
        Assert.False(string.IsNullOrEmpty(managerAway.DelegatorUsername));
        await delegations.CreateAsync(new ApprovalDelegationInput(org.Gm, org.Outsider, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 11), null), Ct);
        var submitted = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.DELEGATE", 1, "D-1", 1m), Ct);

        // 代理人的待辦包含被代理人的用戶步驟，並標示被代理人
        var task = Assert.Single((await Engine(db, org.Outsider).ListTasksAsync(new ApprovalTaskQuery { DocumentType = "TST.DELEGATE" }, Ct)).Items);
        Assert.Equal((org.Manager, 1, 2, "Manager"), (task.OnBehalfOfUserId!.Value, task.StepNo, task.StepCount, task.StepName));
        Assert.Equal(managerAway.DelegatorUsername, task.OnBehalfOfUsername);

        // 代理期間以外不可處理
        var later = new ApprovalWorkflowService(db, new FixedClock(new DateTime(2026, 10, 13, 9, 0, 0)), new FixedUser(org.Outsider), new AuditTrail(), []);
        await Assert.ThrowsAsync<ForbiddenException>(() => later.ApproveAsync(submitted.InstanceId, new ApprovalDecision(null, 1), Ct));
        Assert.Empty((await later.ListTasksAsync(new ApprovalTaskQuery(), Ct)).Items);

        var first = await Engine(db, org.Outsider).ApproveAsync(submitted.InstanceId, new ApprovalDecision(null, 1), Ct);
        Assert.Equal((org.Outsider, org.Manager), (first.Steps[0].ActedBy!.Value, first.Steps[0].OnBehalfOfUserId!.Value));
        Assert.Equal(first.Steps[0].OnBehalfOfUsername, managerAway.DelegatorUsername);
        // 角色步驟經代理：被代理人擁有該角色
        var done = await Engine(db, org.Outsider).ApproveAsync(submitted.InstanceId, new ApprovalDecision(null, first.Version), Ct);
        Assert.Equal(ApprovalStatus.Approved, done.Status);
        Assert.Equal(org.Gm, done.Steps[1].OnBehalfOfUserId);

        // 重疊期間、停用後可重新建立、驗證
        var overlap = await Assert.ThrowsAsync<ConflictException>(() => delegations.CreateAsync(
            new ApprovalDelegationInput(org.Manager, org.Supervisor, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 20), null), Ct));
        Assert.Equal(ApprovalDelegationService.Overlap, overlap.ErrorCode);
        var disabled = await delegations.UpdateAsync(managerAway.Id, new ApprovalDelegationUpdate(managerAway.StartDate, managerAway.EndDate, null, false, managerAway.Version), Ct);
        Assert.False(disabled.IsActive);
        Assert.Null(disabled.Remark);
        await delegations.CreateAsync(new ApprovalDelegationInput(org.Manager, org.Supervisor, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 20), null), Ct);
        await Assert.ThrowsAsync<ConflictException>(() => delegations.UpdateAsync(managerAway.Id,
            new ApprovalDelegationUpdate(managerAway.StartDate, managerAway.EndDate, null, true, disabled.Version), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => delegations.UpdateAsync(managerAway.Id,
            new ApprovalDelegationUpdate(managerAway.StartDate, managerAway.EndDate, null, false, managerAway.Version), Ct));
        Assert.Equal(2, (await delegations.ListAsync(new ApprovalDelegationQuery { UserId = org.Manager }, Ct)).TotalCount);
        Assert.Single((await delegations.ListAsync(new ApprovalDelegationQuery { ActiveOn = new DateOnly(2026, 10, 15) }, Ct)).Items);

        foreach (var (input, field) in new[]
        {
            (new ApprovalDelegationInput(org.Manager, org.Manager, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null), "delegateUserId"),
            (new ApprovalDelegationInput(org.Manager, org.Gm, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 1), null), "endDate"),
            (new ApprovalDelegationInput(org.Manager, org.Stranger, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null), "delegateUserId"),
            (new ApprovalDelegationInput(org.Stranger, org.Gm, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), null), "delegatorUserId"),
            (new ApprovalDelegationInput(org.Manager, org.Gm, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), new string('R', 201)), "remark"),
        })
        {
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => delegations.CreateAsync(input, Ct));
            Assert.Contains(field, error.Errors.Keys);
        }
    }

    [Fact]
    public async Task AFailingResultHandlerRollsBackTheWholeApproval()
    {
        var org = await CreateOrgAsync();
        await CreateFlowAsync(org.CompanyId, "TST.ATOMIC", new ApprovalStepInput("Manager", ApproverType.User, null, org.Manager, null));
        await using var db = Context(org.CompanyId);
        var submitted = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.ATOMIC", 1, "A-1", 1m), Ct);

        await using (var failing = Context(org.CompanyId))
        {
            var handler = new FailingHandler(failing, "TST.ATOMIC");
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Engine(failing, org.Manager, handler).ApproveAsync(submitted.InstanceId, new ApprovalDecision(null, 1), Ct));
            Assert.Equal("TST.HANDLER.FAILED", error.ErrorCode);
        }

        await using var check = Context(org.CompanyId);
        Assert.False(await check.Set<NumberRule>().AnyAsync(r => r.DocumentType == "TST.ATOMIC", Ct));
        var detail = await Engine(check, org.Manager).GetAsync(submitted.InstanceId, Ct);
        Assert.Equal((ApprovalStatus.Pending, ApprovalStepStatus.Pending, 1), (detail.Status, detail.Steps[0].Status, detail.Version));
        Assert.Equal(ApprovalStatus.Approved, (await Engine(check, org.Manager).ApproveAsync(submitted.InstanceId, new ApprovalDecision(null, 1), Ct)).Status);
    }

    [Fact]
    public async Task ConcurrentSubmissionsAndDecisionsLetOnlyOneWin()
    {
        var org = await CreateOrgAsync();
        await AddRoleAsync(org.CompanyId, org.Manager, org.SupervisorRole);
        await CreateFlowAsync(org.CompanyId, "TST.RACE", new ApprovalStepInput("Supervisor", ApproverType.Role, org.SupervisorRole, null, null));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = Context(org.CompanyId);
            try
            {
                return (await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.RACE", 1, "R-1", 1m), Ct)).InstanceId;
            }
            catch (ConflictException ex) when (ex.ErrorCode == ApprovalWorkflowService.AlreadyPending)
            {
                return 0L;
            }
        }));
        var instanceId = Assert.Single(outcomes, id => id != 0);

        // 兩位審批人同時處理同一步驟：只有一位成功，另一位得到 409
        var decisions = await Task.WhenAll(new[] { org.Supervisor, org.Manager }.Select(async approver =>
        {
            await using var db = Context(org.CompanyId);
            try
            {
                await Engine(db, approver).ApproveAsync(instanceId, new ApprovalDecision(null, 1), Ct);
                return true;
            }
            catch (ErpException ex) when (ex is ConflictException || ex.ErrorCode == ApprovalWorkflowService.NotPending)
            {
                // 讀到舊版本時為 409；讀取時對方已提交則為已完成
                return false;
            }
        }));
        Assert.Single(decisions, ok => ok);
        await using var check = Context(org.CompanyId);
        Assert.Equal(1, await check.Set<ApprovalInstance>().CountAsync(Ct));
        Assert.Equal(ApprovalStatus.Approved, (await Engine(check, org.Submitter).GetStateAsync("TST.RACE", 1, Ct))!.Status);
    }

    [Fact]
    public async Task FlowMaintenanceValidatesStepsAndKeepsPendingSnapshots()
    {
        var org = await CreateOrgAsync();
        var other = await CreateOrgAsync();
        await using var db = Context(org.CompanyId);
        var flows = new ApprovalFlowService(db);
        var created = await flows.CreateAsync(new ApprovalFlowInput(" tst.flow ", " Flow ", false,
            [new ApprovalStepInput(" First ", ApproverType.Role, org.SupervisorRole, null, null), new ApprovalStepInput("Second", ApproverType.User, null, org.Manager, 500.5m)]), Ct);
        Assert.Equal(("TST.FLOW", "Flow", 1), (created.DocumentType, created.Name, created.Version));
        Assert.Equal([(1, "First"), (2, "Second")], created.Steps.Select(s => (s.StepNo, s.Name)));
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => flows.CreateAsync(new ApprovalFlowInput("TST.FLOW", "Again", false,
            [new ApprovalStepInput("Only", ApproverType.User, null, org.Manager, null)]), Ct));
        Assert.Equal(ApprovalFlowService.DuplicateType, duplicate.ErrorCode);
        var submitted = await Engine(db, org.Submitter).SubmitAsync(new ApprovalSubmission("TST.FLOW", 1, "F-1", 1_000m), Ct);

        // 整組取代步驟；只改步驟也遞增版本
        var updated = await flows.UpdateAsync(created.Id, new ApprovalFlowUpdate("Flow", false, true,
            [new ApprovalStepInput("GM only", ApproverType.Role, org.GmRole, null, null)], created.Version), Ct);
        Assert.Equal(2, updated.Version);
        Assert.Equal(["GM only"], updated.Steps.Select(s => s.Name));
        Assert.Equal(["GM only"], (await flows.GetAsync(created.Id, Ct)).Steps.Select(s => s.Name));
        await Assert.ThrowsAsync<ConflictException>(() => flows.UpdateAsync(created.Id, new ApprovalFlowUpdate("Flow", false, true,
            [new ApprovalStepInput("Stale", ApproverType.Role, org.GmRole, null, null)], created.Version), Ct));
        var snapshot = await Engine(db, org.Submitter).GetAsync(submitted.InstanceId, Ct);
        Assert.Equal(["First", "Second"], snapshot.Steps.Select(s => s.Name));
        Assert.Equal("First", Assert.Single((await Engine(db, org.Supervisor).ListTasksAsync(new ApprovalTaskQuery(), Ct)).Items).StepName);
        Assert.Empty((await Engine(db, org.Gm).ListTasksAsync(new ApprovalTaskQuery(), Ct)).Items);

        Assert.Equal(1, (await flows.ListAsync(new PagedRequest { Keyword = "flow" }, Ct)).TotalCount);
        await Assert.ThrowsAsync<RequestValidationException>(() => flows.ListAsync(new PagedRequest { Sort = "steps" }, Ct));

        var stranger = other.Manager;
        foreach (var (steps, field) in new (ApprovalStepInput[], string)[]
        {
            ([], "steps"),
            ([.. Enumerable.Range(0, 11).Select(i => new ApprovalStepInput($"S{i}", ApproverType.User, null, org.Manager, null))], "steps"),
            ([new ApprovalStepInput(" ", ApproverType.User, null, org.Manager, null)], "steps[0].name"),
            ([new ApprovalStepInput("R", ApproverType.Role, null, null, null)], "steps[0].roleId"),
            ([new ApprovalStepInput("R", ApproverType.Role, org.SupervisorRole, org.Manager, null)], "steps[0].roleId"),
            ([new ApprovalStepInput("R", ApproverType.Role, long.MaxValue, null, null)], "steps[0].roleId"),
            ([new ApprovalStepInput("U", ApproverType.User, null, null, null)], "steps[0].userId"),
            ([new ApprovalStepInput("U", ApproverType.User, null, stranger, null)], "steps[0].userId"),
            ([new ApprovalStepInput("U", ApproverType.User, null, org.Manager, -1m)], "steps[0].minAmount"),
            ([new ApprovalStepInput("U", ApproverType.User, null, org.Manager, 0.00001m)], "steps[0].minAmount"),
            ([new ApprovalStepInput("U", (ApproverType)9, null, org.Manager, null)], "steps[0].approverType"),
            // 角色與用戶以批次查詢確認，錯誤仍指向第一個無效步驟
            ([
                new ApprovalStepInput("OK role", ApproverType.Role, org.SupervisorRole, null, null),
                new ApprovalStepInput("OK user", ApproverType.User, null, org.Manager, null),
                new ApprovalStepInput("Bad role", ApproverType.Role, long.MaxValue, null, null),
            ], "steps[2].roleId"),
            ([
                new ApprovalStepInput("OK user", ApproverType.User, null, org.Manager, null),
                new ApprovalStepInput("OK role", ApproverType.Role, org.GmRole, null, null),
                new ApprovalStepInput("Stranger", ApproverType.User, null, stranger, null),
            ], "steps[2].userId"),
        })
        {
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => flows.CreateAsync(new ApprovalFlowInput("TST.BAD", "Bad", false, steps), Ct));
            Assert.Contains(field, error.Errors.Keys);
        }

        // 他公司看不到本公司的流程與實例
        await using var otherDb = Context(other.CompanyId);
        await Assert.ThrowsAsync<NotFoundException>(() => new ApprovalFlowService(otherDb).GetAsync(created.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Engine(otherDb, other.Manager).GetAsync(submitted.InstanceId, Ct));
        Assert.Equal(0, (await Engine(otherDb, other.Manager).ListAsync(new ApprovalInstanceQuery(), Ct)).TotalCount);
        await using var none = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ApprovalFlowService(none).ListAsync(new PagedRequest(), Ct));
    }

    private sealed record Org(
        long CompanyId, long SupervisorRole, long GmRole, long Submitter, long Supervisor, long Manager, long Gm, long Outsider, long Stranger);

    private sealed class RecordingHandler(string documentType) : IApprovalResultHandler
    {
        public List<ApprovalCompleted> Results { get; } = [];

        public string DocumentType => documentType;

        public Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
        {
            Results.Add(result);
            return Task.CompletedTask;
        }
    }

    /// <summary>先在同一 DbContext 寫入資料並儲存，再拋出例外：驗證兩者都隨交易回滾。</summary>
    private sealed class FailingHandler(MyErpDbContext db, string documentType) : IApprovalResultHandler
    {
        public string DocumentType => documentType;

        public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
        {
            db.Add(new NumberRule
            {
                CompanyId = db.CurrentCompanyId!.Value, DocumentType = documentType, Name = "Side effect", Prefix = "X",
                DateFormat = NumberDateFormat.None, SequenceLength = 4, ResetPeriod = NumberResetPeriod.Never,
            });
            await db.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException("TST.HANDLER.FAILED", "The document module refused the result.");
        }
    }

    private static ApprovalWorkflowService Engine(MyErpDbContext db, long? userId, params IApprovalResultHandler[] handlers) =>
        new(db, Clock, new FixedUser(userId), new AuditTrail(), handlers);

    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(Clock, new FixedUser(null), company);
        return new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new SystemOrganizationDbModel()], company);
    }

    private async Task<long> CreateFlowAsync(long companyId, string documentType, params ApprovalStepInput[] steps) =>
        await CreateFlowAsync(companyId, documentType, steps, false);

    private async Task<long> CreateFlowAsync(long companyId, string documentType, ApprovalStepInput[] steps, bool allowSelfApproval)
    {
        await using var db = Context(companyId);
        return (await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput(documentType, documentType, allowSelfApproval, steps), Ct)).Id;
    }

    /// <summary>一間公司、兩個角色與六位用戶：Stranger 屬於另一間公司，其餘都是本公司的啟用用戶。</summary>
    private async Task<Org> CreateOrgAsync()
    {
        await using var db = Context(null);
        var company = new SystemCompany { Code = "A" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Approval" };
        var elsewhere = new SystemCompany { Code = "A" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Elsewhere" };
        var supervisorRole = new SystemRole { Code = "R" + Guid.NewGuid().ToString("N")[..12], Name = "Supervisor role" };
        var gmRole = new SystemRole { Code = "R" + Guid.NewGuid().ToString("N")[..12], Name = "GM role" };
        db.AddRange(company, elsewhere, supervisorRole, gmRole);
        await db.SaveChangesAsync(Ct);

        async Task<long> User(long companyId, params long[] roles)
        {
            var name = "apr" + Guid.NewGuid().ToString("N")[..16];
            var user = new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
            db.Add(user);
            await db.SaveChangesAsync(Ct);
            db.Add(new SystemUserCompany { UserId = user.Id, CompanyId = companyId });
            db.AddRange(roles.Select(r => new SystemUserRole { UserId = user.Id, CompanyId = companyId, RoleId = r }));
            await db.SaveChangesAsync(Ct);
            return user.Id;
        }

        return new Org(company.Id, supervisorRole.Id, gmRole.Id,
            Submitter: await User(company.Id),
            Supervisor: await User(company.Id, supervisorRole.Id),
            Manager: await User(company.Id),
            Gm: await User(company.Id, gmRole.Id),
            Outsider: await User(company.Id),
            Stranger: await User(elsewhere.Id, supervisorRole.Id));
    }

    private async Task AddRoleAsync(long companyId, long userId, long roleId)
    {
        await using var db = Context(null);
        db.Add(new SystemUserRole { UserId = userId, CompanyId = companyId, RoleId = roleId });
        await db.SaveChangesAsync(Ct);
    }
}
