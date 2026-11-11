using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceNumberClaimState(long CompanyId, long SupplierId,
    long InvoiceId, string NumberKey, bool IsActive);

public enum PurchaseInvoiceNumberClaimWriteKind { Deactivate, ActivateExisting, InsertActive }

public sealed record PurchaseInvoiceNumberClaimWriteStep(PurchaseInvoiceNumberClaimWriteKind Kind, string NumberKey);

/// <summary>
/// Offline plan only. The writer must read all claims under the invoice transaction lock,
/// apply these steps in order and update the header in that same transaction.
/// </summary>
public sealed class PurchaseInvoiceNumberClaimWritePlan
{
    private const string NumberScopeIndex =
        "ux_pur_purchase_invoice_number_claim_company_id_supplier_id_number_key";

    private PurchaseInvoiceNumberClaimWritePlan(
        IReadOnlyList<PurchaseInvoiceNumberClaimWriteStep> steps, IReadOnlyList<string> retainedKeys)
    {
        Steps = steps;
        RetainedKeys = retainedKeys;
    }

    public IReadOnlyList<PurchaseInvoiceNumberClaimWriteStep> Steps { get; }
    public IReadOnlyList<string> RetainedKeys { get; }

    public static PurchaseInvoiceNumberClaimWritePlan ForRevision(long companyId, long supplierId,
        long invoiceId, PurchaseInvoiceNumberRevision? revision,
        IReadOnlyCollection<PurchaseInvoiceNumberClaimState>? claims)
    {
        if (companyId <= 0 || supplierId <= 0 || invoiceId <= 0 || revision is null || claims is null ||
            revision.Kind is not (PurchaseInvoiceNumberRevisionKind.Retained or PurchaseInvoiceNumberRevisionKind.Changed) ||
            !IsCanonical(revision.OldKey) || !IsCanonical(revision.NewKey) ||
            (revision.Kind == PurchaseInvoiceNumberRevisionKind.Retained) !=
                string.Equals(revision.OldKey, revision.NewKey, StringComparison.Ordinal))
            throw InvalidSnapshot();

        var ownKeys = new HashSet<string>(StringComparer.Ordinal);
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        var activeCount = 0;
        foreach (var claim in claims)
        {
            if (claim is null || claim.CompanyId <= 0 || claim.SupplierId <= 0 ||
                claim.InvoiceId <= 0 || !IsCanonical(claim.NumberKey))
                throw InvalidSnapshot();

            if (claim.InvoiceId == invoiceId &&
                (claim.CompanyId != companyId || claim.SupplierId != supplierId))
                throw InvalidSnapshot();

            if (claim.CompanyId != companyId || claim.SupplierId != supplierId)
                continue;

            if (!allKeys.Add(claim.NumberKey))
                throw InvalidSnapshot();

            if (claim.InvoiceId == invoiceId)
            {
                ownKeys.Add(claim.NumberKey);
                if (claim.IsActive)
                {
                    activeCount++;
                    if (!string.Equals(claim.NumberKey, revision.OldKey, StringComparison.Ordinal))
                        throw InvalidSnapshot();
                }
            }
            else if (string.Equals(claim.NumberKey, revision.NewKey, StringComparison.Ordinal))
                throw new ConflictException("PUR.INVOICE.NUMBER_EXISTS",
                    "Supplier invoice number is already used by this supplier.");
        }

        if (activeCount != 1 || !ownKeys.Contains(revision.OldKey))
            throw InvalidSnapshot();

        var steps = new List<PurchaseInvoiceNumberClaimWriteStep>();
        if (revision.Kind == PurchaseInvoiceNumberRevisionKind.Changed)
        {
            steps.Add(new(PurchaseInvoiceNumberClaimWriteKind.Deactivate, revision.OldKey));
            steps.Add(new(ownKeys.Contains(revision.NewKey)
                ? PurchaseInvoiceNumberClaimWriteKind.ActivateExisting
                : PurchaseInvoiceNumberClaimWriteKind.InsertActive, revision.NewKey));
            ownKeys.Add(revision.NewKey);
        }

        return new(Array.AsReadOnly(steps.ToArray()),
            Array.AsReadOnly(ownKeys.Order(StringComparer.Ordinal).ToArray()));
    }

    /// <summary>Translate only collisions on the permanent company/supplier/number index.</summary>
    public static bool IsNumberConflict(int sqlErrorNumber, string? sqlMessage) =>
        sqlErrorNumber is 2601 or 2627 &&
        sqlMessage?.Contains(NumberScopeIndex, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsCanonical(string? key)
    {
        try { return key is not null && PurchaseInvoiceNumber.Normalize(key).Key == key; }
        catch (BusinessRuleException) { return false; }
    }

    private static BusinessRuleException InvalidSnapshot() =>
        new("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT", "Supplier invoice number claim snapshot is invalid.");
}
