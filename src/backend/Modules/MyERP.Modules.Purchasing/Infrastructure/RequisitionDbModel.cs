using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Purchasing.Infrastructure;

public sealed class RequisitionDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Requisition>(e =>
        {
            e.ToTable("pur_requisition", t =>
            {
                t.HasCheckConstraint("ck_pur_requisition_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Approved', 'Closed')");
                t.HasCheckConstraint("ck_pur_requisition_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                t.HasCheckConstraint("ck_pur_requisition_requester", "[requester_id] > 0");
                t.HasCheckConstraint("ck_pur_requisition_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.RequesterId, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.RequisitionId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RequisitionLine>(e =>
        {
            e.ToTable("pur_requisition_line", t =>
            {
                t.HasCheckConstraint("ck_pur_requisition_line_no", "[line_no] > 0");
                t.HasCheckConstraint("ck_pur_requisition_line_item", "[item_id] > 0");
                t.HasCheckConstraint("ck_pur_requisition_line_quantity", "[quantity] > 0 AND [ordered_quantity] >= 0 AND [ordered_quantity] <= [quantity]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.OrderedQuantity).HasPrecision(19, 6);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.HasIndex(x => new { x.CompanyId, x.RequisitionId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.ItemId });
        });
    }
}
