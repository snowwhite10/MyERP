using Microsoft.EntityFrameworkCore;
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
/// transaction (Read Committed per ADR-138/139) and must already hold the source-order
/// and delivery primary-key update locks, so no writer can change these rows; it must
/// then lock source lines in the returned order, reread the full balance and call
/// <see cref="SalesDeliverySelection.CheckLockedDecision"/>. Every read seeks this
/// company's delivery only, so other orders' or companies' uncommitted rows never block it.
/// </summary>
public sealed class SalesDeliverySelectionStore(MyErpDbContext db)
{
    public async Task<SavedDeliverySelection> SelectAsync(long deliveryId,
        SalesDeliveryStatus expectedStatus, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned delivery transaction is required.");

        var header = await db.Set<SalesDelivery>().FromSqlInterpolated($"""
                SELECT * FROM dbo.sal_delivery WITH (FORCESEEK) WHERE id = {deliveryId}
                """).AsNoTracking()
            .Select(x => new { x.Id, x.CompanyId, x.Status, x.IsDeleted }).SingleOrDefaultAsync(token);
        if (header is null)
            throw new NotFoundException("SAL.DELIVERY.NOT_FOUND", "Sales delivery", deliveryId);
        if (header.Status != expectedStatus || header.IsDeleted)
            throw new BusinessRuleException("SAL.DELIVERY.INVALID_STATUS",
                "Delivery action is unavailable in its current status.");

        // Independent COUNT detects a truncated materialized selection; the caller's
        // delivery and order locks keep both reads on the same saved rows.
        var count = await Lines(header.CompanyId, deliveryId).CountAsync(token);
        var lines = await Lines(header.CompanyId, deliveryId)
            .OrderBy(x => x.Id)
            .Select(x => new SavedDeliverySelectedLine(x.Id, x.DeliveryId,
                x.OrderLineId, x.Quantity, expectedStatus, false))
            .ToArrayAsync(token);
        var lockPlan = SalesDeliverySelection.CreateLockPlan(lines, count,
            deliveryId, expectedStatus);
        return new SavedDeliverySelection(lines, lockPlan);
    }

    private IQueryable<SalesDeliveryLine> Lines(long companyId, long deliveryId) =>
        db.Set<SalesDeliveryLine>().FromSqlInterpolated($"""
            SELECT * FROM dbo.sal_delivery_line
              WITH (INDEX(ix_sal_delivery_line_company_id_delivery_id_line_no), FORCESEEK)
            WHERE company_id = {companyId} AND delivery_id = {deliveryId}
            """).AsNoTracking();
}
