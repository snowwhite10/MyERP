using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class SafetyStockDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<SafetyStockThreshold>(e =>
    {
        e.ToTable("inv_safety_stock_threshold", t =>
        {
            t.HasCheckConstraint("ck_inv_safety_stock_threshold_quantity", "[minimum_quantity] > 0");
            t.HasCheckConstraint("ck_inv_safety_stock_threshold_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL AND [deleted_by] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [deleted_by] IS NOT NULL)");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();
        e.Property(x => x.MinimumQuantity).HasPrecision(19, 6);
        e.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId }).IsUnique();
        e.HasIndex(x => new { x.CompanyId, x.Id })
            .IncludeProperties(x => new { x.IsDeleted, x.ItemId, x.WarehouseId })
            .HasDatabaseName("ix_inv_safety_stock_threshold_company_id_id");
        e.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.ItemId, x.Id });
    });
}
