using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>Saved invoice values loaded together inside the caller's locked transaction.</summary>
public sealed record PurchaseInvoicePostSnapshot(long InvoiceId, string Status,
    DateOnly BusinessDate, PurchaseInvoiceIdentity Identity,
    IReadOnlyList<PurchaseInvoiceMatchLine> Lines,
    IReadOnlyList<PurchaseInvoiceResolvedLine> AcceptedDifferences);

/// <summary>Prepared FI request values; the actual FI contract and transaction belong to 008b.</summary>
public sealed record PurchaseInvoicePayableInput(long CompanyId, long PurchaseInvoiceId,
    string SnapshotHash, long SupplierId, string Currency, DateOnly BusinessDate,
    IReadOnlyList<PurchaseInvoiceAmountLine> Lines);

public sealed record PurchaseInvoicePostAssemblyResult(PurchaseInvoicePostDecision Posting,
    PurchaseInvoicePayableInput Payable);

/// <summary>Uses one copied saved snapshot for POST preflight, fingerprint, and FI inputs.</summary>
public static class PurchaseInvoicePostAssembly
{
    public static PurchaseInvoicePostAssemblyResult Build(long requestedCompanyId,
        long requestedInvoiceId, PurchaseInvoicePostSnapshot saved,
        PurchaseInvoiceOrderIdentity order,
        IReadOnlyList<PurchaseInvoiceReceiptIdentity> receipts,
        IReadOnlyList<PurchaseInvoicePostingSource> sources)
    {
        if (saved is null || saved.Identity is null || saved.Lines is null ||
            saved.AcceptedDifferences is null || requestedCompanyId <= 0 ||
            requestedInvoiceId <= 0 || requestedCompanyId != saved.Identity.CompanyId ||
            requestedInvoiceId != saved.InvoiceId)
            throw InvalidSnapshot();

        // Materialize caller-owned collections before either rule reads them.
        var lines = saved.Lines.ToArray();
        var accepted = saved.AcceptedDifferences.Select(x => x is null
            ? null! : x with { Differences = x.Differences?.ToArray()! }).ToArray();
        var posting = PurchaseInvoicePostPreflight.Check(saved.Status, lines,
            saved.Identity, order, receipts, sources, accepted);
        var hash = PurchaseInvoiceSnapshotFingerprint.Create(saved.Identity.CompanyId,
            saved.InvoiceId, saved.Identity.SupplierId, saved.Identity.Currency,
            saved.BusinessDate, lines, accepted);
        var payable = new PurchaseInvoicePayableInput(saved.Identity.CompanyId,
            saved.InvoiceId, hash, saved.Identity.SupplierId, saved.Identity.Currency,
            saved.BusinessDate, lines.Select(x => x.Amount).OrderBy(x => x.LineNo).ToArray());
        return new(posting, payable);
    }

    private static BusinessRuleException InvalidSnapshot() =>
        new("PUR.INVOICE.INVALID_POSTING_SNAPSHOT", "Requested invoice does not match saved invoice identity.");
}
