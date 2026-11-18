using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// REQ_F4112.3.3 / REQ_F4112.3.6 / BR-SD-021–023: replaces every line of a Draft or Rejected
/// delivery (returning it to Draft) and soft-deletes a Draft. Delivery headers have no owner,
/// so the exact action scope is checked on the source order; hidden documents are 404.
/// Lock order (ADR-138): source order primary key, then the delivery primary key.
/// </summary>
public sealed class SalesDeliveryDraftCommandService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesDeliveryDraftLines draftLines, IClock clock)
{
    public async Task<SalesDelivery> ReplaceAsync(long id, int version, SalesDeliveryDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync("SAL.DELIVERY.EDIT", token);
        await RunAsync(id, version, scope, async (saved, orderStatus) =>
        {
            saved.Status = SalesDeliveryLifecycle.Next(saved.Status, SalesDeliveryAction.Edit,
                orderStatus == SalesOrderStatus.Approved);
            if (orderStatus != SalesOrderStatus.Approved)
                throw new BusinessRuleException("SAL.DELIVERY.ORDER_NOT_APPROVED",
                    "The source order must be approved before the delivery can be edited.");
            if (input.OrderId != saved.OrderId)
                throw new BusinessRuleException("SAL.DELIVERY.SOURCE_IMMUTABLE",
                    "A saved delivery cannot change its source order.");

            var replacement = await draftLines.BuildAsync(saved.CompanyId, saved.OrderId, saved.Id,
                input.Lines!, token);
            await LoadLinesAsync(saved, token);
            await draftLines.GuardAsync(replacement, async () =>
            {
                db.RemoveRange(saved.Lines);
                saved.Lines.Clear();
                saved.Lines.AddRange(replacement);
                saved.BusinessDate = input.BusinessDate;
                // A lines-only replacement must still advance the header's optimistic version.
                db.Entry(saved).Property(x => x.BusinessDate).IsModified = true;
                await db.SaveChangesAsync(token);
            }, token);
        }, token);
        return await db.Set<SalesDelivery>().AsNoTracking().AsSplitQuery().Include(x => x.Lines).ThenInclude(x => x.Tracking)
            .SingleAsync(x => x.Id == id, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync("SAL.DELIVERY.DELETE", token);
        // A Draft of a cancelled order may still be discarded; Delete never consults the order state.
        await RunAsync(id, version, scope, async (saved, _) =>
        {
            saved.Status = SalesDeliveryLifecycle.Next(saved.Status, SalesDeliveryAction.Delete, false);
            saved.IsDeleted = true;
            saved.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
        }, token);
    }

    private async Task RunAsync(long id, int version, DataScopeFilter scope,
        Func<SalesDelivery, SalesOrderStatus, Task> mutate, CancellationToken token)
    {
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // The source order of a saved delivery never changes, so this unlocked read is stable.
            var orderId = (await db.Database.SqlQueryRaw<long>(
                "SELECT [order_id] AS [Value] FROM dbo.sal_delivery WITH (FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token)).SingleOrDefault();
            if (orderId == 0) throw NotFound(id);
            await draftLines.LockOrderAsync(companyId, orderId, token);
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_delivery WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1}",
                id, companyId).ToListAsync(token);

            var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .Where(x => x.Id == orderId).Select(x => new { x.Status }).SingleOrDefaultAsync(token)
                ?? throw NotFound(id);
            var saved = await db.Set<SalesDelivery>().SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw NotFound(id);
            if (version < 1 || saved.Version != version) throw new ConflictException();

            await mutate(saved, order.Status);
            await transaction.CommitAsync(token);
            db.ChangeTracker.Clear();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw new ConflictException();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private Task LoadLinesAsync(SalesDelivery saved, CancellationToken token) =>
        // Seek this delivery's lines only; tracking fixup attaches them to the loaded header.
        db.Set<SalesDeliveryLine>().FromSqlInterpolated($"""
            SELECT * FROM dbo.sal_delivery_line
              WITH (INDEX(ix_sal_delivery_line_company_id_delivery_id_line_no), FORCESEEK)
            WHERE company_id = {saved.CompanyId} AND delivery_id = {saved.Id}
            """).LoadAsync(token);

    private static NotFoundException NotFound(long id) =>
        new("SAL.DELIVERY.NOT_FOUND", "Sales delivery", id);
}
