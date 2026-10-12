using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>會計科目樹；上層以 (company_id, parent_id) 複合外鍵參照同公司科目，類別與方向以名稱字串保存（ADR-039）。</summary>
public sealed class AccountDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<Account>(modelBuilder, "md_account");
        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("md_account", t =>
            {
                t.HasCheckConstraint("ck_md_account_parent", "[parent_id] <> [id]");
                t.HasCheckConstraint("ck_md_account_code_digits", "[code] NOT LIKE '%[^0-9]%'");
                t.HasCheckConstraint("ck_md_account_account_type",
                    "[account_type] IN ('Asset', 'Liability', 'Equity', 'Cost', 'Revenue', 'Expense')");
                t.HasCheckConstraint("ck_md_account_balance_direction", "[balance_direction] IN ('Debit', 'Credit')");
            });
            e.Property(x => x.AccountType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.BalanceDirection).HasMaxLength(10).IsUnicode(false);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.ParentId }).HasDatabaseName("ix_md_account_company_id_parent_id");
            e.HasOne<Account>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ParentId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
