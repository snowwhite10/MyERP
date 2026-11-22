using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// Applies the final SYS decision to a Pending order in the approval transaction (WU-SD-003b1, ADR-142).
/// Approval takes the FI credit key, then the order row, and rechecks credit with fresh FI and SD figures; a refusal
/// throws so SYS rolls back its decision and the order stays Pending. Rejection needs no credit and releases nothing,
/// because Pending orders never reserve credit (BR-SD-032).
/// WU-SD-006b2 (ADR-171, BR-SD-034/036): an over-limit approval passes only with the approver's own
/// <see cref="ApprovalCompleted.OverrideReason"/> and a <c>SAL.CREDIT.OVERRIDE</c> grant whose data scope covers the
/// order; the submit-round override never carries over. The append-only <c>FinalApproval</c> evidence row binds the
/// decision to the Pending order version and the SYS round, and rolls back with the SYS decision.
/// The credit gate is resolved only when a decision arrives: it depends on the MD customer service, which itself
/// depends on the SYS workflow that enumerates every result handler, so constructor injection would form a DI cycle
/// that hangs <c>IApprovalWorkflowService</c> resolution (found in WU-SD-003b1).
/// </summary>
public sealed class SalesOrderApprovalResultHandler(MyErpDbContext db, Func<SalesOrderCreditCheck> creditFactory,
    SalesActionAccess access, IClock clock)
    : IApprovalResultHandler
{
    public const string OverrideReasonField = "overrideReason";

    public string DocumentType => SalesOrderSubmissionService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var credit = creditFactory();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A caller-owned approval transaction is required.");

        var header = await db.Set<SalesOrder>().AsNoTracking().Where(x => x.Id == result.DocumentId)
            .Select(x => new { x.CompanyId, x.CustomerId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound(result.DocumentId);
        if (result.Status == ApprovalStatus.Approved)
            await credit.LockAsync(header.CompanyId, header.CustomerId, transaction.GetDbTransaction(),
                cancellationToken);
        await SalesOrderLocks.LockOrderAsync(db, header.CompanyId, result.DocumentId, cancellationToken);

        var order = await db.Set<SalesOrder>().SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw NotFound(result.DocumentId);
        if (order.Status != SalesOrderStatus.Pending || order.ApprovalInstanceId != result.InstanceId ||
            order.CustomerId != header.CustomerId)
            throw new ConflictException("SAL.ORDER.APPROVAL_STATE_CHANGED",
                "Order is no longer waiting for this approval instance.");

        if (result.Status == ApprovalStatus.Approved)
        {
            var reason = result.OverrideReason;
            var hasGrant = reason is not null &&
                await access.AllowsAsync(SalesOrderSubmissionService.OverridePermission, order, cancellationToken);
            var decision = await credit.DecideAsync(db, order, transaction.GetDbTransaction(), reason, hasGrant,
                cancellationToken, OverrideReasonField);
            if (decision.IsOverride)
                db.Add(SalesOrderSubmissionService.Evidence(order, order.Version, result.InstanceId, decision,
                    SalesCreditDecisionStage.FinalApproval));
            order.Status = SalesOrderLifecycle.Next(order.Status, SalesOrderAction.Approve, false);
            order.ApprovedAt = clock.LocalNow;
        }
        else
        {
            order.Status = SalesOrderLifecycle.Next(order.Status, SalesOrderAction.Reject, false);
        }
    }

    private static NotFoundException NotFound(long id) => new("SAL.ORDER.NOT_FOUND", "Sales order", id);
}
