using MyERP.Modules.System.Contracts;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Application.Authorization;

/// <summary>SYS 模組的權限代碼（api.md「權限」小節）。</summary>
public sealed class SystemPermissions : IPermissionDefinitionProvider
{
    public const string CompanyView = "SYS.COMPANY.VIEW";
    public const string CompanyCreate = "SYS.COMPANY.CREATE";
    public const string CompanyEdit = "SYS.COMPANY.EDIT";
    public const string BranchView = "SYS.BRANCH.VIEW";
    public const string BranchCreate = "SYS.BRANCH.CREATE";
    public const string BranchEdit = "SYS.BRANCH.EDIT";
    public const string DepartmentView = "SYS.DEPARTMENT.VIEW";
    public const string DepartmentCreate = "SYS.DEPARTMENT.CREATE";
    public const string DepartmentEdit = "SYS.DEPARTMENT.EDIT";
    public const string UserView = "SYS.USER.VIEW";
    public const string UserCreate = "SYS.USER.CREATE";
    public const string UserEdit = "SYS.USER.EDIT";
    public const string UserResetPassword = "SYS.USER.RESET_PASSWORD";
    public const string RoleView = "SYS.ROLE.VIEW";
    public const string RoleCreate = "SYS.ROLE.CREATE";
    public const string RoleEdit = "SYS.ROLE.EDIT";
    public const string RoleDelete = "SYS.ROLE.DELETE";
    public const string AuditView = "SYS.AUDIT.VIEW";
    public const string NumberRuleView = "SYS.NUMBER_RULE.VIEW";
    public const string NumberRuleCreate = "SYS.NUMBER_RULE.CREATE";
    public const string NumberRuleEdit = "SYS.NUMBER_RULE.EDIT";
    public const string ApprovalFlowView = "SYS.APPROVAL_FLOW.VIEW";
    public const string ApprovalFlowCreate = "SYS.APPROVAL_FLOW.CREATE";
    public const string ApprovalFlowEdit = "SYS.APPROVAL_FLOW.EDIT";
    public const string ApprovalDelegationView = "SYS.APPROVAL_DELEGATION.VIEW";
    public const string ApprovalDelegationCreate = "SYS.APPROVAL_DELEGATION.CREATE";
    public const string ApprovalDelegationEdit = "SYS.APPROVAL_DELEGATION.EDIT";
    public const string ApprovalView = "SYS.APPROVAL.VIEW";
    public const string ApprovalProcess = "SYS.APPROVAL.PROCESS";

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        .. new[]
        {
            CompanyView, CompanyCreate, CompanyEdit,
            BranchView, BranchCreate, BranchEdit,
            DepartmentView, DepartmentCreate, DepartmentEdit,
            UserView, UserCreate, UserEdit, UserResetPassword,
            RoleView, RoleCreate, RoleEdit, RoleDelete,
            AuditView,
            NumberRuleView, NumberRuleCreate, NumberRuleEdit,
            ApprovalFlowView, ApprovalFlowCreate, ApprovalFlowEdit,
            ApprovalDelegationView, ApprovalDelegationCreate, ApprovalDelegationEdit,
            ApprovalView, ApprovalProcess,
        }.Select(code => new PermissionDefinition(code, SystemModuleContracts.ModuleCode)),
    ];
}
