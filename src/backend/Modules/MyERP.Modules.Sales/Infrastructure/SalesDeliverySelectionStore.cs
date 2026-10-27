using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

public sealed record SavedDeliverySelection(
    IReadOnlyList<SavedDeliverySelectedLine> Lines,
    IReadOnlyList<long> OrderLineLockPlan);

/// <summary>
/// Selects every saved current line before source locking. The caller owns the
/// serializable transaction and must subsequently lock source lines in the
/// returned order, reread the full balance, and call CheckLockedDecision.
/// </summary>
public sealed class SalesDeliverySelectionStore(MyErpDbContext db)
{
    public async Task<SavedDeliverySelection> SelectAsync(long deliveryId,
        SalesDeliveryStatus expectedStatus, CancellationToken token = default)
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("A serializable delivery transaction is required.");

        var header = await db.Set<SalesDelivery>().AsNoTracking()
            .Where(x => x.Id == deliveryId)
            .Select(x => new { x.Id, x.Status, x.IsDeleted }).SingleOrDefaultAsync(token);
        if (header is null)
            throw new NotFoundException("SAL.DELIVERY.NOT_FOUND", "Sales delivery", deliveryId);
        if (header.Status != expectedStatus || header.IsDeleted)
            throw new BusinessRuleException("SAL.DELIVERY.INVALID_STATUS",
                "Delivery action is unavailable in its current status.");

        // Independent COUNT detects a truncated materialized selection. The
        // serializable transaction keeps both reads in one protected snapshot.
        var count = await db.Set<SalesDeliveryLine>().AsNoTracking()
            .CountAsync(x => x.DeliveryId == deliveryId, token);
        var lines = await db.Set<SalesDeliveryLine>().AsNoTracking()
            .Where(x => x.DeliveryId == deliveryId)
            .OrderBy(x => x.Id)
            .Select(x => new SavedDeliverySelectedLine(x.Id, x.DeliveryId,
                x.OrderLineId, x.Quantity, expectedStatus, false))
            .ToArrayAsync(token);
        var lockPlan = SalesDeliverySelection.CreateLockPlan(lines, count,
            deliveryId, expectedStatus);
        return new SavedDeliverySelection(lines, lockPlan);
    }
}
