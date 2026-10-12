using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>物料主檔與物料專屬換算；參照均以 (company_id, id) 複合外鍵限同公司。</summary>
public sealed class ItemDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureItem(modelBuilder);
        modelBuilder.Entity<ItemUomConversion>(e =>
        {
            e.ToTable("md_item_uom_conversion", t => t.HasCheckConstraint("ck_md_item_uom_conversion_factor", "[factor] > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Factor).HasPrecision(ItemUomConversion.FactorPrecision, ItemUomConversion.FactorScale);
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.UomId }).IsUnique().HasFilter("[is_deleted] = 0")
                .HasDatabaseName("ux_md_item_uom_conversion_company_id_item_id_uom_id");
            e.HasIndex(x => new { x.CompanyId, x.UomId }).HasDatabaseName("ix_md_item_uom_conversion_company_id_uom_id");
            e.HasOne(x => x.Item).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ItemId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Uom).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.UomId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        ConfigureImage(modelBuilder);
    }

    private static void ConfigureImage(ModelBuilder modelBuilder) => modelBuilder.Entity<ItemImage>(e =>
    {
        e.ToTable("md_item_image", t =>
        {
            t.HasCheckConstraint("ck_md_item_image_content_type", "[content_type] IN ('image/png', 'image/jpeg')");
            t.HasCheckConstraint("ck_md_item_image_sort_order", $"[sort_order] BETWEEN 0 AND {ItemImage.MaxSortOrder}");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();
        e.Property(x => x.ContentType).HasMaxLength(30).IsUnicode(false);
        e.Property(x => x.FileName).HasMaxLength(ItemImage.MaxFileNameLength);
        e.Property(x => x.Content).IsRequired();
        e.HasIndex(x => new { x.CompanyId, x.ItemId, x.SortOrder }).HasDatabaseName("ix_md_item_image_company_id_item_id_sort_order");
        e.HasIndex(x => new { x.CompanyId, x.ItemId }).IsUnique().HasFilter("[is_primary] = 1")
            .HasDatabaseName("ux_md_item_image_company_id_item_id_primary");
        e.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ItemId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    });

    private static void ConfigureItem(ModelBuilder modelBuilder) => modelBuilder.Entity<Item>(e =>
    {
        e.ToTable("md_item");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();
        e.HasAlternateKey(x => new { x.CompanyId, x.Id });
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
