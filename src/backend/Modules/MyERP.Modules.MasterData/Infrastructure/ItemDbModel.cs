using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

public sealed class ItemDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Item>(e =>
    {
        e.ToTable("md_item");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();
        e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Specification).HasMaxLength(500);
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ux_md_item_company_id_code");
        e.HasIndex(x => new { x.CompanyId, x.Name }).HasDatabaseName("ix_md_item_company_id_name");
        e.HasOne(x => x.BaseUom).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BaseUomId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Brand).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BrandId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Category).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CategoryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    });
}
