using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class InventoryReferenceDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<InventoryReference>(e =>
    {
        e.ToTable("inv_reference", t => t.HasCheckConstraint("ck_inv_reference_location", "([location_id] IS NULL AND [location_code] IS NULL) OR ([location_id] IS NOT NULL AND [location_code] IS NOT NULL)"));
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();
        e.Property(x => x.SourceKey).HasMaxLength(100).IsUnicode(false);
        e.Property(x => x.ItemCode).HasMaxLength(30).IsUnicode(false);
        e.Property(x => x.WarehouseCode).HasMaxLength(30).IsUnicode(false);
        e.Property(x => x.LocationCode).HasMaxLength(30).IsUnicode(false);
        e.HasIndex(x => new { x.CompanyId, x.SourceKey }).IsUnique();
        e.HasIndex(x => new { x.CompanyId, x.ItemCode });
        e.HasIndex(x => new { x.CompanyId, x.WarehouseCode, x.LocationCode });
        e.HasIndex(x => new { x.CompanyId, x.ItemId });
        e.HasIndex(x => new { x.CompanyId, x.WarehouseId });
        e.HasIndex(x => new { x.CompanyId, x.LocationId });
    });
}
