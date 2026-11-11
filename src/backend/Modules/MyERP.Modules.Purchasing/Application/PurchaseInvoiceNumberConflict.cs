using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceNumberOwner(
    long CompanyId, long SupplierId, long InvoiceId, string NumberKey, bool IsDeleted);

/// <summary>
/// BR-PUR-036b: offline conflict decision. The caller must still enforce an unfiltered SQL
/// unique index on company, supplier and the normalized key under concurrent writes.
/// </summary>
public static class PurchaseInvoiceNumberConflict
{
    public static void EnsureAvailable(
        long companyId,
        long supplierId,
        long? currentInvoiceId,
        string? candidateNumber,
        IReadOnlyCollection<PurchaseInvoiceNumberOwner>? existingOwners)
    {
        var key = PurchaseInvoiceNumber.Normalize(candidateNumber).Key;
        if (companyId <= 0 || supplierId <= 0 || currentInvoiceId is <= 0 || existingOwners is null)
            throw InvalidSnapshot();

        var invoiceScopes = new Dictionary<long, (long CompanyId, long SupplierId)>();
        var seenInvoiceKeys = new HashSet<(long InvoiceId, string NumberKey)>();
        foreach (var owner in existingOwners)
        {
            if (owner is null || owner.CompanyId <= 0 || owner.SupplierId <= 0 || owner.InvoiceId <= 0 ||
                !IsCanonicalKey(owner.NumberKey) ||
                (invoiceScopes.TryGetValue(owner.InvoiceId, out var scope) &&
                 scope != (owner.CompanyId, owner.SupplierId)) ||
                !seenInvoiceKeys.Add((owner.InvoiceId, owner.NumberKey)))
                throw InvalidSnapshot();

            invoiceScopes[owner.InvoiceId] = (owner.CompanyId, owner.SupplierId);

            if (owner.CompanyId == companyId && owner.SupplierId == supplierId &&
                string.Equals(owner.NumberKey, key, StringComparison.Ordinal) &&
                owner.InvoiceId != currentInvoiceId)
                throw new ConflictException("PUR.INVOICE.NUMBER_EXISTS",
                    "Supplier invoice number is already used by this supplier.");
        }
    }

    private static bool IsCanonicalKey(string? key)
    {
        try { return key is not null && PurchaseInvoiceNumber.Normalize(key).Key == key; }
        catch (BusinessRuleException) { return false; }
    }

    private static BusinessRuleException InvalidSnapshot() =>
        new("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT", "Supplier invoice number ownership snapshot is invalid.");
}
