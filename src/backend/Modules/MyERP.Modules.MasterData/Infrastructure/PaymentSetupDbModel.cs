using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>結算方式與付款條件（ADR-040）；付款條件以 CHECK 保證每種到期規則只存自己的欄位。</summary>
public sealed class PaymentSetupDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<SettlementMethod>(modelBuilder, "md_settlement_method");
        MasterCategoryModel.Configure<PaymentTerm>(modelBuilder, "md_payment_term");
        modelBuilder.Entity<PaymentTerm>(e =>
        {
            e.ToTable("md_payment_term", t =>
            {
                t.HasCheckConstraint("ck_md_payment_term_due_type",
                    $"[due_type] IN ('{PaymentTerm.NetDays}', '{PaymentTerm.ClosingDays}', '{PaymentTerm.ClosingMonthDay}')");
                t.HasCheckConstraint("ck_md_payment_term_rule",
                    $"([due_type] = '{PaymentTerm.NetDays}' AND [days] IS NOT NULL AND [closing_day] IS NULL AND [months] IS NULL AND [payment_day] IS NULL)" +
                    $" OR ([due_type] = '{PaymentTerm.ClosingDays}' AND [days] IS NOT NULL AND [closing_day] IS NOT NULL AND [months] IS NULL AND [payment_day] IS NULL)" +
                    $" OR ([due_type] = '{PaymentTerm.ClosingMonthDay}' AND [days] IS NULL AND [closing_day] IS NOT NULL AND [months] IS NOT NULL AND [payment_day] IS NOT NULL)");
                t.HasCheckConstraint("ck_md_payment_term_ranges",
                    $"([days] IS NULL OR [days] BETWEEN 0 AND {PaymentTerm.MaxDays})" +
                    $" AND ([closing_day] IS NULL OR [closing_day] BETWEEN 1 AND {PaymentTerm.MonthEnd})" +
                    $" AND ([months] IS NULL OR [months] BETWEEN 0 AND {PaymentTerm.MaxMonths})" +
                    $" AND ([payment_day] IS NULL OR [payment_day] BETWEEN 1 AND {PaymentTerm.MonthEnd})");
            });
            e.Property(x => x.DueType).HasMaxLength(20).IsUnicode(false);
        });
    }
}
