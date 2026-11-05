using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

public sealed class GlPeriodDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GlPeriod>(e =>
        {
            e.ToTable("gl_period", t =>
            {
                t.HasCheckConstraint("ck_gl_period_dates", "[end_date] >= [start_date]");
                t.HasCheckConstraint("ck_gl_period_status", "[status] IN ('Open','Closed')");
                t.HasCheckConstraint("ck_gl_period_version", "[version] > 0");
                t.HasCheckConstraint("ck_gl_period_company", "[company_id] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.StartDate, x.EndDate })
                .HasDatabaseName("ix_gl_period_company_id_start_date_end_date");
        });
    }
}
