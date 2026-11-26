using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>The SQL adapter must read and lock authority, saved draft and all number claims in one transaction.</summary>
public interface IPurchaseInvoiceDraftSaveSession : IAsyncDisposable
{
    Task BeginAsync(CancellationToken cancellationToken);
    Task<PurchaseInvoiceDraftSaveLockedState> ReadLockedAsync(CancellationToken cancellationToken);
    void Prepare(PurchaseInvoiceDraftEntityPlan plan, PurchaseInvoice? saved,
        IReadOnlyCollection<PurchaseInvoiceLine>? savedLines);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

public sealed record PurchaseInvoiceDraftSaveLockedState(
    PurchaseInvoiceOrderIdentity Order,
    IReadOnlyList<PurchaseInvoiceReceiptIdentity> Receipts,
    IReadOnlyList<PurchaseInvoiceOrderLinePrice> Prices,
    IReadOnlyCollection<PurchaseInvoiceNumberOwner> Owners,
    IReadOnlyCollection<PurchaseInvoiceNumberClaim> Claims,
    PurchaseInvoiceSavedDraft? Saved,
    PurchaseInvoice? TrackedSaved,
    IReadOnlyCollection<PurchaseInvoiceLine>? TrackedSavedLines);

/// <summary>Coordinates one locked, atomic draft save. The session owns SQL isolation and lock order.</summary>
public static class PurchaseInvoiceDraftSaveCoordinator
{
    public static async Task<long> SaveAsync(PurchaseInvoiceDraftSnapshot snapshot,
        long? invoiceId, int? expectedVersion, long provisionalId,
        IPurchaseInvoiceDraftSaveSession session, CancellationToken cancellationToken = default)
        => await SaveCoreAsync(invoiceId, expectedVersion, provisionalId, session,
            locked => snapshot, cancellationToken);

    /// <summary>Assemble the HTTP command only after the authority rows are locked in this transaction.</summary>
    public static Task<long> SaveAsync(PurchaseInvoiceDraftHttpIntent command,
        IPurchaseInvoiceDraftSaveSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SaveCoreAsync(command.InvoiceId, command.ExpectedVersion, 1, session,
            locked => PurchaseInvoiceDraftAssembly.Create(command, locked.Order,
                locked.Receipts, locked.Prices), cancellationToken);
    }

    private static async Task<long> SaveCoreAsync(long? invoiceId, int? expectedVersion,
        long provisionalId, IPurchaseInvoiceDraftSaveSession session,
        Func<PurchaseInvoiceDraftSaveLockedState, PurchaseInvoiceDraftSnapshot> assemble,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            await session.BeginAsync(cancellationToken);
            var locked = await session.ReadLockedAsync(cancellationToken);
            if (invoiceId.HasValue && locked.Saved is null)
                throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", invoiceId.Value);
            var snapshot = assemble(locked);
            var intent = invoiceId.HasValue
                ? PurchaseInvoiceDraftSaveIntent.CreateRevision(snapshot, locked.Saved,
                    expectedVersion ?? 0, locked.Order, locked.Receipts, locked.Prices, locked.Owners)
                : PurchaseInvoiceDraftSaveIntent.Create(snapshot, null,
                    locked.Order, locked.Receipts, locked.Prices, locked.Owners);
            var plan = intent.PlanDraftEntities(invoiceId ?? provisionalId, locked.Claims);
            session.Prepare(plan, locked.TrackedSaved, locked.TrackedSavedLines);
            var changedRows = await session.SaveChangesAsync(cancellationToken);
            if (changedRows <= 0)
                throw new InvalidOperationException("Draft save changed no rows.");
            var actualId = invoiceId ?? plan.Invoice.Id;
            if (actualId <= 0 || (invoiceId.HasValue && actualId != invoiceId.Value))
                throw new InvalidOperationException("SQL IDENTITY did not assign the invoice ID.");
            await session.CommitAsync(cancellationToken);
            return actualId;
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql &&
            sql.Errors.Cast<SqlError>().Any(item =>
                PurchaseInvoiceNumberClaimWritePlan.IsNumberConflict(item.Number, item.Message)))
        {
            await session.RollbackAsync(CancellationToken.None);
            throw new ConflictException("PUR.INVOICE.NUMBER_EXISTS",
                "Supplier invoice number is already used by this supplier.");
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
