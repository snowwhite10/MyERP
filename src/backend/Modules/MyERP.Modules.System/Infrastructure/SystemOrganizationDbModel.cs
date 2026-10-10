using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.System.Infrastructure;

public sealed class SystemOrganizationDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemUser>(e =>
        {
            e.ToTable("sys_user");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Username).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.NormalizedUsername).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.PasswordHash).HasMaxLength(1024).IsUnicode(false);
            e.HasIndex(x => x.NormalizedUsername).IsUnique().HasDatabaseName("ux_sys_user_normalized_username");
        });

        modelBuilder.Entity<SystemCompany>(e =>
        {
            e.ToTable("sys_company", table => table.HasCheckConstraint("ck_sys_company_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_sys_company_code");
        });

        modelBuilder.Entity<SystemBranch>(e =>
        {
            e.ToTable("sys_branch", table => table.HasCheckConstraint("ck_sys_branch_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ux_sys_branch_company_id_code");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => x.CompanyId);
            e.HasOne<SystemCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SystemDepartment>(e =>
        {
            e.ToTable("sys_department", table => table.HasCheckConstraint("ck_sys_department_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CustomFields).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ux_sys_department_company_id_code");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.BranchId);
            e.HasIndex(x => x.ParentDepartmentId);
            e.HasOne<SystemCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemBranch>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.BranchId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemDepartment>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.ParentDepartmentId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
