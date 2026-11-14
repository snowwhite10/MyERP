using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>銷售收款與核銷科目映射（WU-FI-005a2a；REQ_DS43.198；ADR-120）。</summary>
public sealed class SalesCollectionAccountMapDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesCollectionAccountMapping>(e =>
        {
            e.ToTable("gl_sales_collection_account_map", t =>
            {
                t.HasCheckConstraint("ck_gl_sales_collection_account_map_identity", "[company_id] > 0 AND [version] > 0");
                t.HasCheckConstraint("ck_gl_sales_collection_account_map_codes",
                    "LEN(TRIM([unapplied_cash_account_code])) > 0 AND LEN(TRIM([receivable_account_code])) > 0");
                // 暫收與應收控制相同時，核銷分錄借貸同一科目、暫收永不清除；忽略大小寫與前後空白比較。
                t.HasCheckConstraint("ck_gl_sales_collection_account_map_distinct",
                    "UPPER(TRIM([unapplied_cash_account_code])) <> UPPER(TRIM([receivable_account_code]))");
                t.HasTrigger("tr_gl_sales_collection_account_map_guard");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.UnappliedCashAccountCode).HasMaxLength(SalesCollectionAccountMapping.CodeMaxLength).IsUnicode(false);
            e.Property(x => x.ReceivableAccountCode).HasMaxLength(SalesCollectionAccountMapping.CodeMaxLength).IsUnicode(false);
            e.HasIndex(x => x.CompanyId).IsUnique().HasDatabaseName("ux_gl_sales_collection_account_map_company_id");
        });
    }
}
