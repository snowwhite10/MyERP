using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class QuotationDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupplierQuotation>(e =>
        {
            e.ToTable("pur_supplier_quotation", t =>
            {
                t.HasCheckConstraint("ck_pur_supplier_quotation_revision", "[current_revision_no] > 0");
                t.HasCheckConstraint("ck_pur_supplier_quotation_status", "[status] IN ('Active', 'Withdrawn', 'Awarded')");
                t.HasCheckConstraint("ck_pur_supplier_quotation_supplier", "[supplier_id] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.RfqId, x.SupplierId }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.SupplierId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.Id });
            e.HasOne<RfqInvitation>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.RfqId, x.SupplierId })
                .HasPrincipalKey(x => new { x.CompanyId, x.RfqId, x.SupplierId })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Revisions).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.QuotationId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<QuotationRevision>(e =>
        {
            e.ToTable("pur_quotation_revision", t =>
            {
                t.HasCheckConstraint("ck_pur_quotation_revision_no", "[revision_no] > 0");
                t.HasCheckConstraint("ck_pur_quotation_revision_dates", "[valid_until] >= [quote_date]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Remark).HasMaxLength(500);
            e.HasIndex(x => new { x.CompanyId, x.QuotationId, x.RevisionNo }).IsUnique();
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.RevisionId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<QuotationLine>(e =>
        {
            e.ToTable("pur_quotation_line", t =>
            {
                t.HasCheckConstraint("ck_pur_quotation_line_price", "[unit_price] >= 0");
                t.HasCheckConstraint("ck_pur_quotation_line_currency", "LEN([currency_code]) = 3");
                t.HasCheckConstraint("ck_pur_quotation_line_tax", "[tax_rate_id] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.UnitPrice).HasPrecision(19, 6);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.HasIndex(x => new { x.CompanyId, x.RevisionId, x.RfqLineId }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.RfqLineId });
            e.HasIndex(x => new { x.CompanyId, x.TaxRateId });
            e.HasOne<RfqLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.RfqLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
