using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>
/// 資金帳戶與收支類別（ADR-041）。幣別與對應科目以 (company_id, x_id) 複合外鍵參照同公司資料；
/// 須與 <see cref="CurrencyDbModel"/>、<see cref="AccountDbModel"/> 一起註冊。
/// </summary>
public sealed class FundSetupDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<FundAccount>(modelBuilder, "md_fund_account");
        modelBuilder.Entity<FundAccount>(e =>
        {
            e.ToTable("md_fund_account", t =>
            {
                t.HasCheckConstraint("ck_md_fund_account_fund_type", "[fund_type] IN ('Cash', 'Bank')");
                t.HasCheckConstraint("ck_md_fund_account_bank",
                    "[fund_type] = 'Bank' OR ([bank_name] IS NULL AND [bank_account_no] IS NULL)");
            });
            e.Property(x => x.FundType).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.BankName).HasMaxLength(200);
            e.Property(x => x.BankAccountNo).HasMaxLength(FundAccount.BankAccountNoMaxLength).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.CurrencyId }).HasDatabaseName("ix_md_fund_account_company_id_currency_id");
            e.HasOne<Currency>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CurrencyId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.CompanyId, x.LedgerAccountId }).HasDatabaseName("ix_md_fund_account_company_id_ledger_account_id");
            e.HasOne<Account>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.LedgerAccountId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });

        MasterCategoryModel.Configure<IncomeExpenseCategory>(modelBuilder, "md_income_expense_category");
        modelBuilder.Entity<IncomeExpenseCategory>(e =>
        {
            e.ToTable("md_income_expense_category", t =>
                t.HasCheckConstraint("ck_md_income_expense_category_direction", "[direction] IN ('Income', 'Expense')"));
            e.Property(x => x.Direction).HasMaxLength(10).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.LedgerAccountId })
                .HasDatabaseName("ix_md_income_expense_category_company_id_ledger_account_id");
            e.HasOne<Account>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.LedgerAccountId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
