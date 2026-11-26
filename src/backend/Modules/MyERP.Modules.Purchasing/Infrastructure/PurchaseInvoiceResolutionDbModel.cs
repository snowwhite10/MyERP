using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class PurchaseInvoiceResolutionDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseInvoiceResolutionRecord>(e =>
        {
            e.ToTable("pur_purchase_invoice_resolution", t =>
            {
                t.HasCheckConstraint("ck_pur_invoice_resolution_refs", "[invoice_id] > 0 AND [invoice_version] > 0 AND [line_no] > 0");
                t.HasCheckConstraint("ck_pur_invoice_resolution_action", "[action] IN ('Accept', 'Reject')");
                t.HasCheckConstraint("ck_pur_invoice_resolution_reason", "LEN([reason]) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Action).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.NetDifference).HasPrecision(19, 4);
            e.Property(x => x.TaxDifference).HasPrecision(19, 4);
            e.Property(x => x.GrossDifference).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.InvoiceVersion, x.LineNo }).IsUnique()
                .HasDatabaseName("ux_pur_purchase_invoice_resolution_company_id_invoice_id_invoice_version_line_no");
            e.HasOne<PurchaseInvoice>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .HasConstraintName("fk_pur_purchase_invoice_resolution_invoice")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
