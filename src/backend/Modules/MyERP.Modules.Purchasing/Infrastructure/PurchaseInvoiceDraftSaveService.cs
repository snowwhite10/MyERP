using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>Creates the transaction session from the server-scoped draft identity.</summary>
public interface IPurchaseInvoiceDraftSessionFactory
{
    IPurchaseInvoiceDraftSaveSession Create(long companyId, long supplierId,
        long orderId, IReadOnlyList<long> receiptLineIds, long? invoiceId);
}

public sealed class PurchaseInvoiceDraftSessionFactory(
    MyErpDbContext db, IPurchaseInvoiceDraftSourceReader sources)
    : IPurchaseInvoiceDraftSessionFactory
{
    public IPurchaseInvoiceDraftSaveSession Create(long companyId, long supplierId,
        long orderId, IReadOnlyList<long> receiptLineIds, long? invoiceId)
    {
        var reader = new PurchaseInvoiceDraftLockedReader(db, sources, companyId,
            supplierId, orderId, receiptLineIds, invoiceId);
        return new PurchaseInvoiceDraftSqlSession(db, companyId, supplierId, reader.ReadAsync);
    }
}

public sealed class PurchaseInvoiceDraftSaveService(IPurchaseInvoiceDraftSessionFactory sessions)
{
    public Task<long> SaveAsync(PurchaseInvoiceDraftHttpIntent command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var input = command.Input;
        var session = sessions.Create(command.CompanyId, input.SupplierId,
            input.PurchaseOrderId, command.ReceiptLineIds, command.InvoiceId);
        return PurchaseInvoiceDraftSaveCoordinator.SaveAsync(command, session, cancellationToken);
    }

    public Task<long> SaveAsync(PurchaseInvoiceDraftSnapshot snapshot,
        long? invoiceId, int? expectedVersion, long provisionalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var identity = snapshot.Identity;
        if (identity is null || identity.CompanyId <= 0 || identity.SupplierId <= 0 ||
            identity.PurchaseOrderId <= 0 || identity.Currency is not { Length: 3 } ||
            !identity.Currency.All(c => c is >= 'A' and <= 'Z') ||
            snapshot.BusinessDate == default || snapshot.Lines is null ||
            snapshot.Lines.Count == 0 || snapshot.Lines.Any(x => x is null || x.ReceiptLineId <= 0) ||
            (invoiceId.HasValue
                ? invoiceId.Value <= 0 || expectedVersion is null or <= 0 || provisionalId != 0
                : expectedVersion.HasValue || provisionalId <= 0))
            throw new ArgumentException("Draft save scope is invalid.", nameof(snapshot));

        var receiptIds = snapshot.Lines.Select(x => x.ReceiptLineId).Distinct().Order().ToArray();
        var session = sessions.Create(identity.CompanyId, identity.SupplierId,
            identity.PurchaseOrderId, receiptIds, invoiceId);
        return PurchaseInvoiceDraftSaveCoordinator.SaveAsync(snapshot, invoiceId,
            expectedVersion, provisionalId, session, cancellationToken);
    }
}
