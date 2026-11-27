using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>FI-owned posted payment, write-off and allocation persistence.</summary>
public sealed class PurchasePaymentDbModel : IModuleDbModel
{
    private const string Identity =
        "[company_id] > 0 AND [supplier_id] > 0 AND [source_id] > 0 AND [source_version] > 0 AND [gl_journal_id] > 0 AND LEN(TRIM([fingerprint])) > 0";
    private const string Currency = "[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchasePaymentSource>(e =>
        {
            e.ToTable("ap_payment_source", t =>
            {
                t.HasCheckConstraint("ck_ap_payment_source_identity", Identity);
                t.HasCheckConstraint("ck_ap_payment_source_type", "[source_type] = 'PurchasePayment'");
                t.HasCheckConstraint("ck_ap_payment_source_bank", "[bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0");
                t.HasCheckConstraint("ck_ap_payment_source_currency", Currency);
                t.HasCheckConstraint("ck_ap_payment_source_amounts", "[amount] > 0 AND [unapplied_amount] >= 0 AND [unapplied_amount] <= [amount]");
                t.HasTrigger("tr_ap_payment_source_origin");
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
                .HasDatabaseName("ux_ap_payment_source_company_id_source_type_source_id");
            e.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.BankReference }).IsUnique()
                .HasDatabaseName("ux_ap_payment_source_company_id_bank_account_id_bank_reference");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.BusinessDate })
                .IncludeProperties(x => x.UnappliedAmount)
                .HasDatabaseName("ix_ap_payment_source_company_id_supplier_id_business_date");
            e.HasIndex(x => x.GlJournalId).IsUnique().HasDatabaseName("ux_ap_payment_source_gl_journal_id");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<GlJournal>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.GlJournalId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchasePaymentSettlement>(e =>
        {
            e.ToTable("ap_payment_settlement", t =>
            {
                t.HasCheckConstraint("ck_ap_payment_settlement_identity", Identity + " AND [payment_id] > 0");
                t.HasCheckConstraint("ck_ap_payment_settlement_type", "[source_type] = 'PaymentWriteOff'");
                t.HasCheckConstraint("ck_ap_payment_settlement_currency", Currency);
                t.HasCheckConstraint("ck_ap_payment_settlement_amount", "[total_amount] > 0");
                t.HasTrigger("tr_ap_payment_settlement_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.TotalAmount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique()
                .HasDatabaseName("ux_ap_payment_settlement_company_id_source_type_source_id");
            e.HasIndex(x => new { x.CompanyId, x.PaymentId })
                .HasDatabaseName("ix_ap_payment_settlement_company_id_payment_id");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId })
                .HasDatabaseName("ix_ap_payment_settlement_company_id_supplier_id");
            e.HasIndex(x => x.GlJournalId).IsUnique().HasDatabaseName("ux_ap_payment_settlement_gl_journal_id");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<PurchasePaymentSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PaymentId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GlJournal>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.GlJournalId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchasePaymentAllocation>(e =>
        {
            e.ToTable("ap_payment_allocation", t =>
            {
                t.HasCheckConstraint("ck_ap_payment_allocation_refs", "[company_id] > 0 AND [settlement_id] > 0 AND [payment_id] > 0 AND [payable_id] > 0");
                t.HasCheckConstraint("ck_ap_payment_allocation_amount", "[amount] > 0");
                t.HasTrigger("tr_ap_payment_allocation_immutable");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.SettlementId, x.PayableId }).IsUnique()
                .HasDatabaseName("ux_ap_payment_allocation_company_id_settlement_id_payable_id");
            e.HasIndex(x => new { x.CompanyId, x.PaymentId })
                .IncludeProperties(x => x.Amount)
                .HasDatabaseName("ix_ap_payment_allocation_company_id_payment_id");
            e.HasIndex(x => new { x.CompanyId, x.PayableId })
                .IncludeProperties(x => x.Amount)
                .HasDatabaseName("ix_ap_payment_allocation_company_id_payable_id");
            e.HasOne<PurchasePaymentSettlement>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.SettlementId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchasePaymentSource>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PaymentId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PostedPayable>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.PayableId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
