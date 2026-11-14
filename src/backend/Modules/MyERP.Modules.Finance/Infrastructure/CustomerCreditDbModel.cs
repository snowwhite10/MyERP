using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>FI credit foundation: permanent key, dated limits and posted AR exposure (WU-FI-001a).</summary>
public sealed class CustomerCreditDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreditKey>(e =>
        {
            e.ToTable("ar_credit_key", t => t.HasCheckConstraint("ck_ar_credit_key_refs", "[company_id] > 0 AND [customer_id] > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasAlternateKey(x => new { x.CompanyId, x.CustomerId }).HasName("ux_ar_credit_key_company_id_customer_id");
        });

        modelBuilder.Entity<CreditLimit>(e =>
        {
            e.ToTable("ar_credit_limit", t =>
            {
                t.HasCheckConstraint("ck_ar_credit_limit_dates", "[effective_to] >= [effective_from]");
                t.HasCheckConstraint("ck_ar_credit_limit_amount", "[amount] >= 0");
                t.HasCheckConstraint("ck_ar_credit_limit_status", "[status] IN ('Active','Deactivated')");
                t.HasCheckConstraint("ck_ar_credit_limit_currency", "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.EffectiveFrom, x.EffectiveTo })
                .HasDatabaseName("ix_ar_credit_limit_company_id_customer_id_effective_from_effective_to");
            e.HasOne<CreditKey>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CustomerId })
                .HasPrincipalKey(x => new { x.CompanyId, x.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PostedReceivable>(e =>
        {
            e.ToTable("ar_posted_receivable", t =>
            {
                t.HasCheckConstraint("ck_ar_posted_receivable_open_amount",
                    "[original_base_amount] >= 0 AND [open_base_amount] >= 0 AND [open_base_amount] <= [original_base_amount]");
                t.HasCheckConstraint("ck_ar_posted_receivable_source", "[source_id] > 0 AND LEN([source_type]) > 0");
                t.HasCheckConstraint("ck_ar_posted_receivable_currency", "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.OriginalBaseAmount).HasPrecision(19, 4);
            e.Property(x => x.OpenBaseAmount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_ar_posted_receivable_company_id_source_type_source_id");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId })
                .IncludeProperties(x => new { x.BaseCurrency, x.OpenBaseAmount })
                .HasDatabaseName("ix_ar_posted_receivable_company_id_customer_id");
            // ar_credit_allocation 以 (company_id, receivable_id) 複合外鍵確保分配與應收同公司（WU-FI-002a1）。
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<CreditKey>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CustomerId })
                .HasPrincipalKey(x => new { x.CompanyId, x.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
