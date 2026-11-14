using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>FI 銷售收款、收款核銷與核銷分配的已過帳來源（WU-FI-005a1；REQ_DS43.195–197；ADR-119）。</summary>
public sealed class CollectionDbModel : IModuleDbModel
{
    private const string Identity =
        "[company_id] > 0 AND [customer_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0";

    // 預設定序不分大小寫，需二進位定序才能拒絕小寫幣別（BR-FI-148）。
    private const string BinaryCurrency = "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionReceipt>(e =>
        {
            e.ToTable("ar_collection_receipt", t =>
            {
                t.HasCheckConstraint("ck_ar_collection_receipt_identity", Identity);
                t.HasCheckConstraint("ck_ar_collection_receipt_type", "[source_type] = 'SalesCollection'");
                t.HasCheckConstraint("ck_ar_collection_receipt_bank", "[bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0");
                t.HasCheckConstraint("ck_ar_collection_receipt_currency", BinaryCurrency);
                t.HasCheckConstraint("ck_ar_collection_receipt_amounts",
                    "[amount] > 0 AND [unapplied_amount] >= 0 AND [unapplied_amount] <= [amount]");
                t.HasTrigger("tr_ar_collection_receipt_origin");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.BankReference).HasMaxLength(50);
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.UnappliedAmount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_ar_collection_receipt_company_id_source_type_source_id");
            e.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.BankReference }).IsUnique()
                .HasDatabaseName("ux_ar_collection_receipt_company_id_bank_account_id_bank_reference");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate })
                .IncludeProperties(x => new { x.UnappliedAmount })
                .HasDatabaseName("ix_ar_collection_receipt_company_id_customer_id_business_date");
            e.HasIndex(x => x.GlJournalId).IsUnique().HasDatabaseName("ux_ar_collection_receipt_gl_journal_id");
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

        modelBuilder.Entity<CollectionSettlement>(e =>
        {
            e.ToTable("ar_collection_settlement", t =>
            {
                t.HasCheckConstraint("ck_ar_collection_settlement_identity", Identity + " AND [receipt_id] > 0");
                t.HasCheckConstraint("ck_ar_collection_settlement_type", "[source_type] = 'CollectionWriteOff'");
                t.HasCheckConstraint("ck_ar_collection_settlement_currency", BinaryCurrency);
                t.HasCheckConstraint("ck_ar_collection_settlement_amount", "[total_amount] > 0");
                t.HasTrigger("tr_ar_collection_settlement_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.TotalAmount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_ar_collection_settlement_company_id_source_type_source_id");
            e.HasIndex(x => new { x.CompanyId, x.ReceiptId })
                .HasDatabaseName("ix_ar_collection_settlement_company_id_receipt_id");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId })
                .HasDatabaseName("ix_ar_collection_settlement_company_id_customer_id");
            e.HasIndex(x => x.GlJournalId).IsUnique().HasDatabaseName("ux_ar_collection_settlement_gl_journal_id");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<CreditKey>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CustomerId })
                .HasPrincipalKey(x => new { x.CompanyId, x.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GlJournal>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.GlJournalId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CollectionReceipt>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ReceiptId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CollectionAllocation>(e =>
        {
            e.ToTable("ar_collection_allocation", t =>
            {
                t.HasCheckConstraint("ck_ar_collection_allocation_refs",
                    "[company_id] > 0 AND [settlement_id] > 0 AND [receipt_id] > 0 AND [receivable_id] > 0");
                t.HasCheckConstraint("ck_ar_collection_allocation_amount", "[amount] > 0");
                t.HasTrigger("tr_ar_collection_allocation_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Amount).HasPrecision(19, 4);
            // 唯一性按核銷＋應收，不按收款＋應收：不同核銷可分次以同一收款核銷同一發票（WU-FI-005d 設計審查）。
            e.HasIndex(x => new { x.CompanyId, x.SettlementId, x.ReceivableId }).IsUnique()
                .HasDatabaseName("ux_ar_collection_allocation_company_id_settlement_id_receivable_id");
            e.HasIndex(x => new { x.CompanyId, x.ReceiptId })
                .IncludeProperties(x => new { x.Amount })
                .HasDatabaseName("ix_ar_collection_allocation_company_id_receipt_id");
            e.HasIndex(x => new { x.CompanyId, x.ReceivableId })
                .IncludeProperties(x => new { x.Amount })
                .HasDatabaseName("ix_ar_collection_allocation_company_id_receivable_id");
            e.HasOne<CollectionSettlement>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.SettlementId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CollectionReceipt>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ReceiptId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PostedReceivable>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ReceivableId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
