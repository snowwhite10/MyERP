using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>FI 銷售收款單據草稿與審批狀態（WU-FI-005b1；REQ_DS43.199；ADR-126）。</summary>
public sealed class CollectionReceiptDocumentDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionReceiptDocument>(e =>
        {
            e.ToTable("ar_collection_receipt_doc", t =>
            {
                t.HasCheckConstraint("ck_ar_collection_receipt_doc_status",
                    "[status] IN ('Draft','Pending','Rejected','Approved','Posted')");
                t.HasCheckConstraint("ck_ar_collection_receipt_doc_refs",
                    "[customer_id] > 0 AND [bank_account_id] > 0 AND LEN(TRIM([bank_reference])) > 0 AND LEN(TRIM([document_no])) > 0");
                t.HasCheckConstraint("ck_ar_collection_receipt_doc_amount", "[amount] > 0");
                // 預設定序不分大小寫，需二進位定序才能拒絕小寫幣別（BR-FI-148）。
                t.HasCheckConstraint("ck_ar_collection_receipt_doc_currency",
                    "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                t.HasCheckConstraint("ck_ar_collection_receipt_doc_deleted",
                    "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] IN ('Draft','Rejected'))");
                t.HasCheckConstraint("ck_ar_collection_receipt_doc_custom_fields_json",
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
                .HasDatabaseName("ux_ar_collection_receipt_doc_company_id_document_no");
            // 未刪除單據的銀行參考唯一（預設定序不分大小寫）；已刪除列釋出參考（BR-FI-033）。
            e.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.BankReference }).IsUnique()
                .HasFilter("[is_deleted] = 0")
                .HasDatabaseName("ux_ar_collection_receipt_doc_company_id_bank_account_id_bank_reference");
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id })
                .HasDatabaseName("ix_ar_collection_receipt_doc_company_id_status_business_date_id");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate })
                .HasDatabaseName("ix_ar_collection_receipt_doc_company_id_customer_id_business_date");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });
    }
}
