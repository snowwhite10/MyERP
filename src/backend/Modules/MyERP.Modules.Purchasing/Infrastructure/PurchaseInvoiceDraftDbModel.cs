using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class PurchaseInvoiceDraftDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseInvoice>(e =>
        {
            e.ToTable("pur_purchase_invoice", t =>
            {
                t.HasCheckConstraint("ck_pur_invoice_header_refs", "[supplier_id] > 0 AND [purchase_order_id] > 0");
                t.HasCheckConstraint("ck_pur_invoice_header_totals", "[total_net] >= 0 AND [total_tax] >= 0 AND [total_gross] = [total_net] + [total_tax]");
                t.HasCheckConstraint("ck_pur_invoice_header_status", "[status] IN ('Draft', 'Matched', 'Disputed', 'Rejected', 'Posted')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.SupplierInvoiceNumber).HasMaxLength(80);
            e.Property(x => x.NumberKey).HasMaxLength(80).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.TotalNet).HasPrecision(19, 4);
            e.Property(x => x.TotalTax).HasPrecision(19, 4);
            e.Property(x => x.TotalGross).HasPrecision(19, 4);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId, x.Status, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseInvoiceLine>(e =>
        {
            e.ToTable("pur_purchase_invoice_line", t =>
            {
                t.HasCheckConstraint("ck_pur_invoice_line_refs", "[invoice_id] > 0 AND [line_no] > 0 AND [receipt_line_id] > 0");
                t.HasCheckConstraint("ck_pur_invoice_line_amounts", "[quantity] > 0 AND [unit_price] >= 0 AND [tax_rate_percent] BETWEEN 0 AND 100 AND [net] >= 0 AND [tax] >= 0 AND [gross] = [net] + [tax]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnitPrice).HasPrecision(19, 6);
            e.Property(x => x.TaxRatePercent).HasPrecision(9, 4);
            e.Property(x => x.Net).HasPrecision(19, 4);
            e.Property(x => x.Tax).HasPrecision(19, 4);
            e.Property(x => x.Gross).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.ReceiptLineId });
        });
    }
}
