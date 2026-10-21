using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class RfqAwardDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RfqAward>(e =>
        {
            e.ToTable("pur_rfq_award", t =>
            {
                t.HasCheckConstraint("ck_pur_rfq_award_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_pur_rfq_award_price", "[unit_price] >= 0 AND [exchange_rate] > 0 AND [tax_rate_percent] >= 0");
                t.HasCheckConstraint("ck_pur_rfq_award_amount", "[base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]");
                t.HasCheckConstraint("ck_pur_rfq_award_dates", "([tax_rate_to] IS NULL OR [tax_rate_to] >= [tax_rate_from]) AND ([exchange_rate_to] IS NULL OR [exchange_rate_to] >= [exchange_rate_from])");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnitPrice).HasPrecision(19, 6);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.TaxRatePercent).HasPrecision(9, 4);
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            e.Property(x => x.BaseNet).HasPrecision(19, 4);
            e.Property(x => x.BaseTax).HasPrecision(19, 4);
            e.Property(x => x.BaseGross).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.RfqLineId }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.RfqId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.QuotationId });
            e.HasIndex(x => new { x.CompanyId, x.QuotationLineId });
            e.HasIndex(x => new { x.CompanyId, x.SupplierId });
            e.HasIndex(x => new { x.CompanyId, x.TaxRateId });
            e.HasIndex(x => new { x.CompanyId, x.ExchangeRateId });
            e.HasOne<RfqLine>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RfqLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QuotationLine>().WithMany().HasForeignKey(x => new { x.CompanyId, x.QuotationLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
