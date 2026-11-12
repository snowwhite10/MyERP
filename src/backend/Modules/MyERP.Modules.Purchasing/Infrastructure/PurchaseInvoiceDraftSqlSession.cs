using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>SQL transaction shell for one supplier's invoice draft. The reader must lock and
/// return every authoritative source row and the complete number-claim set in this transaction.</summary>
public sealed class PurchaseInvoiceDraftSqlSession(
    MyErpDbContext db, long companyId, long supplierId,
    Func<CancellationToken, Task<PurchaseInvoiceDraftSaveLockedState>> lockedReader)
    : IPurchaseInvoiceDraftSaveSession
{
    private IDbContextTransaction? transaction;
    private bool read;
    private bool prepared;
    private bool saved;

    public static string LockResource(long companyId, long supplierId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(companyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supplierId);
        return $"pur_invoice_draft:{companyId}:{supplierId}";
    }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null) throw new InvalidOperationException("Draft transaction already started.");
        var resource = LockResource(companyId, supplierId);
        transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Transaction-owned lock serializes drafts for the same supplier before any source read.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource},
                    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51001, 'Purchase invoice draft lock unavailable.', 1;
                """, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            transaction = null;
            throw;
        }
    }

    public async Task<PurchaseInvoiceDraftSaveLockedState> ReadLockedAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (read) throw new InvalidOperationException("Draft sources already read.");
        var state = await lockedReader(cancellationToken);
        if (state.Order.CompanyId != companyId || state.Order.SupplierId != supplierId)
            throw new InvalidOperationException("Locked draft source has a different scope.");
        read = true;
        return state;
    }

    public void Prepare(PurchaseInvoiceDraftEntityPlan plan, PurchaseInvoice? saved,
        IReadOnlyCollection<PurchaseInvoiceLine>? savedLines)
    {
        RequireTransaction();
        if (!read || prepared) throw new InvalidOperationException("Draft sources must be read once before prepare.");
        PurchaseInvoiceDraftTracking.Prepare(db, plan, saved, savedLines);
        prepared = true;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (!prepared || saved) throw new InvalidOperationException("Draft must be prepared and saved once.");
        saved = true;
        return await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (!saved) throw new InvalidOperationException("Draft was not saved.");
        await transaction!.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (transaction is null) return;
        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
        db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null) await RollbackAsync(CancellationToken.None);
    }

    private void RequireTransaction()
    {
        if (transaction is null) throw new InvalidOperationException("Draft transaction is not active.");
    }
}
