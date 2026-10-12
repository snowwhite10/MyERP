using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

public sealed class TaxDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<TaxCode>(modelBuilder, "md_tax_code");
        modelBuilder.Entity<TaxCode>(e =>
        {
            e.ToTable("md_tax_code", t => t.HasCheckConstraint("ck_md_tax_code_tax_type", "[tax_type] IN ('Output', 'Input', 'Both')"));
            e.Property(x => x.TaxType).HasMaxLength(30).IsUnicode(false);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });
        modelBuilder.Entity<TaxRate>(e =>
        {
            e.ToTable("md_tax_rate", t =>
            {
                t.HasCheckConstraint("ck_md_tax_rate_rate_percent", "[rate_percent] BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_md_tax_rate_period", "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.RatePercent).HasPrecision(7, 4);
            e.HasIndex(x => new { x.CompanyId, x.TaxCodeId, x.EffectiveFrom }).IsUnique().HasFilter("[is_deleted] = 0")
                .HasDatabaseName("ux_md_tax_rate_company_id_tax_code_id_effective_from");
            e.HasIndex(x => new { x.CompanyId, x.TaxCodeId }).HasDatabaseName("ix_md_tax_rate_company_id_tax_code_id");
            e.HasOne(x => x.TaxCode).WithMany().HasForeignKey(x => new { x.CompanyId, x.TaxCodeId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
