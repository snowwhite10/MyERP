using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

/// <summary>REQ_DS43.151–153：盤點表頭、結存鍵明細與批序號明細。</summary>
public sealed class StocktakeDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Stocktake>(e =>
        {
            e.ToTable("inv_stocktake", t =>
            {
                t.HasCheckConstraint("ck_inv_stocktake_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                t.HasCheckConstraint("ck_inv_stocktake_scope_json", "ISJSON([scope_json]) = 1");
                t.HasCheckConstraint("ck_inv_stocktake_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Posted')");
                t.HasCheckConstraint("ck_inv_stocktake_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                t.HasCheckConstraint("ck_inv_stocktake_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.ScopeJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Remark).HasMaxLength(1000);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.StocktakeId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StocktakeLine>(e =>
        {
            e.ToTable("inv_stocktake_line", t =>
            {
                t.HasCheckConstraint("ck_inv_stocktake_line_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_inv_stocktake_line_quantity", "[book_quantity] >= 0 AND ([counted_quantity] IS NULL OR [counted_quantity] >= 0)");
                t.HasCheckConstraint("ck_inv_stocktake_line_added", "[is_added] = 0 OR [book_quantity] = 0");
                t.HasCheckConstraint("ck_inv_stocktake_line_tracking_mode", "[tracking_mode] IN ('None', 'Lot', 'Serial')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.TrackingMode).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.BookQuantity).HasPrecision(19, 6);
            e.Property(x => x.CountedQuantity).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.StocktakeId, x.LineNo }).IsUnique();
            // 無儲位亦視為同一鍵：取消 EF 對可為 NULL 欄位的預設篩選，讓 SQL Server 唯一索引把 NULL 當成相同值
            e.HasIndex(x => new { x.CompanyId, x.StocktakeId, x.ItemId, x.WarehouseId, x.LocationId }).IsUnique().HasFilter(null);
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId });
            e.HasMany(x => x.Tracking).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.LineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StocktakeLineTracking>(e =>
        {
            e.ToTable("inv_stocktake_line_tracking", t =>
            {
                t.HasCheckConstraint("ck_inv_stocktake_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL)");
                t.HasCheckConstraint("ck_inv_stocktake_line_tracking_quantity", "[book_quantity] >= 0 AND ([counted_quantity] IS NULL OR [counted_quantity] >= 0)");
                t.HasCheckConstraint("ck_inv_stocktake_line_tracking_serial", "[serial_no] IS NULL OR ([book_quantity] IN (0, 1) AND ([counted_quantity] IS NULL OR [counted_quantity] IN (0, 1)))");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.LotNo).HasMaxLength(StockLot.MaxLength);
            e.Property(x => x.SerialNo).HasMaxLength(StockSerial.MaxLength);
            e.Property(x => x.BookQuantity).HasPrecision(19, 6);
            e.Property(x => x.CountedQuantity).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.SeqNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.LotNo }).IsUnique().HasFilter("[lot_no] IS NOT NULL");
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.SerialNo }).IsUnique().HasFilter("[serial_no] IS NOT NULL");
        });
    }
}
