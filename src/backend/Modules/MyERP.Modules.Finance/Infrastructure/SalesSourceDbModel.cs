using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>FI 銷售應收來源與信用分配（WU-FI-002a1；REQ_DS43.189–190）。</summary>
public sealed class SalesSourceDbModel : IModuleDbModel
{
    private const string Reconciled =
        "[net_amount] >= 0 AND [tax_amount] >= 0 AND [gross_amount] > 0 AND [net_amount] + [tax_amount] = [gross_amount] " +
        "AND [base_net_amount] >= 0 AND [base_tax_amount] >= 0 AND [base_gross_amount] > 0 AND [base_net_amount] + [base_tax_amount] = [base_gross_amount]";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesSource>(e =>
        {
            e.ToTable("ar_sales_source", t =>
            {
                t.HasCheckConstraint("ck_ar_sales_source_identity",
                    "[company_id] > 0 AND [customer_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                t.HasCheckConstraint("ck_ar_sales_source_type", "[source_type] IN ('Invoice','Return')");
                // 預設定序不分大小寫，需二進位定序才能拒絕小寫幣別。
                t.HasCheckConstraint("ck_ar_sales_source_currency",
                    "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [exchange_rate] > 0");
                t.HasCheckConstraint("ck_ar_sales_source_amounts", Reconciled);
                t.HasTrigger("tr_ar_sales_source_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            Money(e);
            e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_ar_sales_source_company_id_source_type_source_id");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate })
                .HasDatabaseName("ix_ar_sales_source_company_id_customer_id_business_date");
            e.HasIndex(x => x.GlJournalId).IsUnique().HasDatabaseName("ux_ar_sales_source_gl_journal_id");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<CreditKey>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CustomerId })
                .HasPrincipalKey(x => new { x.CompanyId, x.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GlJournal>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.GlJournalId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CreditAllocation>(e =>
        {
            e.ToTable("ar_credit_allocation", t =>
            {
                t.HasCheckConstraint("ck_ar_credit_allocation_refs",
                    "[company_id] > 0 AND [credit_source_id] > 0 AND [receivable_id] > 0 AND [invoice_line_id] > 0");
                t.HasCheckConstraint("ck_ar_credit_allocation_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_ar_credit_allocation_amounts", Reconciled);
                t.HasTrigger("tr_ar_credit_allocation_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            Money(e);
            e.HasIndex(x => new { x.CreditSourceId, x.InvoiceLineId }).IsUnique()
                .HasDatabaseName("ux_ar_credit_allocation_credit_source_id_invoice_line_id");
            e.HasIndex(x => new { x.CompanyId, x.ReceivableId })
                .IncludeProperties(x => new { x.BaseGrossAmount })
                .HasDatabaseName("ix_ar_credit_allocation_company_id_receivable_id");
            e.HasIndex(x => new { x.CompanyId, x.InvoiceLineId })
                .IncludeProperties(x => new { x.Quantity, x.NetAmount, x.TaxAmount, x.BaseNetAmount, x.BaseTaxAmount })
                .HasDatabaseName("ix_ar_credit_allocation_company_id_invoice_line_id");
            e.HasOne<SalesSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CreditSourceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PostedReceivable>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ReceivableId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void Money<T>(EntityTypeBuilder<T> e) where T : class
    {
        foreach (var name in new[] { "NetAmount", "TaxAmount", "GrossAmount", "BaseNetAmount", "BaseTaxAmount", "BaseGrossAmount" })
            e.Property<decimal>(name).HasPrecision(19, 4);
    }
}
