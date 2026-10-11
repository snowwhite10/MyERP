using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

public sealed class CustomerDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerCategory>().HasAlternateKey(x => new { x.CompanyId, x.Id });
        BusinessPartnerModel.Configure<Customer, CustomerCategory>(modelBuilder, "md_customer");
    }
}

/// <summary>供應商主檔；分類備用鍵由 <see cref="SupplierCategoryDbModel"/> 宣告。</summary>
public sealed class SupplierDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder) =>
        BusinessPartnerModel.Configure<Supplier, SupplierCategory>(modelBuilder, "md_supplier");
}

internal static class BusinessPartnerModel
{
    public static void Configure<TPartner, TCategory>(ModelBuilder modelBuilder, string table)
        where TPartner : BusinessPartner<TCategory>
        where TCategory : MasterCategory =>
        modelBuilder.Entity<TPartner>(e =>
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
            // 以 (company_id, category_id) 複合外鍵參照分類備用鍵，資料庫層阻止跨公司分類及刪除已引用分類。
            e.HasOne(x => x.Category).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CategoryId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });
}
