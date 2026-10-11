using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

public sealed class CustomerCategoryDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder) =>
        MasterCategoryModel.Configure<CustomerCategory>(modelBuilder, "md_customer_category");
}

public sealed class SupplierCategoryDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<SupplierCategory>(modelBuilder, "md_supplier_category");
        // 供應商以 (company_id, category_id) 複合外鍵參照，資料庫層阻止跨公司分類。
        modelBuilder.Entity<SupplierCategory>().HasAlternateKey(x => new { x.CompanyId, x.Id });
    }
}

public sealed class ItemBrandDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<ItemBrand>(modelBuilder, "md_item_brand");
        // 物料（WU-MD-012）以 (company_id, brand_id) 複合外鍵參照。
        modelBuilder.Entity<ItemBrand>().HasAlternateKey(x => new { x.CompanyId, x.Id });
    }
}

/// <summary>物料分類樹；上層以 (company_id, parent_id) 複合外鍵參照同公司分類，資料庫層阻止跨公司掛載。</summary>
public sealed class ItemCategoryDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<ItemCategory>(modelBuilder, "md_item_category");
        modelBuilder.Entity<ItemCategory>(e =>
        {
            e.ToTable("md_item_category", t => t.HasCheckConstraint("ck_md_item_category_parent", "[parent_id] <> [id]"));
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.ParentId }).HasDatabaseName("ix_md_item_category_company_id_parent_id");
            e.HasOne<ItemCategory>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ParentId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}

internal static class MasterCategoryModel
{
    public static void Configure<TCategory>(ModelBuilder modelBuilder, string table) where TCategory : MasterCategory =>
        modelBuilder.Entity<TCategory>(e =>
        {
            e.ToTable(table, t => t.HasCheckConstraint(
                $"ck_{table}_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName($"ux_{table}_company_id_code");
            e.HasIndex(x => new { x.CompanyId, x.Name }).HasDatabaseName($"ix_{table}_company_id_name");
        });
}
