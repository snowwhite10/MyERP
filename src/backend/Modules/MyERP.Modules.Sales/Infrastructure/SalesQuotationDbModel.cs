using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

public sealed class SalesQuotationDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesQuotation>(e =>
        {
            e.ToTable("sal_quotation", t =>
            {
                t.HasCheckConstraint("ck_sal_quotation_status", "[status] IN ('Draft','Pending','Rejected','Approved','Voided')");
                t.HasCheckConstraint("ck_sal_quotation_refs", "[customer_id] > 0 AND [owner_user_id] > 0");
                t.HasCheckConstraint("ck_sal_quotation_dates", "[expiry_date] >= [business_date]");
                t.HasCheckConstraint("ck_sal_quotation_rate", "[exchange_rate] > 0");
                t.HasCheckConstraint("ck_sal_quotation_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.CustomerCode).HasMaxLength(50);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(500);
            ConfigureAmounts(e);
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate });
            e.HasIndex(x => new { x.CompanyId, x.ExpiryDate, x.Status });
            e.HasIndex(x => new { x.CompanyId, x.BranchId, x.DepartmentId, x.OwnerUserId });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.QuotationId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalesQuotationLine>(e =>
        {
            e.ToTable("sal_quotation_line", t =>
            {
                t.HasCheckConstraint("ck_sal_quotation_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_sal_quotation_line_refs", "[item_id] > 0 AND [tax_rate_id] > 0");
                t.HasCheckConstraint("ck_sal_quotation_line_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_sal_quotation_line_prices", "[unit_price] >= 0 AND [discount_rate] BETWEEN 0 AND 100 AND [tax_rate_percent] BETWEEN 0 AND 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ItemCode).HasMaxLength(50);
            e.Property(x => x.ItemName).HasMaxLength(200);
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnitPrice).HasPrecision(19, 6);
            e.Property(x => x.DiscountRate).HasPrecision(9, 4);
            e.Property(x => x.TaxRatePercent).HasPrecision(9, 4);
            ConfigureAmounts(e);
            e.HasIndex(x => new { x.CompanyId, x.QuotationId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.ItemId });
            e.HasIndex(x => new { x.CompanyId, x.TaxRateId });
        });
    }

    private static void ConfigureAmounts<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e)
        where TEntity : class
    {
        foreach (var name in new[] { "OriginalNet", "OriginalTax", "OriginalGross", "BaseNet", "BaseTax", "BaseGross" })
            e.Property<decimal>(name).HasPrecision(19, 4);
    }
}
