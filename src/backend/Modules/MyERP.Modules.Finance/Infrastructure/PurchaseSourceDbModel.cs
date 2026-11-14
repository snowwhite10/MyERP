using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>FI 採購發票應付來源與已過帳應付（WU-FI-003a1；REQ_DS43.192–193；ADR-115）。</summary>
public sealed class PurchaseSourceDbModel : IModuleDbModel
{
    private const string BinaryCurrencies =
        "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]' AND [base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'";

    private static readonly string[] SourceMoney =
        ["NetAmount", "TaxAmount", "GrossAmount", "BaseNetAmount", "BaseTaxAmount", "BaseGrossAmount"];

    private static readonly string[] PayableMoney = ["OriginalAmount", "OpenAmount", "OriginalBaseAmount", "OpenBaseAmount"];

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseSource>(e =>
        {
            e.ToTable("ap_purchase_source", t =>
            {
                t.HasCheckConstraint("ck_ap_purchase_source_identity",
                    "[company_id] > 0 AND [supplier_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0");
                t.HasCheckConstraint("ck_ap_purchase_source_type", "[source_type] IN ('PurchaseInvoice')");
                // 預設定序不分大小寫，需二進位定序才能拒絕小寫幣別（BR-FI-148）。
                t.HasCheckConstraint("ck_ap_purchase_source_currency", BinaryCurrencies + " AND [exchange_rate] > 0");
                t.HasCheckConstraint("ck_ap_purchase_source_amounts",
                    "[net_amount] >= 0 AND [tax_amount] >= 0 AND [gross_amount] > 0 AND [net_amount] + [tax_amount] = [gross_amount] " +
                    "AND [base_net_amount] >= 0 AND [base_tax_amount] >= 0 AND [base_gross_amount] > 0 AND [base_net_amount] + [base_tax_amount] = [base_gross_amount]");
                t.HasTrigger("tr_ap_purchase_source_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            foreach (var name in SourceMoney)
                e.Property<decimal>(name).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_ap_purchase_source_company_id_source_type_source_id");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.BusinessDate })
                .HasDatabaseName("ix_ap_purchase_source_company_id_supplier_id_business_date");
            e.HasIndex(x => x.GlJournalId).IsUnique().HasDatabaseName("ux_ap_purchase_source_gl_journal_id");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<GlJournal>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.GlJournalId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PostedPayable>(e =>
        {
            e.ToTable("ap_posted_payable", t =>
            {
                t.HasCheckConstraint("ck_ap_posted_payable_refs",
                    "[company_id] > 0 AND [supplier_id] > 0 AND [purchase_source_id] > 0");
                t.HasCheckConstraint("ck_ap_posted_payable_currency", BinaryCurrencies);
                // 未付在 0 與原始之間，且原幣與本位幣同時結清或同時未清。
                t.HasCheckConstraint("ck_ap_posted_payable_amounts",
                    "[original_amount] > 0 AND [original_base_amount] > 0 " +
                    "AND [open_amount] >= 0 AND [open_amount] <= [original_amount] " +
                    "AND [open_base_amount] >= 0 AND [open_base_amount] <= [original_base_amount] " +
                    "AND (([open_amount] = 0 AND [open_base_amount] = 0) OR ([open_amount] > 0 AND [open_base_amount] > 0))");
                t.HasTrigger("tr_ap_posted_payable_origin");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            foreach (var name in PayableMoney)
                e.Property<decimal>(name).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.PurchaseSourceId }).IsUnique()
                .HasDatabaseName("ux_ap_posted_payable_company_id_purchase_source_id");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.PostingDate, x.Id })
                .IncludeProperties(x => new { x.Currency, x.OpenAmount, x.OpenBaseAmount })
                .HasDatabaseName("ix_ap_posted_payable_company_id_supplier_id_posting_date_id");
            e.HasOne<PurchaseSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PurchaseSourceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
