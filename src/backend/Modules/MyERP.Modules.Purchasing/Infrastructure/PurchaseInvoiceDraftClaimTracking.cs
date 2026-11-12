using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>Prepares a validated draft's number claims in the caller's transaction DbContext.</summary>
public static class PurchaseInvoiceDraftClaimTracking
{
    public static PurchaseInvoiceNumberClaimApplyResult Prepare(DbContext db,
        PurchaseInvoiceDraftEntityPlan plan)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(plan);

        plan.ValidateBeforeSave();
        var tracked = db.ChangeTracker.Entries<PurchaseInvoiceNumberClaim>()
            .Where(e => e.State != EntityState.Detached).ToArray();
        foreach (var claim in plan.NumberClaims.TrackedClaims)
        {
            if (claim.Id <= 0 || tracked.Any(e => e.Entity.Id == claim.Id &&
                !ReferenceEquals(e.Entity, claim)))
                throw new InvalidOperationException("A different invoice number claim is already tracked.");
            var entry = tracked.SingleOrDefault(e => ReferenceEquals(e.Entity, claim));
            if (entry is not null && entry.State != EntityState.Unchanged)
                throw new InvalidOperationException("Invoice number claim has pending EF changes.");
        }
        foreach (var claim in plan.NumberClaims.Changes
            .Where(x => x.Kind == PurchaseInvoiceNumberClaimWriteKind.InsertActive)
            .Select(x => x.Entity))
        {
            if (tracked.Any(e => ReferenceEquals(e.Entity, claim) ||
                (claim.Id > 0 && e.Entity.Id == claim.Id)))
                throw new InvalidOperationException("New invoice number claim is already tracked.");
        }

        // Attach before changing flags, so EF captures the original IsActive values.
        foreach (var claim in plan.NumberClaims.TrackedClaims)
            if (!tracked.Any(e => ReferenceEquals(e.Entity, claim)))
                db.Attach(claim);

        var result = plan.ApplyNumberClaims();
        db.AddRange(result.ClaimsToInsert);
        db.ChangeTracker.DetectChanges();
        return result;
    }
}
