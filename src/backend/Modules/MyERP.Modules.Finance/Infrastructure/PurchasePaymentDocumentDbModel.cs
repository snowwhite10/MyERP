using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>WU-FI-006a1: payment draft persistence; no GL/AP balance changes.</summary>
public sealed class PurchasePaymentDocumentDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchasePaymentDocument>(e =>
        {
            e.ToTable("ap_purchase_payment_doc", t =>
            {
                t.HasCheckConstraint("ck_ap_purchase_payment_doc_status",
                    "[status] IN ('Draft','Pending','Rejected','Approved','Posted')");
                t.HasCheckConstraint("ck_ap_purchase_payment_doc_refs",
                    "[supplier_id] > 0 AND [bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0 AND LEN(TRIM([document_no])) > 0");
                t.HasCheckConstraint("ck_ap_purchase_payment_doc_amount", "[amount] > 0");
                t.HasCheckConstraint("ck_ap_purchase_payment_doc_currency",
                    "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                t.HasCheckConstraint("ck_ap_purchase_payment_doc_deleted",
                    "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] IN ('Draft','Rejected'))");
                t.HasCheckConstraint("ck_ap_purchase_payment_doc_custom_fields_json",
                    "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.BankReference).HasMaxLength(50);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique()
                .HasDatabaseName("ux_ap_purchase_payment_doc_company_id_document_no");
            e.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.BankReference }).IsUnique()
                .HasFilter("[is_deleted] = 0")
                .HasDatabaseName("ux_ap_purchase_payment_doc_company_id_bank_account_id_bank_reference");
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id })
                .HasDatabaseName("ix_ap_purchase_payment_doc_company_id_status_business_date_id");
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.BusinessDate })
                .HasDatabaseName("ix_ap_purchase_payment_doc_company_id_supplier_id_business_date");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });
    }
}
