using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// WU-SD-008a2b1 (REQ_F4112.7.1, BR-SD-043/044/046/048, ADR-186): edits and soft-deletes a Draft sales return.
/// Edit replaces the date, remark and every line, rerunning the creation checks and snapshot of
/// <see cref="SalesReturnDraftBuilder"/>; the number, request key, fingerprint, order and currency stay. Returns have no
/// owner, so the exact action scope is checked on the source order and hidden returns are 404. Lock order: order row,
/// return row, then the builder's delivery-line and invoice-line locks.
/// </summary>
public sealed class SalesReturnDraftCommandService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesReturnDraftBuilder builder, IClock clock)
{
    public const string EditPermission = "SAL.RETURN.EDIT";
    public const string DeletePermission = "SAL.RETURN.DELETE";
    public const string NotFoundCode = "SAL.RETURN.NOT_FOUND";
    public const string InvalidStatus = "SAL.RETURN.INVALID_STATUS";
    public const string RequestKeyImmutable = "SAL.RETURN.REQUEST_KEY_IMMUTABLE";

    public async Task<SalesReturn> UpdateAsync(long id, int version, SalesReturnDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync(EditPermission, token);
        var lines = input.Lines!.OrderBy(x => x.LineNo).ToArray();
        await RunAsync(id, version, scope, async saved =>
        {
            if (!string.Equals(input.RequestKey, saved.RequestKey, StringComparison.Ordinal))
                throw new BusinessRuleException(RequestKeyImmutable,
                    "The request key of a saved sales return cannot be changed.");
            await builder.ResolveOrderAsync(saved.CompanyId, lines, saved.OrderId, token);
            await builder.LockLinesAsync(saved.CompanyId, lines, token);
            var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .Where(x => x.Id == saved.OrderId).Include(x => x.Lines).AsSplitQuery().SingleOrDefaultAsync(token)
                ?? throw NotFound(id);
            var replacement = await builder.BuildLinesAsync(saved.CompanyId, order, input.BusinessDate, lines, token);

            await builder.GuardDestinationsAsync(replacement, async () =>
            {
                db.RemoveRange(saved.Lines.SelectMany(x => x.InvoiceAllocations));
                db.RemoveRange(saved.Lines);
                saved.Lines.Clear();
                saved.Lines.AddRange(replacement);
                saved.BusinessDate = input.BusinessDate;
                saved.Remark = input.NormalizedRemark;
                SalesReturnDraftBuilder.ApplyTotals(saved);
                // A lines-only replacement must still advance the header's optimistic version.
                db.Entry(saved).Property(x => x.BusinessDate).IsModified = true;
                await db.SaveChangesAsync(token);
            }, token);
        }, token);

        var result = await db.Set<SalesReturn>().AsNoTracking().AsSplitQuery()
            .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations).SingleAsync(x => x.Id == id, token);
        SalesReturnCreateService.SortForOutput(result);
        return result;
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync(DeletePermission, token);
        // A Draft of a cancelled order may still be discarded; delete never consults the order state.
        await RunAsync(id, version, scope, async saved =>
        {
            saved.IsDeleted = true;
            saved.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
        }, token);
    }

    private async Task RunAsync(long id, int version, DataScopeFilter scope, Func<SalesReturn, Task> mutate,
        CancellationToken token)
    {
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // The source order of a saved return never changes (trigger 51035), so this unlocked read is stable.
            var orderId = (await db.Database.SqlQueryRaw<long>(
                "SELECT [order_id] AS [Value] FROM dbo.sal_return WITH (FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token)).SingleOrDefault();
            if (orderId == 0) throw NotFound(id);
            await builder.LockOrderAsync(companyId, orderId, token);
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_return WITH (UPDLOCK, HOLDLOCK, FORCESEEK) " +
                "WHERE [id] = {0} AND [company_id] = {1}", id, companyId).ToListAsync(token);

            if (!await scope.Apply(db.Set<SalesOrder>()).AnyAsync(x => x.Id == orderId, token))
                throw NotFound(id);
            var saved = await db.Set<SalesReturn>().AsSplitQuery()
                .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations)
                .SingleOrDefaultAsync(x => x.Id == id, token) ?? throw NotFound(id);
            if (version < 1 || saved.Version != version) throw new ConflictException();
            if (saved.Status != SalesReturnStatus.Draft)
                throw new BusinessRuleException(InvalidStatus, "Only a Draft sales return can be edited or deleted.");

            await mutate(saved);
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

    private static NotFoundException NotFound(long id) => new(NotFoundCode, "Sales return", id);
}
