using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class TransferDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transfer>(e =>
        {
            e.ToTable("inv_transfer", t =>
            {
                t.HasCheckConstraint("ck_inv_transfer_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                t.HasCheckConstraint("ck_inv_transfer_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Posted')");
                t.HasCheckConstraint("ck_inv_transfer_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                t.HasCheckConstraint("ck_inv_transfer_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.TransferId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransferLine>(e =>
        {
            e.ToTable("inv_transfer_line", t =>
            {
                t.HasCheckConstraint("ck_inv_transfer_line_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_inv_transfer_line_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_inv_transfer_line_distinct", "[source_warehouse_id] <> [destination_warehouse_id] OR ISNULL([source_location_id], -1) <> ISNULL([destination_location_id], -1)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.TransferId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.ItemId });
            e.HasIndex(x => new { x.CompanyId, x.SourceWarehouseId, x.SourceLocationId });
            e.HasIndex(x => new { x.CompanyId, x.DestinationWarehouseId, x.DestinationLocationId });
            e.HasMany(x => x.Tracking).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.LineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransferLineTracking>(e =>
        {
            e.ToTable("inv_transfer_line_tracking", t =>
            {
                t.HasCheckConstraint("ck_inv_transfer_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1)");
                t.HasCheckConstraint("ck_inv_transfer_line_tracking_quantity", "[quantity] > 0");
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
