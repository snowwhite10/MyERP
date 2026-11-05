using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

public sealed class GlJournalDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GlSourceKey>(e =>
        {
            e.ToTable("gl_source_key", t =>
            {
                t.HasCheckConstraint("ck_gl_source_key_identity", "[company_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND LEN(TRIM([source_module])) > 0 AND LEN(TRIM([source_type])) > 0 AND LEN(TRIM([fingerprint])) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceModule).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.SourceModule, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_gl_source_key_company_module_type_source");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });

        modelBuilder.Entity<GlJournal>(e =>
        {
            e.ToTable("gl_journal", t =>
            {
                t.HasCheckConstraint("ck_gl_journal_refs", "[company_id] > 0 AND [period_id] > 0 AND [source_key_id] > 0");
                t.HasCheckConstraint("ck_gl_journal_status", "[status] = 'Posted'");
                t.HasCheckConstraint("ck_gl_journal_currency", "[base_currency] LIKE '[A-Z][A-Z][A-Z]'");
                t.HasCheckConstraint("ck_gl_journal_balance", "[total_debit] > 0 AND [total_debit] = [total_credit]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.PeriodId, x.BusinessDate, x.Status })
                .HasDatabaseName("ix_gl_journal_company_period_business_date_status");
            e.HasIndex(x => x.SourceKeyId).IsUnique().HasDatabaseName("ux_gl_journal_source_key_id");
            e.HasOne<GlPeriod>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PeriodId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GlSourceKey>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceKeyId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });

        modelBuilder.Entity<GlJournalLine>(e =>
        {
            e.ToTable("gl_journal_line", t =>
            {
                t.HasCheckConstraint("ck_gl_journal_line_refs", "[company_id] > 0 AND [journal_id] > 0 AND [account_id] > 0 AND [sequence] > 0 AND LEN(TRIM([account_code])) > 0");
                t.HasCheckConstraint("ck_gl_journal_line_side", "([debit] > 0 AND [credit] = 0) OR ([debit] = 0 AND [credit] > 0)");
                t.HasCheckConstraint("ck_gl_journal_line_account_snapshot", "LEN(TRIM([account_name])) > 0 AND [account_version] > 0 AND [mapping_version] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.AccountCode).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.AccountName).HasMaxLength(200);
            e.HasIndex(x => new { x.JournalId, x.Sequence }).IsUnique()
                .HasDatabaseName("ux_gl_journal_line_journal_id_sequence");
            e.HasIndex(x => new { x.CompanyId, x.JournalId })
                .HasDatabaseName("ix_gl_journal_line_company_id_journal_id");
            e.HasOne<GlJournal>().WithMany().HasForeignKey(x => new { x.CompanyId, x.JournalId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
