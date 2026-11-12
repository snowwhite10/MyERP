using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>A pending EF change. Existing rows must be tracked by the writer's transaction.</summary>
public sealed class PurchaseInvoiceNumberClaimEntityChange
{
    private readonly long companyId;
    private readonly long supplierId;
    private readonly long invoiceId;
    private readonly string numberKey;
    private readonly bool expectedActive;

    internal PurchaseInvoiceNumberClaimEntityChange(PurchaseInvoiceNumberClaimWriteKind kind,
        PurchaseInvoiceNumberClaim entity, long companyId, long supplierId, long invoiceId,
        string numberKey, bool expectedActive)
    {
        Kind = kind;
        Entity = entity;
        this.companyId = companyId;
        this.supplierId = supplierId;
        this.invoiceId = invoiceId;
        this.numberKey = numberKey;
        this.expectedActive = expectedActive;
    }

    public PurchaseInvoiceNumberClaimWriteKind Kind { get; }
    public PurchaseInvoiceNumberClaim Entity { get; }

    /// <summary>Apply a tracked-row flag change immediately before SaveChanges; insert rows are already populated.</summary>
    public void Apply()
    {
        if (Entity.CompanyId != companyId || Entity.SupplierId != supplierId ||
            Entity.InvoiceId != invoiceId || Entity.NumberKey != numberKey ||
            Entity.IsActive != expectedActive ||
            (Kind == PurchaseInvoiceNumberClaimWriteKind.InsertActive ? Entity.Id != 0 : Entity.Id <= 0))
            throw InvalidSnapshot();

        if (Kind != PurchaseInvoiceNumberClaimWriteKind.InsertActive)
            Entity.IsActive = Kind == PurchaseInvoiceNumberClaimWriteKind.ActivateExisting;
    }

    internal static BusinessRuleException InvalidSnapshot() => new("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT",
        "Supplier invoice number claim snapshot is invalid.");
}

/// <summary>Ordered EF entity changes derived from a complete, locked claim snapshot.</summary>
public sealed class PurchaseInvoiceNumberClaimEntityPlan
{
    private PurchaseInvoiceNumberClaimEntityPlan(IReadOnlyList<PurchaseInvoiceNumberClaimEntityChange> changes) =>
        Changes = changes;

    public IReadOnlyList<PurchaseInvoiceNumberClaimEntityChange> Changes { get; }

    public static PurchaseInvoiceNumberClaimEntityPlan ForNew(long companyId, long supplierId,
        long invoiceId, string numberKey, IReadOnlyCollection<PurchaseInvoiceNumberClaim>? claims) =>
        Build(companyId, supplierId, invoiceId, numberKey, null, claims);

    public static PurchaseInvoiceNumberClaimEntityPlan ForRevision(long companyId, long supplierId,
        long invoiceId, PurchaseInvoiceNumberRevision revision,
        IReadOnlyCollection<PurchaseInvoiceNumberClaim>? claims) =>
        Build(companyId, supplierId, invoiceId, null, revision, claims);

    private static PurchaseInvoiceNumberClaimEntityPlan Build(long companyId, long supplierId,
        long invoiceId, string? numberKey, PurchaseInvoiceNumberRevision? revision,
        IReadOnlyCollection<PurchaseInvoiceNumberClaim>? claims)
    {
        if (claims is null)
            throw PurchaseInvoiceNumberClaimEntityChange.InvalidSnapshot();
        var rows = claims.ToArray();
        if (rows.Any(x => x is null || x.Id <= 0) ||
            rows.Select(x => x.Id).Distinct().Count() != rows.Length)
            throw PurchaseInvoiceNumberClaimEntityChange.InvalidSnapshot();

        var states = rows.Select(x => new PurchaseInvoiceNumberClaimState(
            x.CompanyId, x.SupplierId, x.InvoiceId, x.NumberKey, x.IsActive)).ToArray();
        var writePlan = revision is null
            ? PurchaseInvoiceNumberClaimWritePlan.ForNew(companyId, supplierId, invoiceId, numberKey, states)
            : PurchaseInvoiceNumberClaimWritePlan.ForRevision(companyId, supplierId, invoiceId, revision, states);

        var changes = new List<PurchaseInvoiceNumberClaimEntityChange>();
        foreach (var step in writePlan.Steps)
        {
            var existing = rows.Where(x => x.CompanyId == companyId &&
                x.SupplierId == supplierId && x.InvoiceId == invoiceId &&
                x.NumberKey == step.NumberKey).ToArray();
            if (step.Kind == PurchaseInvoiceNumberClaimWriteKind.InsertActive)
            {
                if (existing.Length != 0)
                    throw PurchaseInvoiceNumberClaimEntityChange.InvalidSnapshot();
                changes.Add(new(step.Kind, new PurchaseInvoiceNumberClaim
                {
                    CompanyId = companyId, SupplierId = supplierId, InvoiceId = invoiceId,
                    NumberKey = step.NumberKey, IsActive = true
                }, companyId, supplierId, invoiceId, step.NumberKey, true));
            }
            else
            {
                var expectedActive = step.Kind == PurchaseInvoiceNumberClaimWriteKind.Deactivate;
                if (existing.Length != 1 || existing[0].IsActive != expectedActive)
                    throw PurchaseInvoiceNumberClaimEntityChange.InvalidSnapshot();
                changes.Add(new(step.Kind, existing[0], companyId, supplierId, invoiceId,
                    step.NumberKey, expectedActive));
            }
        }
        return new(Array.AsReadOnly(changes.ToArray()));
    }
}
