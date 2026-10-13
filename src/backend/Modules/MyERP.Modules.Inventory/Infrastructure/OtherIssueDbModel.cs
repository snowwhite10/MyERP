using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class OtherIssueDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OtherIssue>(e =>
        {
            e.ToTable("inv_other_issue", t =>
            {
                t.HasCheckConstraint("ck_inv_other_issue_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                t.HasCheckConstraint("ck_inv_other_issue_status",
                    "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Posted')");
                t.HasCheckConstraint("ck_inv_other_issue_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                t.HasCheckConstraint("ck_inv_other_issue_posted",
                    "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Remark).HasMaxLength(500);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.Ignore(x => x.TotalAmount);
            // 單號在公司內永久唯一（含已刪草稿，不重用）
            e.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.BusinessDate, x.Id });
            e.HasIndex(x => new { x.CompanyId, x.Status, x.Id });
            e.HasMany(x => x.Lines).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.IssueId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OtherIssueLine>(e =>
        {
            e.ToTable("inv_other_issue_line", t =>
            {
                t.HasCheckConstraint("ck_inv_other_issue_line_quantity", "[quantity] > 0");
                t.HasCheckConstraint("ck_inv_other_issue_line_unit_cost", "[unit_cost] >= 0 AND [amount] >= 0");
                t.HasCheckConstraint("ck_inv_other_issue_line_line_no", "[line_no] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Quantity).HasPrecision(19, 6);
            e.Property(x => x.UnitCost).HasPrecision(19, 6);
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.HasIndex(x => new { x.CompanyId, x.IssueId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.ItemId });
            e.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.LocationId });
        });
    }
}
