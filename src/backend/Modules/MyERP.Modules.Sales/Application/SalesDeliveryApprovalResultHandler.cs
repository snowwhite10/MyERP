using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// Applies the final SYS decision to a Pending delivery in the approval transaction (ADR-139, ADR-144).
/// Rejection releases the reservation (Pending → Rejected, allowed even if the order is no longer Approved) and
/// never calls INV. Final approval (BR-SD-024) locks the source order, the delivery and the order lines in the
/// submission order, requires the order to be Approved, rechecks the full balance with the current Pending
/// reservation excluded, posts the saved lines through INV and marks the delivery Posted. Any refusal throws, so
/// SYS rolls back its decision together with SD and INV and the delivery stays Pending.
/// The posting step is resolved only when a decision arrives: INV posting depends on MD services, and SYS builds
/// every result handler when the workflow is resolved, so constructor injection risks a DI cycle (see ADR-142).
/// </summary>
public sealed class SalesDeliveryApprovalResultHandler(MyErpDbContext db, Func<SalesDeliveryPosting> postingFactory)
    : IApprovalResultHandler
{
    public string DocumentType => SalesDeliveryCreateService.DocumentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned approval transaction is required.");

        if (result.Status == ApprovalStatus.Rejected)
        {
            var rejected = await LoadPendingAsync(result, cancellationToken);
            rejected.Status = SalesDeliveryLifecycle.Next(rejected.Status, SalesDeliveryAction.Reject, false);
            return;
        }

        var posting = postingFactory();
        // The source order of a saved delivery never changes, so this unlocked read only chooses the lock.
        var header = await db.Set<SalesDelivery>().AsNoTracking().Where(x => x.Id == result.DocumentId)
            .Select(x => new { x.CompanyId, x.OrderId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound(result.DocumentId);
        await SalesOrderLocks.LockOrderAsync(db, header.CompanyId, header.OrderId, cancellationToken);
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_delivery WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
            "WHERE [id] = {0} AND [company_id] = {1}",
            result.DocumentId, header.CompanyId).ToListAsync(cancellationToken);

        var delivery = await LoadPendingAsync(result, cancellationToken);
        var orderStatus = await db.Set<SalesOrder>().AsNoTracking().Where(x => x.Id == delivery.OrderId)
            .Select(x => (SalesOrderStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
        SalesDeliveryLifecycle.Next(delivery.Status, SalesDeliveryAction.Approve,
            orderStatus == SalesOrderStatus.Approved);
        await posting.LockAndRecheckAsync(delivery, SalesDeliveryStatus.Pending, cancellationToken);
        await posting.LoadLinesAsync(delivery, cancellationToken);
        await posting.PostAsync(delivery, cancellationToken);
    }

    private async Task<SalesDelivery> LoadPendingAsync(ApprovalCompleted result, CancellationToken token)
    {
        var document = await db.Set<SalesDelivery>().SingleOrDefaultAsync(x => x.Id == result.DocumentId, token)
            ?? throw NotFound(result.DocumentId);
        if (document.Status != SalesDeliveryStatus.Pending || document.ApprovalInstanceId != result.InstanceId)
            throw new ConflictException("SAL.DELIVERY.APPROVAL_STATE_CHANGED",
                "Delivery is no longer waiting for this approval instance.");
        return document;
    }

    private static NotFoundException NotFound(long id) => new("SAL.DELIVERY.NOT_FOUND", "Sales delivery", id);
}
