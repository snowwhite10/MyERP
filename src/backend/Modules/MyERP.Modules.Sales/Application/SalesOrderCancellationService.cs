using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// REQ_F4112.2.6 / BR-SD-016/019 (WU-SD-003b2, ADR-143): cancels a current-version Approved order that has no
/// Pending or Posted delivery. Cancellation only reduces credit exposure, so it takes no FI credit key; it locks the
/// order row (the same lock delivery submission takes first), then checks deliveries, so a concurrent delivery
/// submit either commits first (cancel refused) or waits and then sees the Cancelled order. Number, quotation
/// source, lines and SYS history are kept; no SYS round is changed.
/// </summary>
public sealed class SalesOrderCancellationService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company)
{
    public const string Permission = "SAL.ORDER.CANCEL";

    public async Task<SalesOrder> CancelAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync(Permission, token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await SalesOrderLocks.LockOrderAsync(db, companyId, id, token);
            var order = await scope.Apply(db.Set<SalesOrder>()).SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
            if (version < 1 || order.Version != version) throw new ConflictException();

            var hasDelivery = await db.Set<SalesDelivery>().AsNoTracking().AnyAsync(x => x.OrderId == id &&
                (x.Status == SalesDeliveryStatus.Pending || x.Status == SalesDeliveryStatus.Posted), token);
            order.Status = SalesOrderLifecycle.Next(order.Status, SalesOrderAction.Cancel, hasDelivery);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            db.ChangeTracker.Clear();
            return await db.Set<SalesOrder>().AsNoTracking().Include(x => x.Lines)
                .SingleAsync(x => x.Id == id, token);
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
}
