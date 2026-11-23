using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

/// <summary>
/// WU-SD-008a1 (ADR-184, REQ_DS43.208/209/210): return header, lines and billed invoice allocations. Same-company FKs
/// to the SD order, order line, delivery, delivery line, invoice and invoice line; INV/FI/SYS identities are scalar
/// values. The migration adds triggers 51035 (header), 51036 (line) and 51037 (allocation).
/// </summary>
public sealed class SalesReturnDbModel : IModuleDbModel
{
    private static readonly string[] Amounts =
        ["OriginalNet", "OriginalTax", "OriginalGross", "BaseNet", "BaseTax", "BaseGross"];

    private const string AmountCheck =
        "[original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax] " +
        "AND [base_net] >= 0 AND [base_tax] >= 0 AND [base_gross] = [base_net] + [base_tax]";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesReturn>(e =>
        {
            e.ToTable("sal_return", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("ck_sal_return_status", "[status] IN ('Draft','Pending','Rejected','Posted')");
                t.HasCheckConstraint("ck_sal_return_refs",
                    "[customer_id] > 0 AND [order_id] > 0 AND ([approval_instance_id] IS NULL OR [approval_instance_id] > 0)");
                t.HasCheckConstraint("ck_sal_return_request",
                    "LEN([request_key]) > 0 AND LEN([request_fingerprint]) > 0");
                t.HasCheckConstraint("ck_sal_return_amounts", AmountCheck);
                t.HasCheckConstraint("ck_sal_return_rate",
                    "[exchange_rate] > 0 AND ([currency_code] <> [base_currency_code] OR ([exchange_rate] = 1 " +
                    "AND [base_net] = [original_net] AND [base_tax] = [original_tax] AND [base_gross] = [original_gross]))");
                t.HasCheckConstraint("ck_sal_return_posted",
                    "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL " +
                    "AND [fi_credit_note_id] IS NULL)");
                t.HasCheckConstraint("ck_sal_return_fi_credit", "[fi_credit_note_id] IS NULL OR [fi_credit_note_id] > 0");
                t.HasCheckConstraint("ck_sal_return_deleted",
                    "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL " +
                    "AND [status] = 'Draft')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.RequestKey).HasMaxLength(SalesReturn.RequestKeyMaxLength).IsUnicode(false);
            e.Property(x => x.RequestFingerprint).HasMaxLength(SalesReturn.FingerprintMaxLength).IsUnicode(false);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.BaseCurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(SalesReturn.RemarkMaxLength);
            ConfigureAmounts(e);
            // Unique keys are unfiltered so a soft-deleted Draft keeps its number and request key forever.
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.FiCreditNoteId }).IsUnique()
                .HasFilter("[fi_credit_note_id] IS NOT NULL");
            e.HasIndex(x => new { x.CompanyId, x.CustomerId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.OrderId, x.Status });
            e.HasOne<SalesOrder>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.OrderId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_return_sal_order");
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.ReturnId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_sal_return_line_sal_return");
        });

        modelBuilder.Entity<SalesReturnLine>(e =>
        {
            e.ToTable("sal_return_line", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("ck_sal_return_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_sal_return_line_refs",
                    "[delivery_id] > 0 AND [delivery_line_id] > 0 AND [order_line_id] > 0 AND [item_id] > 0 " +
                    "AND [original_outbound_movement_id] > 0 AND [warehouse_id] > 0 " +
                    "AND ([location_id] IS NULL OR [location_id] > 0) AND ([inv_movement_id] IS NULL OR [inv_movement_id] > 0)");
                t.HasCheckConstraint("ck_sal_return_line_quantity",
                    "[quantity] > 0 AND [unbilled_quantity] >= 0 AND [unbilled_quantity] <= [quantity]");
                t.HasCheckConstraint("ck_sal_return_line_amounts", AmountCheck);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnbilledQuantity).HasPrecision(19, 6);
            ConfigureAmounts(e);
            e.HasIndex(x => new { x.CompanyId, x.ReturnId, x.LineNo }).IsUnique();
            // One INV inbound movement belongs to at most one return line (scalar reference, no FK to INV).
            e.HasIndex(x => new { x.CompanyId, x.InvMovementId }).IsUnique()
                .HasFilter("[inv_movement_id] IS NOT NULL");
            // BR-SD-044 return-balance aggregation under the delivery-line lock.
            e.HasIndex(x => new { x.CompanyId, x.DeliveryLineId, x.ReturnId });
            e.HasIndex(x => new { x.CompanyId, x.OrderLineId, x.ReturnId });
            e.HasIndex(x => new { x.CompanyId, x.DeliveryId });
            e.HasOne<SalesDelivery>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.DeliveryId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_return_line_sal_delivery");
            e.HasOne<SalesDeliveryLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.DeliveryLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_return_line_sal_delivery_line");
            e.HasOne<SalesOrderLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.OrderLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_return_line_sal_order_line");
            e.HasMany(x => x.InvoiceAllocations).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.ReturnLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_sal_return_invoice_allocation_sal_return_line");
        });

        modelBuilder.Entity<SalesReturnInvoiceAllocation>(e =>
        {
            e.ToTable("sal_return_invoice_allocation", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("ck_sal_return_invoice_allocation_refs",
                    "[invoice_id] > 0 AND [invoice_line_id] > 0");
                t.HasCheckConstraint("ck_sal_return_invoice_allocation_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_sal_return_invoice_allocation_amounts", AmountCheck);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            ConfigureAmounts(e);
            e.HasIndex(x => new { x.CompanyId, x.ReturnLineId, x.InvoiceLineId }).IsUnique();
            // BR-SD-044 billed-balance aggregation per invoice line.
            e.HasIndex(x => new { x.CompanyId, x.InvoiceLineId, x.ReturnLineId });
            e.HasIndex(x => new { x.CompanyId, x.InvoiceId });
            e.HasOne<SalesInvoice>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_return_invoice_allocation_sal_invoice");
            e.HasOne<SalesInvoiceLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.InvoiceLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sal_return_invoice_allocation_sal_invoice_line");
        });
    }

    private static void ConfigureAmounts<TEntity>(EntityTypeBuilder<TEntity> e) where TEntity : class
    {
        foreach (var name in Amounts)
            e.Property<decimal>(name).HasPrecision(19, 4);
    }
}
