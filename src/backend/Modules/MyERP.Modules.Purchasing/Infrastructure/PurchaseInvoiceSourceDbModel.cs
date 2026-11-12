using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class PurchaseInvoiceSourceDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseOrderSource>(e =>
        {
            e.ToTable("pur_purchase_order", t =>
            {
                t.HasCheckConstraint("ck_pur_order_supplier", "[supplier_id] > 0");
                t.HasCheckConstraint("ck_pur_order_status", "[status] IN ('Draft','PendingApproval','Approved','Rejected','Cancelled')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.Status, x.Id });
        });
        modelBuilder.Entity<PurchaseOrderSourceLine>(e =>
        {
            e.ToTable("pur_purchase_order_line", t =>
            {
                t.HasCheckConstraint("ck_pur_order_line_refs", "[purchase_order_id] > 0 AND [line_no] > 0 AND [material_id] > 0");
                t.HasCheckConstraint("ck_pur_order_line_values", "[quantity] > 0 AND [unit_price] >= 0 AND [tax_rate_percent] BETWEEN 0 AND 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnitPrice).HasPrecision(19, 6);
            e.Property(x => x.TaxRatePercent).HasPrecision(9, 4);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.MaterialId });
            e.HasOne<PurchaseOrderSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseReceiptSource>(e =>
        {
            e.ToTable("pur_purchase_receipt", t =>
            {
                t.HasCheckConstraint("ck_pur_receipt_refs", "[purchase_order_id] > 0 AND [supplier_id] > 0");
                t.HasCheckConstraint("ck_pur_receipt_status", "[status] IN ('Draft','PendingApproval','Rejected','Posted')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId, x.Status, x.Id });
            e.HasOne<PurchaseOrderSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseReceiptSourceLine>(e =>
        {
            e.ToTable("pur_purchase_receipt_line", t =>
            {
                t.HasCheckConstraint("ck_pur_receipt_line_refs", "[purchase_receipt_id] > 0 AND [purchase_order_line_id] > 0 AND [line_no] > 0 AND [material_id] > 0");
                t.HasCheckConstraint("ck_pur_receipt_line_quantities", "[posted_received] > 0 AND [posted_returned] >= 0 AND [pending_return_reserved] >= 0 AND [posted_invoiced] >= 0 AND [matched_reserved] >= 0 AND [posted_returned]+[pending_return_reserved]+[posted_invoiced]+[matched_reserved] <= [posted_received]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.PostedReceived).HasPrecision(19, 6);
            e.Property(x => x.PostedReturned).HasPrecision(19, 6);
            e.Property(x => x.PendingReturnReserved).HasPrecision(19, 6);
            e.Property(x => x.PostedInvoiced).HasPrecision(19, 6);
            e.Property(x => x.MatchedReserved).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.PurchaseReceiptId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderLineId });
            e.HasIndex(x => new { x.CompanyId, x.MaterialId });
            e.HasOne<PurchaseReceiptSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseReceiptId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrderSourceLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
