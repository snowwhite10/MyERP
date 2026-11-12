namespace MyERP.Modules.Purchasing.Application;

using MyERP.Modules.Purchasing.Domain;
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

/// <summary>Rows the SQL writer must add and the tracked rows after validated flag changes.</summary>
public sealed record PurchaseInvoiceNumberClaimApplyResult(
    IReadOnlyList<PurchaseInvoiceNumberClaim> ClaimsToInsert,
    IReadOnlyList<PurchaseInvoiceNumberClaim> TrackedClaims,
    IReadOnlyList<PurchaseInvoiceNumberClaim> ChangedTrackedClaims);

/// <summary>One invoice graph and its ordered number changes from the same validated save intent.</summary>
public sealed class PurchaseInvoiceDraftEntityPlan
{
    private readonly PurchaseInvoiceDraftRowPlan rows;

    internal PurchaseInvoiceDraftEntityPlan(PurchaseInvoice invoice,
        PurchaseInvoiceNumberClaimEntityPlan numberClaims, PurchaseInvoiceDraftRowPlan rows)
    {
        Invoice = invoice;
        NumberClaims = numberClaims;
        this.rows = rows;
    }

    public PurchaseInvoice Invoice { get; }
    public PurchaseInvoiceNumberClaimEntityPlan NumberClaims { get; }

    /// <summary>Preflight the entire graph before changing tracked number rows.</summary>
    public PurchaseInvoiceNumberClaimApplyResult ApplyNumberClaims()
    {
        ValidateBeforeSave();
        foreach (var change in NumberClaims.Changes)
            change.Apply();
        var inserts = NumberClaims.Changes
            .Where(x => x.Kind == PurchaseInvoiceNumberClaimWriteKind.InsertActive)
            .Select(x => x.Entity).ToArray();
        var changed = NumberClaims.Changes
            .Where(x => x.Kind != PurchaseInvoiceNumberClaimWriteKind.InsertActive)
            .Select(x => x.Entity).ToArray();
        return new(Array.AsReadOnly(inserts), NumberClaims.TrackedClaims,
            Array.AsReadOnly(changed));
    }

    /// <summary>Rejects drift in the materialized graph and tracked number rows before applying changes.</summary>
    public void ValidateBeforeSave()
    {
        var header = rows.Header;
        if (Invoice.Id != rows.InvoiceId || Invoice.CompanyId != header.CompanyId ||
            Invoice.SupplierId != header.SupplierId || Invoice.PurchaseOrderId != header.PurchaseOrderId ||
            Invoice.Currency != header.Currency || Invoice.BusinessDate != header.BusinessDate ||
            Invoice.SupplierInvoiceNumber != header.SupplierInvoiceNumber || Invoice.NumberKey != header.NumberKey ||
            Invoice.Status != PurchaseInvoiceStatus.Draft || Invoice.IsDeleted ||
            Invoice.Version != (rows.ExpectedVersion ?? 0) ||
            Invoice.TotalNet != header.TotalNet || Invoice.TotalTax != header.TotalTax ||
            Invoice.TotalGross != header.TotalGross || Invoice.Lines.Count != rows.Lines.Count)
            throw InvalidRows();

        var actual = Invoice.Lines.OrderBy(x => x.LineNo).ToArray();
        var expected = rows.Lines.OrderBy(x => x.LineNo).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            var line = actual[i];
            var row = expected[i];
            if (line.Id != 0 || line.CompanyId != row.CompanyId || line.InvoiceId != row.InvoiceId ||
                line.LineNo != row.LineNo || line.ReceiptLineId != row.ReceiptLineId ||
                line.Quantity != row.Quantity || line.UnitPrice != row.UnitPrice ||
                line.TaxRatePercent != row.TaxRatePercent || line.Net != row.Net ||
                line.Tax != row.Tax || line.Gross != row.Gross)
                throw InvalidRows();
        }
        NumberClaims.ValidateBeforeSave();
    }

    private static BusinessRuleException InvalidRows() => new("PUR.INVOICE.INVALID_DRAFT_ROWS",
        "Invoice draft entities changed after validation.");
}

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

    /// <summary>Maps one rechecked snapshot to the header and replacement line rows for the SQL writer.</summary>
    public PurchaseInvoiceDraftRowPlan PlanDraftRows(long invoiceId)
    {
        if (invoiceId <= 0 || (InvoiceId.HasValue && InvoiceId.Value != invoiceId) ||
            (InvoiceId.HasValue && (!ExpectedVersion.HasValue || ExpectedVersion.Value <= 0)))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_SAVED_IDENTITY",
                "Invoice row identity does not match the validated draft.");

        var identity = Snapshot.Identity;
        var totals = Snapshot.Totals;
        var header = new PurchaseInvoiceDraftHeaderRow(identity.CompanyId, identity.SupplierId,
            identity.PurchaseOrderId, identity.Currency, Snapshot.BusinessDate,
            Snapshot.Number.Display, Snapshot.Number.Key, totals.Net, totals.Tax, totals.Gross);
        var rows = Snapshot.Lines.OrderBy(x => x.LineNumber).Select(line =>
            new PurchaseInvoiceDraftLineRow(identity.CompanyId, invoiceId, line.LineNumber,
                line.ReceiptLineId, line.Quantity, line.UnitPrice, line.TaxRatePercent,
                line.Net, line.Tax, line.Gross)).ToArray();
        return new(invoiceId, ExpectedVersion, header, Array.AsReadOnly(rows));
    }

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

    /// <summary>Builds EF changes from the same validated draft and a complete locked claim snapshot.</summary>
    public PurchaseInvoiceNumberClaimEntityPlan PlanNumberClaimEntities(long invoiceId,
        IReadOnlyCollection<PurchaseInvoiceNumberClaim>? claims)
    {
        if (invoiceId <= 0 || (InvoiceId.HasValue && InvoiceId.Value != invoiceId) ||
            (InvoiceId.HasValue && NumberRevision is null) ||
            (!InvoiceId.HasValue && NumberRevision is not null))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT",
                "Invoice number claim identity does not match the validated draft.");

        return NumberRevision is null
            ? PurchaseInvoiceNumberClaimEntityPlan.ForNew(Identity.CompanyId, Identity.SupplierId,
                invoiceId, NumberPersistence.ActiveKey, claims)
            : PurchaseInvoiceNumberClaimEntityPlan.ForRevision(Identity.CompanyId, Identity.SupplierId,
                invoiceId, NumberRevision, claims);
    }

    /// <summary>Materializes both EF graphs with one writer-assigned identity and locked claim snapshot.</summary>
    public PurchaseInvoiceDraftEntityPlan PlanDraftEntities(long invoiceId,
        IReadOnlyCollection<PurchaseInvoiceNumberClaim>? claims)
    {
        var rows = PlanDraftRows(invoiceId);
        var invoice = rows.Materialize();
        var numberClaims = PlanNumberClaimEntities(invoiceId, claims);
        if (invoice.CompanyId != Identity.CompanyId || invoice.SupplierId != Identity.SupplierId ||
            invoice.Id != invoiceId || invoice.NumberKey != NumberPersistence.ActiveKey ||
            numberClaims.Changes.Any(x => x.Entity.CompanyId != invoice.CompanyId ||
                x.Entity.SupplierId != invoice.SupplierId || x.Entity.InvoiceId != invoice.Id))
            throw new BusinessRuleException("PUR.INVOICE.INVALID_SAVED_IDENTITY",
                "Invoice rows and number claims do not share the validated identity.");
        return new(invoice, numberClaims, rows);
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
