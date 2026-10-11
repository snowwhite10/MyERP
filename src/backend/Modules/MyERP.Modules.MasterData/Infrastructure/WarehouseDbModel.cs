using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>倉庫與儲位；儲位以 (company_id, warehouse_id) 複合外鍵參照同公司倉庫。</summary>
public sealed class WarehouseDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<Warehouse>(modelBuilder, "md_warehouse");
        modelBuilder.Entity<Warehouse>(e =>
        {
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.Property(x => x.Address).HasMaxLength(Warehouse.AddressMaxLength);
            e.Property(x => x.ContactName).HasMaxLength(Warehouse.ContactNameMaxLength);
            e.Property(x => x.Phone).HasMaxLength(Warehouse.PhoneMaxLength);
        });
        modelBuilder.Entity<StorageLocation>(e =>
        {
            e.ToTable("md_storage_location");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.Code }).IsUnique()
                .HasDatabaseName("ux_md_storage_location_company_id_warehouse_id_code");
            e.HasOne(x => x.Warehouse).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.WarehouseId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
