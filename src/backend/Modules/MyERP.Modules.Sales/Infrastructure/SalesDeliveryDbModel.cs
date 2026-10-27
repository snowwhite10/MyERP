using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

public sealed class SalesDeliveryDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesDelivery>(e =>
        {
            e.ToTable("sal_delivery", t =>
            {
                t.HasCheckConstraint("ck_sal_delivery_status", "[status] IN ('Draft', 'Pending', 'Rejected', 'Posted')");
                t.HasCheckConstraint("ck_sal_delivery_order", "[order_id] > 0");
                t.HasCheckConstraint("ck_sal_delivery_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                t.HasCheckConstraint("ck_sal_delivery_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.OrderId, x.Status });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.DeliveryId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalesDeliveryLine>(e =>
        {
            e.ToTable("sal_delivery_line", t =>
            {
                t.HasCheckConstraint("ck_sal_delivery_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_sal_delivery_line_refs", "[order_line_id] > 0 AND [item_id] > 0 AND [warehouse_id] > 0 AND ([location_id] IS NULL OR [location_id] > 0)");
                t.HasCheckConstraint("ck_sal_delivery_line_quantity", "[quantity] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.DeliveryId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.OrderLineId, x.DeliveryId });
            e.HasIndex(x => new { x.CompanyId, x.ItemId });
            e.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.LocationId });
        });
    }
}
