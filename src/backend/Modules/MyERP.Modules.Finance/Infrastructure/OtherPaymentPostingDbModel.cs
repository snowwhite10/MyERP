using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-008a2: Posted other payment source (REQ_DS43.220) and category→expense account map (REQ_DS43.221).
/// Triggers (SQL 51039): the source is immutable and must match its saved Draft; the map cannot be deleted,
/// change company/category or keep its version; a Draft cannot reuse a reference held by a Posted source.
/// </summary>
public sealed class OtherPaymentPostingDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OtherPaymentSource>(e =>
        {
            e.ToTable("ap_other_payment_source", t =>
            {
                t.HasCheckConstraint("ck_ap_other_payment_source_identity",
                    "[company_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                t.HasCheckConstraint("ck_ap_other_payment_source_type", "[source_type] = 'OtherPayment'");
                t.HasCheckConstraint("ck_ap_other_payment_source_refs",
                    "[expense_category_id] > 0 AND [expense_account_id] > 0 AND [mapping_version] > 0 AND [bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0");
                t.HasCheckConstraint("ck_ap_other_payment_source_currency",
                    "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                t.HasCheckConstraint("ck_ap_other_payment_source_amount", "[amount] > 0");
                t.HasTrigger("tr_ap_other_payment_source_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.BankReference).HasMaxLength(50);
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_ap_other_payment_source_company_id_source_type_source_id");
            // Unfiltered: Posted withdrawal references stay reserved permanently (BR-FI-062).
            e.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.BankReference }).IsUnique()
                .HasDatabaseName("ux_ap_other_payment_source_company_id_bank_account_id_bank_reference");
            e.HasIndex(x => new { x.CompanyId, x.ExpenseCategoryId, x.BusinessDate })
                .IncludeProperties(x => x.Amount)
                .HasDatabaseName("ix_ap_other_payment_source_company_id_expense_category_id_business_date");
            e.HasIndex(x => x.GlJournalId).IsUnique().HasDatabaseName("ux_ap_other_payment_source_gl_journal_id");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<OtherPaymentDocument>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.SourceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GlJournal>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.GlJournalId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OtherPaymentAccountMapping>(e =>
        {
            e.ToTable("gl_other_payment_account_map", t =>
            {
                t.HasCheckConstraint("ck_gl_other_payment_account_map_identity",
                    "[company_id] > 0 AND [expense_category_id] > 0 AND [expense_account_id] > 0 AND [version] > 0");
                t.HasTrigger("tr_gl_other_payment_account_map_guard");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.CompanyId, x.ExpenseCategoryId }).IsUnique()
                .HasDatabaseName("ux_gl_other_payment_account_map_company_id_expense_category_id");
        });
    }
}
