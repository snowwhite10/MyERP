using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// Applies the final SYS decision to a Pending delivery in the approval transaction (ADR-139).
/// Rejection releases the reservation (Pending → Rejected, allowed even if the order is no longer
/// Approved). Final approval must post stock through INV, which arrives with WU-SD-004b; until then
/// it is refused with 422 so the SYS decision rolls back and the delivery stays Pending.
/// </summary>
public sealed class SalesDeliveryApprovalResultHandler(MyErpDbContext db) : IApprovalResultHandler
{
    public string DocumentType => SalesDeliveryCreateService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned approval transaction is required.");

        var document = await db.Set<SalesDelivery>()
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException("SAL.DELIVERY.NOT_FOUND", "Sales delivery", result.DocumentId);
        if (document.Status != SalesDeliveryStatus.Pending || document.ApprovalInstanceId != result.InstanceId)
            throw new ConflictException("SAL.DELIVERY.APPROVAL_STATE_CHANGED",
                "Delivery is no longer waiting for this approval instance.");
        if (result.Status == ApprovalStatus.Approved)
            throw new BusinessRuleException(SalesDeliverySubmissionService.PostingUnavailable,
                "Delivery posting is not available yet; the delivery stays Pending.");

        document.Status = SalesDeliveryLifecycle.Next(document.Status, SalesDeliveryAction.Reject, false);
    }
}
