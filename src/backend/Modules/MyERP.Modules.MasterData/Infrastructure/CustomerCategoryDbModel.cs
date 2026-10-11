using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

public sealed class CustomerCategoryDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerCategory>(e =>
        {
            e.ToTable("md_customer_category", t => t.HasCheckConstraint(
                "ck_md_customer_category_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique()
                .HasDatabaseName("ux_md_customer_category_company_id_code");
            e.HasIndex(x => new { x.CompanyId, x.Name })
                .HasDatabaseName("ix_md_customer_category_company_id_name");
        });
    }
}
