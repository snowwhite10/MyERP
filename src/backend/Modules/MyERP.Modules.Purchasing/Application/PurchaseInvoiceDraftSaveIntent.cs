namespace MyERP.Modules.Purchasing.Application;

using MyERP.Shared.Errors;

/// <summary>Saved invoice header read under the revision transaction lock.</summary>
public sealed record PurchaseInvoiceSavedDraft(long InvoiceId, long CompanyId,
    long SupplierId, long PurchaseOrderId, string Currency, string Status, int Version, bool IsDeleted,
    string NumberKey);

public enum PurchaseInvoiceNumberRevisionKind { Retained, Changed }

/// <summary>Canonical number keys to compare and write under the save transaction lock.</summary>
public sealed record PurchaseInvoiceNumberRevision(PurchaseInvoiceNumberRevisionKind Kind,
    string OldKey, string NewKey);

/// <summary>Keys the SQL writer must keep occupied after saving the draft.</summary>
public sealed record PurchaseInvoiceNumberPersistencePlan(string ActiveKey, string? RetainedOldKey);

/// <summary>One validated source for all draft fields to be written by the caller.</summary>
public sealed class PurchaseInvoiceDraftSaveIntent
{
    private PurchaseInvoiceDraftSaveIntent(PurchaseInvoiceDraftSnapshot snapshot,
        long? invoiceId = null, int? expectedVersion = null,
        PurchaseInvoiceNumberRevision? numberRevision = null)
    {
        Snapshot = snapshot;
        InvoiceId = invoiceId;
        ExpectedVersion = expectedVersion;
        NumberRevision = numberRevision;
    }

    public PurchaseInvoiceDraftSnapshot Snapshot { get; }
    public long? InvoiceId { get; }
    public int? ExpectedVersion { get; }
    public PurchaseInvoiceNumberRevision? NumberRevision { get; }
    public PurchaseInvoiceNumberPersistencePlan NumberPersistence => new(Number.Key,
        NumberRevision is { Kind: PurchaseInvoiceNumberRevisionKind.Changed } revision
            ? revision.OldKey : null);
    public PurchaseInvoiceIdentity Identity => Snapshot.Identity;
    public DateOnly BusinessDate => Snapshot.BusinessDate;
    public PurchaseInvoiceNumberValue Number => Snapshot.Number;
    public IReadOnlyList<PurchaseInvoiceDraftLine> Lines => Snapshot.Lines;
    public PurchaseInvoiceDraftTotals Totals => Snapshot.Totals;
    public PurchaseInvoiceAmountDecision AmountEvidence => Snapshot.AmountEvidence;

    /// <summary>Bind the claim write steps to this validated draft and the writer-assigned invoice ID.</summary>
    public PurchaseInvoiceNumberClaimWritePlan PlanNumberClaimWrite(long invoiceId,
        IReadOnlyCollection<PurchaseInvoiceNumberClaimState>? claims)
    {
        if (invoiceId <= 0 || (InvoiceId.HasValue && InvoiceId.Value != invoiceId) ||
            (InvoiceId.HasValue && NumberRevision is null) ||
            (!InvoiceId.HasValue && NumberRevision is not null))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT",
                "Invoice number claim identity does not match the validated draft.");

        return NumberRevision is null
            ? PurchaseInvoiceNumberClaimWritePlan.ForNew(Identity.CompanyId, Identity.SupplierId,
                invoiceId, NumberPersistence.ActiveKey, claims)
            : PurchaseInvoiceNumberClaimWritePlan.ForRevision(Identity.CompanyId, Identity.SupplierId,
                invoiceId, NumberRevision, claims);
    }

    /// <summary>Checks the locked saved header before allowing its own number to be retained.</summary>
    public static PurchaseInvoiceDraftSaveIntent CreateRevision(PurchaseInvoiceDraftSnapshot? draft,
        PurchaseInvoiceSavedDraft? saved, int expectedVersion,
        PurchaseInvoiceOrderIdentity order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        IReadOnlyList<PurchaseInvoiceOrderLinePrice> prices,
        IReadOnlyCollection<PurchaseInvoiceNumberOwner>? numberOwners)
    {
        if (draft is null || saved is null || saved.InvoiceId <= 0 || saved.CompanyId <= 0 ||
            saved.SupplierId <= 0 || saved.PurchaseOrderId <= 0 || draft.Identity is null ||
            saved.CompanyId != draft.Identity.CompanyId ||
            saved.SupplierId != draft.Identity.SupplierId ||
            saved.PurchaseOrderId != draft.Identity.PurchaseOrderId ||
            saved.Currency != draft.Identity.Currency)
            throw new BusinessRuleException("PUR.INVOICE.INVALID_SAVED_IDENTITY",
                "Saved invoice identity does not match the draft revision.");
        if (saved.IsDeleted || saved.Status != "Draft")
            throw new BusinessRuleException("PUR.INVOICE.NOT_DRAFT",
                "Only an active draft invoice can be revised.");
        if (expectedVersion <= 0 || saved.Version != expectedVersion)
            throw new ConflictException();

        // The saved header and the ownership row must describe the same invoice before
        // its ID can exempt either a retained number or a changed number.
        if (numberOwners is null || !IsCanonicalKey(saved.NumberKey) ||
            !numberOwners.Any(owner => owner is not null && owner.InvoiceId == saved.InvoiceId &&
                owner.CompanyId == saved.CompanyId && owner.SupplierId == saved.SupplierId &&
                !owner.IsDeleted && string.Equals(owner.NumberKey, saved.NumberKey, StringComparison.Ordinal)))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT",
                "Saved invoice number ownership does not match its header.");

        var intent = Create(draft, saved.InvoiceId, order, receipts, prices, numberOwners);
        var newKey = intent.Number.Key;
        var revision = new PurchaseInvoiceNumberRevision(
            string.Equals(saved.NumberKey, newKey, StringComparison.Ordinal)
                ? PurchaseInvoiceNumberRevisionKind.Retained
                : PurchaseInvoiceNumberRevisionKind.Changed,
            saved.NumberKey, newKey);
        return new(intent.Snapshot, saved.InvoiceId, expectedVersion, revision);
    }

    private static bool IsCanonicalKey(string? key)
    {
        try { return key is not null && PurchaseInvoiceNumber.Normalize(key).Key == key; }
        catch (BusinessRuleException) { return false; }
    }

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
