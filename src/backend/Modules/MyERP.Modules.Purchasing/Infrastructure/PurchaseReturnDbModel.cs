using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class PurchaseReturnDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseReturn>(e =>
        {
            e.ToTable("pur_purchase_return", t =>
            {
                t.HasCheckConstraint("ck_pur_return_status", "[status] IN ('Draft','PendingApproval','Rejected','Posted')");
                t.HasCheckConstraint("ck_pur_return_order", "[purchase_order_id] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(50);
            e.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("ak_pur_purchase_return_company_id_id");
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId, x.Status, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.OwnerId, x.Id });
            e.HasOne<PurchaseOrderSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .HasConstraintName("fk_pur_purchase_return_order")
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseReturnLine>(e =>
        {
            e.ToTable("pur_purchase_return_line", t =>
            {
                t.HasCheckConstraint("ck_pur_return_line_quantity",
                    "[purchase_return_id] > 0 AND [receipt_line_id] > 0 AND [original_movement_id] > 0 AND [line_no] > 0 AND [material_id] > 0 AND [warehouse_id] > 0 AND [quantity] > 0 AND ([original_unit_cost] IS NULL OR [original_unit_cost] >= 0)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.OriginalUnitCost).HasPrecision(19, 6);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("ak_pur_purchase_return_line_company_id_id");
            e.HasIndex(x => new { x.CompanyId, x.PurchaseReturnId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.ReceiptLineId });
            e.HasOne<PurchaseReturn>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseReturnId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .HasConstraintName("fk_pur_purchase_return_line_return")
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseReceiptSourceLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ReceiptLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .HasConstraintName("fk_pur_purchase_return_line_receipt_line")
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseReturnLineTracking>(e =>
        {
            e.ToTable("pur_purchase_return_line_tracking", t =>
            {
                t.HasCheckConstraint("ck_pur_return_tracking_target",
                    "([lot_no] IS NOT NULL AND [serial_no] IS NULL AND LEN([lot_no]) > 0 AND [quantity] > 0) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND LEN([serial_no]) > 0 AND [quantity] = 1)");
                t.HasCheckConstraint("ck_pur_return_tracking_seq", "[seq_no] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.LotNo).HasMaxLength(50);
            e.Property(x => x.SerialNo).HasMaxLength(100);
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.SeqNo }).IsUnique();
            e.HasOne<PurchaseReturnLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.LineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .HasConstraintName("fk_pur_purchase_return_tracking_line")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
