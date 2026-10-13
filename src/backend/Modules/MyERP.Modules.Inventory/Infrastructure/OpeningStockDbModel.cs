using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class OpeningStockDbModel : IModuleDbModel
{
    public const string BalanceKeyIndex = "ux_inv_opening_stock_line_balance_key";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OpeningStock>(e =>
        {
            e.ToTable("inv_opening_stock", t =>
                t.HasCheckConstraint("ck_inv_opening_stock_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.RequestKey).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.Ignore(x => x.TotalAmount);
            e.HasIndex(x => new { x.CompanyId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.OpeningDate, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.OpeningStockId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OpeningStockLine>(e =>
        {
            e.ToTable("inv_opening_stock_line", t =>
            {
                t.HasCheckConstraint("ck_inv_opening_stock_line_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_inv_opening_stock_line_unit_cost", "[unit_cost] >= 0 AND [amount] >= 0");
                t.HasCheckConstraint("ck_inv_opening_stock_line_line_no", "[line_no] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnitCost).HasPrecision(19, 6);
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.OpeningStockId, x.LineNo }).IsUnique();
            // 每個結存鍵一筆期初；不加篩選，SQL Server 唯一索引把 NULL 儲位視為同一值（ADR-059）
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId, x.LocationId }).IsUnique().HasFilter(null)
                .HasDatabaseName(BalanceKeyIndex);
            e.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.LocationId });
            e.HasMany(x => x.Tracking).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.LineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OpeningStockLineTracking>(e =>
        {
            e.ToTable("inv_opening_stock_line_tracking", t =>
            {
                t.HasCheckConstraint("ck_inv_opening_stock_line_tracking_target",
                    "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1 AND [manufacture_date] IS NULL AND [expiry_date] IS NULL)");
                t.HasCheckConstraint("ck_inv_opening_stock_line_tracking_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_inv_opening_stock_line_tracking_dates",
                    "[expiry_date] IS NULL OR [manufacture_date] IS NULL OR [expiry_date] >= [manufacture_date]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.LotNo).HasMaxLength(StockLot.MaxLength);
            e.Property(x => x.SerialNo).HasMaxLength(StockSerial.MaxLength);
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.SeqNo }).IsUnique();
        });
    }
}
