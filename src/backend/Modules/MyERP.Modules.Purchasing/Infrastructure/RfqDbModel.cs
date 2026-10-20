using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class RfqDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rfq>(e =>
        {
            e.ToTable("pur_rfq", t =>
            {
                t.HasCheckConstraint("ck_pur_rfq_status", "[status] IN ('Draft', 'Sent', 'Closed')");
                t.HasCheckConstraint("ck_pur_rfq_dates", "[reply_deadline] >= [business_date]");
                t.HasCheckConstraint("ck_pur_rfq_owner", "[owner_id] > 0");
                t.HasCheckConstraint("ck_pur_rfq_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                t.HasCheckConstraint("ck_pur_rfq_sent", "([status] = 'Draft' AND [sent_at] IS NULL) OR ([status] IN ('Sent', 'Closed') AND [sent_at] IS NOT NULL)");
                t.HasCheckConstraint("ck_pur_rfq_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.Ignore(x => x.OwnerUserId);
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.OwnerId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.BranchId, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.DepartmentId, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.RfqId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Invitations).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.RfqId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RfqLine>(e =>
        {
            e.ToTable("pur_rfq_line", t =>
            {
                t.HasCheckConstraint("ck_pur_rfq_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_pur_rfq_line_item", "[item_id] > 0");
                t.HasCheckConstraint("ck_pur_rfq_line_quantity", "[quantity] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.HasIndex(x => new { x.CompanyId, x.RfqId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.ItemId });
            e.HasIndex(x => new { x.CompanyId, x.RequisitionLineId });
            e.HasOne<RequisitionLine>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.RequisitionLineId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RfqInvitation>(e =>
        {
            e.ToTable("pur_rfq_invitation", t =>
            {
                t.HasCheckConstraint("ck_pur_rfq_invitation_supplier", "[supplier_id] > 0");
                t.HasCheckConstraint("ck_pur_rfq_invitation_snapshot", "([sent_at] IS NULL AND [supplier_code_snapshot] IS NULL AND [supplier_name_snapshot] IS NULL) OR ([sent_at] IS NOT NULL AND [supplier_code_snapshot] IS NOT NULL AND [supplier_name_snapshot] IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SupplierCodeSnapshot).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.SupplierNameSnapshot).HasMaxLength(200);
            e.HasIndex(x => new { x.CompanyId, x.RfqId, x.SupplierId }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.SupplierId });
        });
    }
}
