using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>SD 發票科目映射（WU-FI-002a4a；REQ_DS43.191；ADR-109）。</summary>
public sealed class SalesInvoiceAccountMapDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesInvoiceAccountMapping>(e =>
        {
            e.ToTable("gl_sales_invoice_account_map", t =>
            {
                t.HasCheckConstraint("ck_gl_sales_invoice_account_map_identity", "[company_id] > 0 AND [version] > 0");
                t.HasCheckConstraint("ck_gl_sales_invoice_account_map_codes",
                    "LEN(TRIM([receivable_account_code])) > 0 AND LEN(TRIM([revenue_account_code])) > 0 AND LEN(TRIM([output_tax_account_code])) > 0");
                t.HasTrigger("tr_gl_sales_invoice_account_map_guard");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ReceivableAccountCode).HasMaxLength(SalesInvoiceAccountMapping.CodeMaxLength).IsUnicode(false);
            e.Property(x => x.RevenueAccountCode).HasMaxLength(SalesInvoiceAccountMapping.CodeMaxLength).IsUnicode(false);
            e.Property(x => x.OutputTaxAccountCode).HasMaxLength(SalesInvoiceAccountMapping.CodeMaxLength).IsUnicode(false);
            e.HasIndex(x => x.CompanyId).IsUnique().HasDatabaseName("ux_gl_sales_invoice_account_map_company_id");
        });
    }
}
