using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>Mutates saved manual drafts within the exact SYS action scope.</summary>
public sealed class SalesOrderDraftCommandService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesOrderSnapshotBuilder snapshots, SalesOrderDraftStore store)
{
    public async Task<SalesOrder> ReplaceAsync(long id, int version, SalesOrderDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync("SAL.ORDER.EDIT", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await LockHeaderAsync(id, companyId, token);
            var saved = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
            if (version < 1 || saved.Version != version) throw new ConflictException();
            var replacement = await snapshots.BuildManualAsync(companyId, saved.DocumentNo, input, token);
            await store.ReplaceAsync(id, version, replacement, token);
            await transaction.CommitAsync(token);
            return await db.Set<SalesOrder>().AsNoTracking().Include(x => x.Lines)
                .SingleAsync(x => x.Id == id, token);
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

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync("SAL.ORDER.DELETE", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await LockHeaderAsync(id, companyId, token);
            var visible = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .AnyAsync(x => x.Id == id, token);
            if (!visible) throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
            await store.SoftDeleteAsync(id, version, token);
            await transaction.CommitAsync(token);
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

    private async Task LockHeaderAsync(long id, long companyId, CancellationToken token)
    {
        // Acquire the update lock before the scope read takes a shared lock. Two
        // concurrent commands can otherwise deadlock while upgrading that lock.
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, companyId).ToListAsync(token);
    }
}
