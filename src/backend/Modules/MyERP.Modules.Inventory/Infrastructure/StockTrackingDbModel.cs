using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class StockTrackingDbModel : IModuleDbModel
{
    public const string ImmutableTrigger = "trg_inv_stock_movement_tracking_immutable";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockLot>(e =>
        {
            e.ToTable("inv_lot", t =>
            {
                t.HasCheckConstraint("ck_inv_lot_lot_no", "LEN([lot_no]) > 0");
                t.HasCheckConstraint("ck_inv_lot_dates", "[expiry_date] IS NULL OR [manufacture_date] IS NULL OR [expiry_date] >= [manufacture_date]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.LotNo).HasMaxLength(StockLot.MaxLength);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            // 物料屬 MD：只存 Id；唯一索引同時供追溯查詢依物料列出批號
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.LotNo }).IsUnique();
        });

        modelBuilder.Entity<StockSerial>(e =>
        {
            e.ToTable("inv_serial", t => t.HasCheckConstraint("ck_inv_serial_serial_no", "LEN([serial_no]) > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SerialNo).HasMaxLength(StockSerial.MaxLength);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.SerialNo }).IsUnique();
        });

        modelBuilder.Entity<StockMovementTracking>(e =>
        {
            e.ToTable("inv_stock_movement_tracking", t =>
            {
                t.HasCheckConstraint("ck_inv_stock_movement_tracking_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_inv_stock_movement_tracking_target",
                    "([lot_id] IS NOT NULL AND [serial_id] IS NULL) OR ([lot_id] IS NULL AND [serial_id] IS NOT NULL AND [quantity] = 1)");
                t.HasTrigger(ImmutableTrigger);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            // 同公司複合外鍵：明細只能指向本公司的異動、批號與序號
            e.HasOne(x => x.Movement).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.MovementId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.LotId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Serial).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.SerialId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            // 追溯匯出由異動時間排序時可能先走異動帳；覆蓋批序號條件與數量，免逐筆回表。
            e.HasIndex(x => new { x.CompanyId, x.MovementId })
                .IncludeProperties(x => new { x.LotId, x.SerialId, x.Quantity });
            // 批號／序號結存與追溯：依批序號找異動，覆蓋數量避免回表
            e.HasIndex(x => new { x.CompanyId, x.LotId }).IncludeProperties(x => new { x.MovementId, x.Quantity });
            e.HasIndex(x => new { x.CompanyId, x.SerialId }).IncludeProperties(x => new { x.MovementId, x.Quantity });
        });
    }
}
