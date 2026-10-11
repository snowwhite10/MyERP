using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using IpRange = MyERP.Shared.Security.IpRange;

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
        ConfigureNumbering(modelBuilder);
        ConfigureApproval(modelBuilder);
        ConfigureIpWhitelist(modelBuilder);
    }

    private static void ConfigureIpWhitelist(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IpWhitelistEntry>(e =>
        {
            e.ToTable("sys_ip_whitelist", table =>
            {
                table.HasCheckConstraint("ck_sys_ip_whitelist_target",
                    "([scope] = 'System' AND [role_id] IS NULL AND [user_id] IS NULL) OR ([scope] = 'Role' AND [role_id] IS NOT NULL AND [user_id] IS NULL) "
                    + "OR ([scope] = 'User' AND [user_id] IS NOT NULL AND [role_id] IS NULL)");
                table.HasCheckConstraint("ck_sys_ip_whitelist_dates", "[valid_from] IS NULL OR [valid_to] IS NULL OR [valid_to] >= [valid_from]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Scope).HasMaxLength(10);
            e.Property(x => x.IpRange).HasMaxLength(IpRange.MaxLength).IsUnicode(false);
            e.Property(x => x.Description).HasMaxLength(IpWhitelistEntry.MaxDescriptionLength);
            e.Ignore(x => x.TargetId);
            // SQL Server/EF 預設對 nullable 唯一索引加篩選條件，三種層級須分開建立唯一索引。
            e.HasIndex(x => x.IpRange).IsUnique().HasFilter("[scope] = 'System'").HasDatabaseName("ux_sys_ip_whitelist_system_range");
            e.HasIndex(x => new { x.RoleId, x.IpRange }).IsUnique().HasFilter("[scope] = 'Role'").HasDatabaseName("ux_sys_ip_whitelist_role_range");
            e.HasIndex(x => new { x.UserId, x.IpRange }).IsUnique().HasFilter("[scope] = 'User'").HasDatabaseName("ux_sys_ip_whitelist_user_range");
            e.HasIndex(x => x.RoleId);
            e.HasIndex(x => x.UserId);
            e.HasOne<SystemRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureApproval(ModelBuilder modelBuilder)
    {
        const string approverCheck =
            "([approver_type] = 'Role' AND [role_id] IS NOT NULL AND [user_id] IS NULL) OR ([approver_type] = 'User' AND [user_id] IS NOT NULL AND [role_id] IS NULL)";

        modelBuilder.Entity<ApprovalFlow>(e =>
        {
            e.ToTable("sys_approval_flow");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentType).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(100);
            // 每間公司每個單據類型一條流程；亦為 company_id 外鍵索引
            e.HasIndex(x => new { x.CompanyId, x.DocumentType }).IsUnique().HasDatabaseName("ux_sys_approval_flow_company_id_document_type");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<SystemCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Steps).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.FlowId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Steps).HasField("<Steps>k__BackingField");
        });

        modelBuilder.Entity<ApprovalFlowStep>(e =>
        {
            e.ToTable("sys_approval_flow_step", table =>
            {
                table.HasCheckConstraint("ck_sys_approval_flow_step_approver", approverCheck);
                table.HasCheckConstraint("ck_sys_approval_flow_step_min_amount", "[min_amount] IS NULL OR [min_amount] >= 0");
                table.HasCheckConstraint("ck_sys_approval_flow_step_step_no", $"[step_no] BETWEEN 1 AND {ApprovalFlow.MaxSteps}");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Name).HasMaxLength(100);
            // 亦為 (company_id, flow_id) 複合外鍵索引
            e.HasIndex(x => new { x.CompanyId, x.FlowId, x.StepNo }).IsUnique().HasDatabaseName("ux_sys_approval_flow_step_company_id_flow_id_step_no");
            e.HasIndex(x => x.RoleId);
            e.HasIndex(x => x.UserId);
            e.HasOne<SystemRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalDelegation>(e =>
        {
            e.ToTable("sys_approval_delegation", table =>
            {
                table.HasCheckConstraint("ck_sys_approval_delegation_dates", "[end_date] >= [start_date]");
                table.HasCheckConstraint("ck_sys_approval_delegation_users", "[delegator_user_id] <> [delegate_user_id]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Remark).HasMaxLength(200);
            // 審批時以（公司、代理人、日期）找出被代理人；重疊檢查以（公司、被代理人）查詢
            e.HasIndex(x => new { x.CompanyId, x.DelegateUserId, x.StartDate }).HasDatabaseName("ix_sys_approval_delegation_company_id_delegate_user_id_start_date");
            e.HasIndex(x => new { x.CompanyId, x.DelegatorUserId, x.StartDate }).HasDatabaseName("ix_sys_approval_delegation_company_id_delegator_user_id_start_date");
            e.HasIndex(x => x.DelegatorUserId);
            e.HasIndex(x => x.DelegateUserId);
            e.HasOne<SystemCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.DelegatorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalInstance>(e =>
        {
            e.ToTable("sys_approval_instance", table =>
            {
                table.HasCheckConstraint("ck_sys_approval_instance_amount", "[amount] >= 0");
                table.HasCheckConstraint("ck_sys_approval_instance_current_step",
                    "([status] = 'Pending' AND [current_step_no] IS NOT NULL) OR ([status] <> 'Pending' AND [current_step_no] IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentType).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Summary).HasMaxLength(200);
            // 同一單據最多一個審批中的實例（併發送審由此唯一索引擋下）
            e.HasIndex(x => new { x.CompanyId, x.DocumentType, x.DocumentId }).IsUnique().HasFilter("[status] = 'Pending'")
                .HasDatabaseName("ux_sys_approval_instance_pending_document");
            // 單據審批歷史查詢；亦為 company_id 外鍵索引
            e.HasIndex(x => new { x.CompanyId, x.DocumentType, x.DocumentId, x.SubmittedAt })
                .HasDatabaseName("ix_sys_approval_instance_company_id_document_type_document_id_submitted_at");
            e.HasIndex(x => new { x.CompanyId, x.FlowId });
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<SystemCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApprovalFlow>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.FlowId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Steps).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.InstanceId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Steps).HasField("<Steps>k__BackingField");
        });

        modelBuilder.Entity<ApprovalInstanceStep>(e =>
        {
            e.ToTable("sys_approval_instance_step", table => table.HasCheckConstraint("ck_sys_approval_instance_step_approver", approverCheck));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Comment).HasMaxLength(500);
            // 亦為 (company_id, instance_id) 複合外鍵索引
            e.HasIndex(x => new { x.CompanyId, x.InstanceId, x.StepNo }).IsUnique().HasDatabaseName("ux_sys_approval_instance_step_company_id_instance_id_step_no");
            // 待辦清單：目前公司狀態為 Pending 的步驟，依審批人篩選
            e.HasIndex(x => new { x.CompanyId, x.Status, x.UserId }).HasDatabaseName("ix_sys_approval_instance_step_company_id_status_user_id");
            e.HasIndex(x => new { x.CompanyId, x.Status, x.RoleId }).HasDatabaseName("ix_sys_approval_instance_step_company_id_status_role_id");
            e.HasIndex(x => x.RoleId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ActedBy);
            e.HasIndex(x => x.OnBehalfOfUserId);
            e.HasOne<SystemRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.ActedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SystemUser>().WithMany().HasForeignKey(x => x.OnBehalfOfUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureNumbering(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NumberRule>(e =>
        {
            e.ToTable("sys_number_rule", table => table.HasCheckConstraint(
                "ck_sys_number_rule_sequence_length", $"[sequence_length] BETWEEN {NumberRule.MinSequenceLength} AND {NumberRule.MaxSequenceLength}"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.DocumentType).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Prefix).HasMaxLength(NumberRule.MaxPrefixLength).IsUnicode(false);
            e.Ignore(x => x.MaxSequence);
            // 每間公司每個單據類型一條規則；亦為 company_id 外鍵索引
            e.HasIndex(x => new { x.CompanyId, x.DocumentType }).IsUnique().HasDatabaseName("ux_sys_number_rule_company_id_document_type");
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasOne<SystemCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NumberSequence>(e =>
        {
            e.ToTable("sys_number_sequence", table => table.HasCheckConstraint("ck_sys_number_sequence_current_value", "[current_value] >= 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.PeriodKey).HasMaxLength(8).IsUnicode(false);
            // MERGE 以此唯一索引定位計數列；亦為 (company_id, rule_id) 複合外鍵索引
            e.HasIndex(x => new { x.CompanyId, x.RuleId, x.PeriodKey }).IsUnique().HasDatabaseName("ux_sys_number_sequence_company_id_rule_id_period_key");
            e.HasOne<NumberRule>().WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.RuleId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
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
