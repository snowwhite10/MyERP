using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class AssemblyDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Assembly>(e =>
        {
            e.ToTable("inv_assembly", t =>
            {
                t.HasCheckConstraint("ck_inv_assembly_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Posted')");
                t.HasCheckConstraint("ck_inv_assembly_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                t.HasCheckConstraint("ck_inv_assembly_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
                t.HasCheckConstraint("ck_inv_assembly_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(1000);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.Id });
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.CompanyId, x.AssemblyId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AssemblyLine>(e =>
        {
            e.ToTable("inv_assembly_line", t =>
            {
                t.HasCheckConstraint("ck_inv_assembly_line_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_inv_assembly_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_inv_assembly_line_role", "[role] IN ('Finished', 'Component')");
                t.HasCheckConstraint("ck_inv_assembly_line_cost", "[posted_unit_cost] IS NULL OR [posted_unit_cost] >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.PostedUnitCost).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.AssemblyId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.AssemblyId, x.Role }).IsUnique().HasFilter("[role] = 'Finished'");
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId });
            e.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.LocationId });
            e.HasMany(x => x.Tracking).WithOne().HasForeignKey(x => new { x.CompanyId, x.LineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AssemblyLineTracking>(e =>
        {
            e.ToTable("inv_assembly_line_tracking", t =>
            {
                t.HasCheckConstraint("ck_inv_assembly_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1)");
                t.HasCheckConstraint("ck_inv_assembly_tracking_quantity", "[quantity] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.LotNo).HasMaxLength(StockLot.MaxLength);
            e.Property(x => x.SerialNo).HasMaxLength(StockSerial.MaxLength);
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.SeqNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.LotNo }).IsUnique().HasFilter("[lot_no] IS NOT NULL");
            e.HasIndex(x => new { x.CompanyId, x.LineId, x.SerialNo }).IsUnique().HasFilter("[serial_no] IS NOT NULL");
        });
    }
}
