using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>PUR 採購發票科目映射（WU-FI-003a2a；REQ_DS43.194；ADR-116）。</summary>
public sealed class PurchaseInvoiceAccountMapDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseInvoiceAccountMapping>(e =>
        {
            e.ToTable("gl_purchase_invoice_account_map", t =>
            {
                t.HasCheckConstraint("ck_gl_purchase_invoice_account_map_identity", "[company_id] > 0 AND [version] > 0");
                t.HasCheckConstraint("ck_gl_purchase_invoice_account_map_codes",
                    "LEN(TRIM([payable_account_code])) > 0 AND LEN(TRIM([purchase_account_code])) > 0 AND LEN(TRIM([input_tax_account_code])) > 0");
                t.HasTrigger("tr_gl_purchase_invoice_account_map_guard");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.PayableAccountCode).HasMaxLength(PurchaseInvoiceAccountMapping.CodeMaxLength).IsUnicode(false);
            e.Property(x => x.PurchaseAccountCode).HasMaxLength(PurchaseInvoiceAccountMapping.CodeMaxLength).IsUnicode(false);
            e.Property(x => x.InputTaxAccountCode).HasMaxLength(PurchaseInvoiceAccountMapping.CodeMaxLength).IsUnicode(false);
            e.HasIndex(x => x.CompanyId).IsUnique().HasDatabaseName("ux_gl_purchase_invoice_account_map_company_id");
        });
    }
}
