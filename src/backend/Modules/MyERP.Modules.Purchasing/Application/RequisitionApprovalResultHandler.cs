using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>SYS 最終決定與請購狀態在共用 DbContext 及同一交易中保存。</summary>
public sealed class RequisitionApprovalResultHandler(MyErpDbContext db) : IApprovalResultHandler
{
    public string DocumentType => RequisitionSubmissionService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var document = await db.Set<Requisition>().SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException("PUR.REQUISITION.NOT_FOUND", "Purchase requisition", result.DocumentId);
        if (document.Status != RequisitionStatus.PendingApproval)
            throw new ConflictException("PUR.REQUISITION.APPROVAL_STATE_CHANGED", "Requisition is no longer waiting for approval.");
        document.Status = result.Status == ApprovalStatus.Approved
            ? RequisitionStatus.Approved : RequisitionStatus.Rejected;
    }
}
