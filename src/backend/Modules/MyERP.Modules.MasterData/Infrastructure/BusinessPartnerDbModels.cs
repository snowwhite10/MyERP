using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

public sealed class CustomerDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerCategory>().HasAlternateKey(x => new { x.CompanyId, x.Id });
        BusinessPartnerModel.Configure<Customer, CustomerCategory>(modelBuilder, "md_customer");
        modelBuilder.Entity<Customer>().HasAlternateKey(x => new { x.CompanyId, x.Id });
    }
}

/// <summary>客戶聯絡人與地址；以 (company_id, customer_id) 複合外鍵參照同公司客戶（ADR-043）。</summary>
public sealed class CustomerContactDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        PartnerChildModel.Configure<CustomerContact, Customer>(modelBuilder, "md_customer_contact", "customer_id");
        PartnerChildModel.ConfigureContact<CustomerContact>(modelBuilder);
        PartnerChildModel.Configure<CustomerAddress, Customer>(modelBuilder, "md_customer_address", "customer_id");
        modelBuilder.Entity<CustomerAddress>(e =>
        {
            e.Property(x => x.Address).HasMaxLength(CustomerAddress.AddressMaxLength);
            e.Property(x => x.PostalCode).HasMaxLength(CustomerAddress.PostalCodeMaxLength).IsUnicode(false);
            e.Property(x => x.Recipient).HasMaxLength(CustomerAddress.RecipientMaxLength);
            e.Property(x => x.Phone).HasMaxLength(CustomerAddress.PhoneMaxLength);
        });
    }
}

internal static class PartnerChildModel
{
    /// <summary>
    /// 子表共用設定：複合外鍵、外鍵索引、每個往來對象最多一筆預設的篩選唯一索引，
    /// 以及「預設必為啟用且未刪除」的 CHECK。
    /// </summary>
    public static void Configure<TChild, TPartner>(ModelBuilder modelBuilder, string table, string partnerColumn)
        where TChild : PartnerChild
        where TPartner : MasterEntity =>
        modelBuilder.Entity<TChild>(e =>
        {
            e.ToTable(table, t => t.HasCheckConstraint(
                $"ck_{table}_default_active", "[is_default] = 0 OR ([is_active] = 1 AND [is_deleted] = 0)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.PartnerId).HasColumnName(partnerColumn);
            // 同一欄位組合有兩個索引，須以具名索引區分並明確指定資料庫名稱。
            var index = $"ix_{table}_company_id_{partnerColumn}";
            var defaultIndex = $"ux_{table}_company_id_{partnerColumn}_default";
            e.HasIndex(x => new { x.CompanyId, x.PartnerId }, index).HasDatabaseName(index);
            e.HasIndex(x => new { x.CompanyId, x.PartnerId }, defaultIndex).HasDatabaseName(defaultIndex)
                .IsUnique().HasFilter("[is_default] = 1");
            e.HasOne<TPartner>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PartnerId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });

    public static void ConfigureContact<TContact>(ModelBuilder modelBuilder) where TContact : PartnerContact =>
        modelBuilder.Entity<TContact>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(PartnerContact.NameMaxLength);
            e.Property(x => x.Position).HasMaxLength(PartnerContact.PositionMaxLength);
            e.Property(x => x.Mobile).HasMaxLength(PartnerContact.PhoneMaxLength);
            e.Property(x => x.Phone).HasMaxLength(PartnerContact.PhoneMaxLength);
            e.Property(x => x.Email).HasMaxLength(PartnerContact.EmailMaxLength).IsUnicode(false);
            e.Property(x => x.MessagingId).HasMaxLength(PartnerContact.MessagingIdMaxLength);
            e.Property(x => x.Note).HasMaxLength(PartnerContact.NoteMaxLength);
        });
}

/// <summary>供應商主檔；分類備用鍵由 <see cref="SupplierCategoryDbModel"/> 宣告。</summary>
public sealed class SupplierDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        BusinessPartnerModel.Configure<Supplier, SupplierCategory>(modelBuilder, "md_supplier");
        modelBuilder.Entity<Supplier>().HasAlternateKey(x => new { x.CompanyId, x.Id });
    }
}

/// <summary>供應商聯絡人；以 (company_id, supplier_id) 複合外鍵參照同公司供應商（ADR-043）。</summary>
public sealed class SupplierContactDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        PartnerChildModel.Configure<SupplierContact, Supplier>(modelBuilder, "md_supplier_contact", "supplier_id");
        PartnerChildModel.ConfigureContact<SupplierContact>(modelBuilder);
    }
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
