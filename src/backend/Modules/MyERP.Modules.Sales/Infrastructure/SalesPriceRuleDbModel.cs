using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

/// <summary>sal_price_rule (REQ_DS43.204, WU-SD-005a8, ADR-157).</summary>
public sealed class SalesPriceRuleDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesPriceRule>(e =>
        {
            e.ToTable("sal_price_rule", t =>
            {
                t.HasCheckConstraint("ck_sal_price_rule_refs",
                    "[item_id] > 0 AND ([customer_id] IS NULL OR [customer_id] > 0)");
                t.HasCheckConstraint("ck_sal_price_rule_dates", "[valid_to] >= [valid_from]");
                t.HasCheckConstraint("ck_sal_price_rule_price", "[unit_price] >= 0");
                t.HasCheckConstraint("ck_sal_price_rule_ceiling",
                    "[discount_ceiling] >= 0 AND [discount_ceiling] <= 100");
                t.HasCheckConstraint("ck_sal_price_rule_currency",
                    "LEN([currency_code]) = 3 AND [currency_code] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.UnitPrice).HasPrecision(19, 6);
            e.Property(x => x.DiscountCeiling).HasPrecision(7, 4);
            // Dated selection and the same-key overlap check (company, item, currency, customer, start).
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.CurrencyCode, x.CustomerId, x.ValidFrom, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.CustomerId });
            e.HasIndex(x => new { x.CompanyId, x.ValidFrom, x.Id });
            // Principal of same-company document line references (WU-SD-005a11a, ADR-160).
            e.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("ak_sal_price_rule_company_id_id");
        });
    }
}
