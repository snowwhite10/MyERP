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
                // WU-PUR-008b2 (ADR-206): the rate snapshot is all-or-nothing; base-currency orders use rate 1.
                // Every comparison is guarded by IS NOT NULL: a CHECK that evaluates to UNKNOWN passes.
                t.HasCheckConstraint("ck_pur_order_rate_snapshot",
                    "([base_currency_code] IS NULL AND [exchange_rate] IS NULL) OR ([base_currency_code] IS NOT NULL AND [exchange_rate] IS NOT NULL AND [exchange_rate] > 0 AND ([currency] <> [base_currency_code] OR [exchange_rate] = 1))");
                t.HasCheckConstraint("ck_pur_order_conversion_amount", "([base_net] IS NULL AND [base_tax] IS NULL AND [base_gross] IS NULL) OR ([base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax])");
                t.HasCheckConstraint("ck_pur_order_consumption_state", "[requisition_consumption_active] = 0 OR ([is_deleted] = 0 AND [status] IN ('PendingApproval','Approved'))");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            e.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.DocumentNo).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.BaseNet).HasPrecision(19, 4);
            e.Property(x => x.BaseTax).HasPrecision(19, 4);
            e.Property(x => x.BaseGross).HasPrecision(19, 4);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("ak_pur_purchase_order_company_id_id");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.Status, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.RfqId });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id });
        });
        modelBuilder.Entity<PurchaseOrderSourceLine>(e =>
        {
            e.ToTable("pur_purchase_order_line", t =>
            {
                t.HasCheckConstraint("ck_pur_order_line_refs", "[purchase_order_id] > 0 AND [line_no] > 0 AND [material_id] > 0");
                t.HasCheckConstraint("ck_pur_order_line_values", "[quantity] > 0 AND [unit_price] >= 0 AND [tax_rate_percent] BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_pur_order_line_conversion_amount", "([base_net] IS NULL AND [base_tax] IS NULL AND [base_gross] IS NULL) OR ([base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax])");
                t.HasCheckConstraint("ck_pur_order_line_original_amount", "([original_net] IS NULL AND [original_tax] IS NULL AND [original_gross] IS NULL) OR ([original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax])");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnitPrice).HasPrecision(19, 6);
            e.Property(x => x.TaxRatePercent).HasPrecision(9, 4);
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            e.Property(x => x.BaseNet).HasPrecision(19, 4);
            e.Property(x => x.BaseTax).HasPrecision(19, 4);
            e.Property(x => x.BaseGross).HasPrecision(19, 4);
            e.Property(x => x.OriginalNet).HasPrecision(19, 4);
            e.Property(x => x.OriginalTax).HasPrecision(19, 4);
            e.Property(x => x.OriginalGross).HasPrecision(19, 4);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("ak_pur_purchase_order_line_company_id_id");
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.MaterialId });
            e.HasIndex(x => new { x.CompanyId, x.RequisitionLineId });
            e.HasIndex(x => new { x.CompanyId, x.TaxRateId });
            e.HasOne<PurchaseOrderSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RequisitionLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.RequisitionLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseOrderAwardSource>(e =>
        {
            e.ToTable("pur_order_source", t => t.HasCheckConstraint("ck_pur_order_source_refs",
                "[purchase_order_line_id] > 0 AND [award_id] > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.CompanyId, x.AwardId }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderLineId }).IsUnique();
            e.HasOne<PurchaseOrderSourceLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RfqAward>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.AwardId })
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
            e.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("ak_pur_purchase_receipt_company_id_id");
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
            e.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("ak_pur_purchase_receipt_line_company_id_id");
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
