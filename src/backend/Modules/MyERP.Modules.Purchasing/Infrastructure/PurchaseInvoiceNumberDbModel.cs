using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class PurchaseInvoiceNumberDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseInvoiceNumberClaim>(e =>
        {
            e.ToTable("pur_purchase_invoice_number_claim", t =>
            {
                t.HasCheckConstraint("ck_pur_invoice_number_claim_refs", "[company_id] > 0 AND [supplier_id] > 0 AND [invoice_id] > 0");
                t.HasCheckConstraint("ck_pur_invoice_number_claim_key", "LEN([number_key]) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasOne(x => x.Invoice).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.NumberKey).HasMaxLength(80).UseCollation("Latin1_General_100_BIN2");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.NumberKey }).IsUnique()
                .HasDatabaseName("ux_pur_purchase_invoice_number_claim_company_id_supplier_id_number_key");
            e.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.NumberKey }).IsUnique()
                .HasDatabaseName("ux_pur_purchase_invoice_number_claim_company_id_invoice_id_number_key");
            e.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.IsActive })
                .IsUnique().HasFilter("[is_active] = 1")
                .HasDatabaseName("ux_pur_purchase_invoice_number_claim_company_id_invoice_id_is_active");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.InvoiceId });
        });
    }
}
