using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>FI 收款核銷單據草稿、分配行與審批狀態（WU-FI-005b2；REQ_DS43.200–201；ADR-127）。</summary>
public sealed class CollectionSettlementDocumentDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionSettlementDocument>(e =>
        {
            e.ToTable("ar_collection_settlement_doc", t =>
            {
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_status",
                    "[status] IN ('Draft','Pending','Rejected','Approved','Posted')");
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_refs",
                    "[customer_id] > 0 AND [receipt_id] > 0 AND LEN(TRIM([document_no])) > 0");
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_total", "[total_amount] > 0");
                // 預設定序不分大小寫，需二進位定序才能拒絕小寫幣別（BR-FI-148）。
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_currency",
                    "[currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_deleted",
                    "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] IN ('Draft','Rejected'))");
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_custom_fields_json",
                    "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.TotalAmount).HasPrecision(19, 4);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique()
                .HasDatabaseName("ux_ar_collection_settlement_doc_company_id_document_no");
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id })
                .HasDatabaseName("ix_ar_collection_settlement_doc_company_id_status_business_date_id");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate })
                .HasDatabaseName("ix_ar_collection_settlement_doc_company_id_customer_id_business_date");
            // 某收款的核銷單據（收款明細與 005b3 待審合計）。
            e.HasIndex(x => new { x.CompanyId, x.ReceiptId })
                .HasDatabaseName("ix_ar_collection_settlement_doc_company_id_receipt_id");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        });

        modelBuilder.Entity<CollectionSettlementDocumentLine>(e =>
        {
            e.ToTable("ar_collection_settlement_doc_line", t =>
            {
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_line_amount", "[amount] > 0");
                t.HasCheckConstraint("ck_ar_collection_settlement_doc_line_line_no", "[line_no] BETWEEN 1 AND 1000");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SettlementDocumentId).HasColumnName("settlement_doc_id");
            e.Property(x => x.Amount).HasPrecision(19, 4);
            // 每張單據每個應收一行（BR-FI-035）；同時作為單據外鍵索引。
            e.HasIndex(x => new { x.CompanyId, x.SettlementDocumentId, x.ReceivableId }).IsUnique()
                .HasDatabaseName("ux_ar_collection_settlement_doc_line_company_id_settlement_doc_id_receivable_id");
            e.HasIndex(x => new { x.CompanyId, x.SettlementDocumentId, x.LineNo }).IsUnique()
                .HasDatabaseName("ux_ar_collection_settlement_doc_line_company_id_settlement_doc_id_line_no");
            e.HasIndex(x => new { x.CompanyId, x.ReceivableId })
                .HasDatabaseName("ix_ar_collection_settlement_doc_line_company_id_receivable_id");
            e.HasOne<CollectionSettlementDocument>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.SettlementDocumentId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PostedReceivable>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ReceivableId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
