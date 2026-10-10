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

        ConfigureAuthorization(modelBuilder);
    }

    private static void ConfigureAuthorization(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemRole>(e =>
        {
            e.ToTable("sys_role");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_sys_role_code");
        });

        modelBuilder.Entity<SystemRoleGrant>(e =>
        {
            e.ToTable("sys_role_permission");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.PermissionCode).HasMaxLength(100).IsUnicode(false);
            // 唯一索引以 role_id 開頭，同時作為外鍵索引
            e.HasIndex(x => new { x.RoleId, x.PermissionCode }).IsUnique().HasDatabaseName("ux_sys_role_permission_role_id_permission_code");
            e.HasOne<SystemRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemUserCompany>(e =>
        {
            e.ToTable("sys_user_company");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            // 唯一（用戶、公司），亦為 sys_user_role 複合外鍵的主鍵端與 user_id 外鍵索引
            e.HasAlternateKey(x => new { x.UserId, x.CompanyId });
            // 每位用戶最多一間預設公司
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("[is_default] = 1").HasDatabaseName("ux_sys_user_company_default");
            e.HasIndex(x => new { x.CompanyId, x.BranchId });
            e.HasIndex(x => new { x.CompanyId, x.DepartmentId });
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemBranch>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.BranchId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemDepartment>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SystemUserRole>(e =>
        {
            e.ToTable("sys_user_role");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.CompanyId, x.RoleId }).IsUnique().HasDatabaseName("ux_sys_user_role_user_id_company_id_role_id");
            e.HasIndex(x => x.RoleId);
            e.HasOne<SystemUserCompany>().WithMany()
                .HasForeignKey(x => new { x.UserId, x.CompanyId })
                .HasPrincipalKey(x => new { x.UserId, x.CompanyId })
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SystemRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
