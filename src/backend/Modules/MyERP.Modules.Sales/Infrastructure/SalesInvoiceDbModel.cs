using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

/// <summary>
/// WU-SD-007a1 (ADR-176, REQ_DS43.206/207): invoice header and lines. Same-company FKs to the SD order, order line,
/// delivery and delivery line; FI identities are scalar values. The migration adds triggers 51032/51033.
/// </summary>
public sealed class SalesInvoiceDbModel : IModuleDbModel
{
    private static readonly string[] Amounts =
        ["OriginalNet", "OriginalTax", "OriginalGross", "BaseNet", "BaseTax", "BaseGross"];

    private const string AmountCheck =
        "[original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax] " +
        "AND [base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesInvoice>(e =>
        {
            e.ToTable("sal_invoice", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("ck_sal_invoice_status", "[status] IN ('Issuing','Posted')");
                t.HasCheckConstraint("ck_sal_invoice_refs", "[customer_id] > 0 AND [order_id] > 0");
                t.HasCheckConstraint("ck_sal_invoice_request",
                    "LEN([request_key]) > 0 AND LEN([request_fingerprint]) > 0");
                t.HasCheckConstraint("ck_sal_invoice_amounts", AmountCheck);
                t.HasCheckConstraint("ck_sal_invoice_rate",
                    "[exchange_rate] > 0 AND ([currency_code] <> [base_currency_code] OR ([exchange_rate] = 1 " +
                    "AND [base_net] = [original_net] AND [base_tax] = [original_tax] AND [base_gross] = [original_gross]))");
                t.HasCheckConstraint("ck_sal_invoice_posted",
                    "([status] = 'Posted' AND [posted_at] IS NOT NULL AND [fi_sales_source_id] > 0 " +
                    "AND [fi_receivable_id] > 0 AND [fi_journal_id] > 0) OR ([status] = 'Issuing' AND [posted_at] IS NULL " +
                    "AND [fi_sales_source_id] IS NULL AND [fi_receivable_id] IS NULL AND [fi_journal_id] IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.RequestKey).HasMaxLength(SalesInvoice.RequestKeyMaxLength).IsUnicode(false);
            e.Property(x => x.RequestFingerprint).HasMaxLength(SalesInvoice.FingerprintMaxLength).IsUnicode(false);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            ConfigureAmounts(e);
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.FiSalesSourceId }).IsUnique()
                .HasFilter("[fi_sales_source_id] IS NOT NULL");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.OrderId, x.Id });
            e.HasOne<SalesOrder>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.OrderId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_invoice_sal_order");
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_invoice_line_sal_invoice");
        });

        modelBuilder.Entity<SalesInvoiceLine>(e =>
        {
            e.ToTable("sal_invoice_line", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("ck_sal_invoice_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_sal_invoice_line_refs",
                    "[delivery_id] > 0 AND [delivery_line_id] > 0 AND [order_line_id] > 0 AND [item_id] > 0 " +
                    "AND [tax_rate_id] > 0");
                t.HasCheckConstraint("ck_sal_invoice_line_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_sal_invoice_line_tax_rate", "[tax_rate_percent] BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_sal_invoice_line_amounts", AmountCheck);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.TaxRatePercent).HasPrecision(9, 4);
            ConfigureAmounts(e);
            e.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.DeliveryLineId, x.InvoiceId });
            e.HasIndex(x => new { x.CompanyId, x.OrderLineId, x.InvoiceId });
            e.HasIndex(x => new { x.CompanyId, x.DeliveryId });
            e.HasOne<SalesDelivery>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.DeliveryId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_invoice_line_sal_delivery");
            e.HasOne<SalesDeliveryLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.DeliveryLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_invoice_line_sal_delivery_line");
            e.HasOne<SalesOrderLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.OrderLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_invoice_line_sal_order_line");
        });
    }

    private static void ConfigureAmounts<TEntity>(EntityTypeBuilder<TEntity> e) where TEntity : class
    {
        foreach (var name in Amounts)
            e.Property<decimal>(name).HasPrecision(19, 4);
    }
}
