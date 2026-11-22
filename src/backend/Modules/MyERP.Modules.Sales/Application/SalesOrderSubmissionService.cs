using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesOrderSubmissionResult(SalesOrder Order, ApprovalState Approval,
    SalesCreditAssessment Credit, SalesCreditOverride? Override = null);

/// <summary>
/// REQ_F4112.2.4 / BR-SD-016/018/033 (WU-SD-003b1, ADR-142): submits one saved Draft order to SYS approval.
/// Read Committed lock order: FI credit key (company, customer) → order primary key. The customer read before the
/// FI lock is verified again after the order lock; a concurrent customer change returns 409. The credit gate runs
/// before SYS is called, so a refusal leaves no SYS round; zero-step approval is saved in the same transaction.
/// WU-SD-006b1 (BR-SD-034/036): an over-limit order passes only with a 1–200 character reason and
/// <c>SAL.CREDIT.OVERRIDE</c> whose data scope covers the order; the append-only <see cref="SalesCreditOverride"/>
/// row binds the decision to the submitted version and the new SYS round inside the same transaction.
/// </summary>
public sealed class SalesOrderSubmissionService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesOrderCreditCheck credit, IApprovalWorkflowService workflow, IClock clock)
{
    public const string DocumentType = "SAL.ORDER";
    public const string Permission = "SAL.ORDER.SUBMIT";
    public const string OverridePermission = "SAL.CREDIT.OVERRIDE";

    public Task<SalesOrderSubmissionResult> SubmitAsync(long id, int version, CancellationToken token = default) =>
        SubmitAsync(id, version, null, token);

    public async Task<SalesOrderSubmissionResult> SubmitAsync(long id, int version, string? creditOverrideReason,
        CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync(Permission, token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var customerId = await SalesOrderLocks.ReadCustomerAsync(db, companyId, id, token)
                ?? throw NotFound(id);
            await credit.LockAsync(companyId, customerId, transaction.GetDbTransaction(), token);
            await SalesOrderLocks.LockOrderAsync(db, companyId, id, token);

            var order = await scope.Apply(db.Set<SalesOrder>()).SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw NotFound(id);
            if (version < 1 || order.Version != version || order.CustomerId != customerId)
                throw new ConflictException();
            SalesOrderLifecycle.Next(order.Status, SalesOrderAction.SubmitPending, false);

            var hasGrant = creditOverrideReason is not null &&
                await access.AllowsAsync(OverridePermission, order, token);
            var decision = await credit.DecideAsync(db, order, transaction.GetDbTransaction(), creditOverrideReason,
                hasGrant, token);
            var approval = await workflow.SubmitAsync(new ApprovalSubmission(DocumentType, order.Id,
                order.DocumentNo, order.BaseGross,
                order.Remark is { Length: > 200 } ? order.Remark[..200] : order.Remark), token);
            if (approval.DocumentType != DocumentType || approval.DocumentId != order.Id ||
                approval.InstanceId <= 0 || approval.Status is not (ApprovalStatus.Pending or ApprovalStatus.Approved))
                throw new ConflictException("SAL.ORDER.APPROVAL_RESULT_INVALID",
                    "SYS returned an invalid order approval state.");

            order.ApprovalInstanceId = approval.InstanceId;
            order.Status = SalesOrderLifecycle.Next(SalesOrderStatus.Draft,
                approval.Status == ApprovalStatus.Approved
                    ? SalesOrderAction.SubmitApproved
                    : SalesOrderAction.SubmitPending, false);
            order.ApprovedAt = order.Status == SalesOrderStatus.Approved ? clock.LocalNow : null;
            var evidence = decision.IsOverride ? Evidence(order, version, approval.InstanceId, decision) : null;
            if (evidence is not null) db.Add(evidence);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            db.ChangeTracker.Clear();
            var saved = await db.Set<SalesOrder>().AsNoTracking().Include(x => x.Lines)
                .SingleAsync(x => x.Id == id, token);
            return new SalesOrderSubmissionResult(saved, approval, decision.Assessment, evidence);
        }
        catch (Exception ex)
        {
            if (db.Database.CurrentTransaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    internal static SalesCreditOverride Evidence(SalesOrder order, int version, long instanceId,
        SalesCreditDecision decision, SalesCreditDecisionStage stage = SalesCreditDecisionStage.Submit)
    {
        var a = decision.Assessment;
        return new SalesCreditOverride
        {
            CompanyId = order.CompanyId, OrderId = order.Id, OrderVersion = version, ApprovalInstanceId = instanceId,
            Stage = stage, EvaluationDate = a.EvaluationDate,
            CreditLimitVersion = a.LimitVersion!.Value, BaseCurrencyCode = a.BaseCurrency!, CreditLimit = a.Limit!.Value,
            PostedOpenAr = a.PostedOpenAr!.Value, OtherCommitments = a.OtherCommitments!.Value,
            Candidate = a.Candidate, Excess = a.Excess!.Value, Reason = decision.OverrideReason!
        };
    }

    private static NotFoundException NotFound(long id) => new("SAL.ORDER.NOT_FOUND", "Sales order", id);
}

/// <summary>Shared row-lock statements for order commands that must follow the FI credit key (ADR-142).</summary>
internal static class SalesOrderLocks
{
    /// <summary>Unlocked customer read used only to choose the FI credit key; callers recheck it after locking.</summary>
    public static async Task<long?> ReadCustomerAsync(MyErpDbContext db, long companyId, long id,
        CancellationToken token) =>
        (await db.Database.SqlQueryRaw<long>(
            "SELECT [customer_id] AS [Value] FROM dbo.sal_order WITH (FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, companyId).ToListAsync(token)).Cast<long?>().SingleOrDefault();

    public static Task LockOrderAsync(MyErpDbContext db, long companyId, long id, CancellationToken token) =>
        db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1}",
            id, companyId).ToListAsync(token);
}
