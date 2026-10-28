using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>Applies the final SYS decision to the quotation in the caller's transaction.</summary>
public sealed class SalesQuotationApprovalResultHandler(MyErpDbContext db, IClock clock) : IApprovalResultHandler
{
    public string DocumentType => "SAL.QUOTATION";

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned approval transaction is required.");

        var document = await db.Set<SalesQuotation>()
            .SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", result.DocumentId);
        if (document.Status != SalesQuotationStatus.Pending || document.ApprovalInstanceId != result.InstanceId)
            throw new ConflictException("SAL.QUOTATION.APPROVAL_STATE_CHANGED",
                "Quotation is no longer waiting for this approval instance.");

        document.Status = SalesQuotationLifecycle.Next(document.Status,
            result.Status == ApprovalStatus.Approved ? SalesQuotationAction.Approve : SalesQuotationAction.Reject,
            document.ExpiryDate, false, clock);
        document.ApprovedAt = result.Status == ApprovalStatus.Approved ? clock.LocalNow : null;
    }
}
