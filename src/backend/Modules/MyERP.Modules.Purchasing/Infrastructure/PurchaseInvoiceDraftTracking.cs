using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;

namespace MyERP.Modules.Purchasing.Infrastructure;

/// <summary>Prepares one validated draft graph in the caller's transaction context.</summary>
public static class PurchaseInvoiceDraftTracking
{
    public static void Prepare(DbContext db, PurchaseInvoiceDraftEntityPlan plan,
        PurchaseInvoice? saved = null, IReadOnlyCollection<PurchaseInvoiceLine>? savedLines = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(plan);
        plan.ValidateBeforeSave();

        var invoice = plan.Invoice;
        var revision = invoice.Version > 0;
        var trackedHeaders = db.ChangeTracker.Entries<PurchaseInvoice>()
            .Where(e => e.State != EntityState.Detached).ToArray();
        if (revision)
        {
            if (saved is null || savedLines is null || !ReferenceEquals(
                    trackedHeaders.SingleOrDefault(e => e.Entity.Id == invoice.Id)?.Entity, saved) ||
                db.Entry(saved).State != EntityState.Unchanged ||
                saved.Id != invoice.Id || saved.CompanyId != invoice.CompanyId ||
                saved.SupplierId != invoice.SupplierId ||
                saved.PurchaseOrderId != invoice.PurchaseOrderId ||
                saved.Status != PurchaseInvoiceStatus.Draft || saved.IsDeleted ||
                saved.Version != invoice.Version)
                throw new InvalidOperationException("Saved draft header is not the unchanged tracked version.");
            var trackedLines = db.ChangeTracker.Entries<PurchaseInvoiceLine>()
                .Where(e => e.State != EntityState.Detached && e.Entity.InvoiceId == invoice.Id).ToArray();
            if (savedLines.Count != trackedLines.Length ||
                savedLines.Any(line => line.Id <= 0 ||
                    !trackedLines.Any(e => ReferenceEquals(e.Entity, line) &&
                        e.State == EntityState.Unchanged && e.Entity.CompanyId == invoice.CompanyId)) ||
                trackedLines.Any(e => !savedLines.Any(line => ReferenceEquals(line, e.Entity))))
                throw new InvalidOperationException("Saved draft lines are not the complete unchanged tracked set.");
        }
        else if (saved is not null || savedLines is not null ||
                 trackedHeaders.Any(e => e.Entity.Id == invoice.Id))
            throw new InvalidOperationException("New draft header is already tracked.");

        if (db.ChangeTracker.Entries<PurchaseInvoiceLine>().Any(e =>
            invoice.Lines.Any(line => ReferenceEquals(line, e.Entity) ||
                (line.Id > 0 && e.Entity.Id == line.Id))))
            throw new InvalidOperationException("Replacement draft line is already tracked.");

        if (revision)
        {
            PurchaseInvoiceDraftClaimTracking.Prepare(db, plan);
            saved!.Currency = invoice.Currency;
            saved.BusinessDate = invoice.BusinessDate;
            saved.SupplierInvoiceNumber = invoice.SupplierInvoiceNumber;
            saved.NumberKey = invoice.NumberKey;
            saved.TotalNet = invoice.TotalNet;
            saved.TotalTax = invoice.TotalTax;
            saved.TotalGross = invoice.TotalGross;
            db.RemoveRange(savedLines!);
            db.AddRange(invoice.Lines);
        }
        else
        {
            // The plan's positive ID only binds its validated rows. SQL IDENTITY owns
            // the persisted key; EF propagates its temporary key to both dependents.
            var claims = plan.ApplyNumberClaims();
            invoice.Id = 0;
            foreach (var line in invoice.Lines)
                line.InvoiceId = 0;
            db.Add(invoice);
            foreach (var claim in claims.ClaimsToInsert)
            {
                claim.InvoiceId = 0;
                claim.Invoice = invoice;
                db.Add(claim);
            }
        }
        db.ChangeTracker.DetectChanges();
    }
}
