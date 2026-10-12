using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>
/// 工序類別與工序（ADR-042）。類別與計件單位以 (company_id, x_id) 複合外鍵參照同公司資料；
/// 須與 <see cref="UomDbModel"/> 一起註冊。
/// </summary>
public sealed class ProcessDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<ProcessCategory>(modelBuilder, "md_process_category");
        modelBuilder.Entity<ProcessCategory>(e =>
        {
            e.ToTable("md_process_category", t => t.HasCheckConstraint(
                "ck_md_process_category_sort_order", $"[sort_order] BETWEEN 0 AND {ProcessCategory.MaxSortOrder}"));
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });

        MasterCategoryModel.Configure<Process>(modelBuilder, "md_process");
        modelBuilder.Entity<Process>(e =>
        {
            e.ToTable("md_process", t =>
            {
                t.HasCheckConstraint("ck_md_process_sort_order", $"[sort_order] BETWEEN 0 AND {ProcessCategory.MaxSortOrder}");
                t.HasCheckConstraint("ck_md_process_standard_rate", "[standard_rate] >= 0");
            });
            e.Property(x => x.StandardRate).HasPrecision(19, 6);
            e.HasIndex(x => new { x.CompanyId, x.CategoryId }).HasDatabaseName("ix_md_process_company_id_category_id");
            e.HasOne<ProcessCategory>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CategoryId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.CompanyId, x.UomId }).HasDatabaseName("ix_md_process_company_id_uom_id");
            e.HasOne<UnitOfMeasure>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.UomId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
