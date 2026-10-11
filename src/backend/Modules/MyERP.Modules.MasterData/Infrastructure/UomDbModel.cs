using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>計量單位與單位換算；換算以 (company_id, uom_id) 複合外鍵參照，資料庫層阻止跨公司單位。</summary>
public sealed class UomDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<UnitOfMeasure>(modelBuilder, "md_uom");
        modelBuilder.Entity<UnitOfMeasure>(e =>
        {
            e.ToTable("md_uom", t => t.HasCheckConstraint(
                "ck_md_uom_decimal_places", $"[decimal_places] BETWEEN 0 AND {UnitOfMeasure.MaxDecimalPlaces}"));
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });
        modelBuilder.Entity<UomConversion>(e =>
        {
            e.ToTable("md_uom_conversion", t =>
            {
                t.HasCheckConstraint("ck_md_uom_conversion_factor", "[factor] > 0");
                t.HasCheckConstraint("ck_md_uom_conversion_units", "[from_uom_id] <> [to_uom_id]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Factor).HasPrecision(UomConversion.FactorPrecision, UomConversion.FactorScale);
            e.HasIndex(x => new { x.CompanyId, x.FromUomId, x.ToUomId }).IsUnique().HasFilter("[is_deleted] = 0")
                .HasDatabaseName("ux_md_uom_conversion_company_id_from_uom_id_to_uom_id");
            e.HasIndex(x => new { x.CompanyId, x.ToUomId }).HasDatabaseName("ix_md_uom_conversion_company_id_to_uom_id");
            e.HasOne(x => x.FromUom).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.FromUomId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ToUom).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ToUomId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
