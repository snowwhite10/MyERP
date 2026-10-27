using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

public sealed class SalesOrderDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesOrder>(e =>
        {
            e.ToTable("sal_order", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("ck_sal_order_status", "[status] IN ('Draft','Pending','Rejected','Approved','Cancelled')");
                t.HasCheckConstraint("ck_sal_order_refs", "[customer_id] > 0 AND ([source_quotation_id] IS NULL OR [source_quotation_id] > 0)");
                t.HasCheckConstraint("ck_sal_order_rate", "[exchange_rate] > 0");
                t.HasCheckConstraint("ck_sal_order_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
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
            e.HasIndex(x => new { x.CompanyId, x.SourceQuotationId }).IsUnique()
                .HasFilter("[source_quotation_id] IS NOT NULL");
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.OrderId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalesOrderLine>(e =>
        {
            e.ToTable("sal_order_line", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("ck_sal_order_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_sal_order_line_refs", "[item_id] > 0 AND [tax_rate_id] > 0 AND ([source_quotation_line_id] IS NULL OR [source_quotation_line_id] > 0)");
                t.HasCheckConstraint("ck_sal_order_line_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_sal_order_line_prices", "[unit_price] >= 0 AND [discount_rate] BETWEEN 0 AND 100 AND [tax_rate_percent] BETWEEN 0 AND 100");
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
            e.HasIndex(x => new { x.CompanyId, x.OrderId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.SourceQuotationLineId }).IsUnique()
                .HasFilter("[source_quotation_line_id] IS NOT NULL");
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
