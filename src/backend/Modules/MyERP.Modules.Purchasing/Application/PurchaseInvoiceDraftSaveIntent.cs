namespace MyERP.Modules.Purchasing.Application;

/// <summary>One validated source for all draft fields to be written by the caller.</summary>
public sealed class PurchaseInvoiceDraftSaveIntent
{
    private PurchaseInvoiceDraftSaveIntent(PurchaseInvoiceDraftSnapshot snapshot) => Snapshot = snapshot;

    public PurchaseInvoiceDraftSnapshot Snapshot { get; }
    public PurchaseInvoiceIdentity Identity => Snapshot.Identity;
    public DateOnly BusinessDate => Snapshot.BusinessDate;
    public PurchaseInvoiceNumberValue Number => Snapshot.Number;
    public IReadOnlyList<PurchaseInvoiceDraftLine> Lines => Snapshot.Lines;
    public PurchaseInvoiceDraftTotals Totals => Snapshot.Totals;
    public PurchaseInvoiceAmountDecision AmountEvidence => Snapshot.AmountEvidence;

    /// <summary>Caller supplies authority and number owners read under the save transaction lock.</summary>
    public static PurchaseInvoiceDraftSaveIntent Create(PurchaseInvoiceDraftSnapshot? saved,
        long? currentInvoiceId, PurchaseInvoiceOrderIdentity order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        IReadOnlyList<PurchaseInvoiceOrderLinePrice> prices,
        IReadOnlyCollection<PurchaseInvoiceNumberOwner>? numberOwners)
    {
        var checkedSnapshot = PurchaseInvoiceDraftRecheck.Check(saved, order, receipts, prices);
        PurchaseInvoiceNumberConflict.EnsureAvailable(checkedSnapshot.Identity.CompanyId,
            checkedSnapshot.Identity.SupplierId, currentInvoiceId,
            checkedSnapshot.Number.Display, numberOwners);
        var evidence = checkedSnapshot.AmountEvidence;
        var lines = evidence.Lines.Select(line => line with
        {
            Differences = Array.AsReadOnly(line.Differences.ToArray())
        }).ToArray();
        return new(checkedSnapshot with
        {
            AmountEvidence = evidence with { Lines = Array.AsReadOnly(lines) }
        });
    }
}
