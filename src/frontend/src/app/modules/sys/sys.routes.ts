import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';
import { OrganizationKind, organizationPermission } from './organization/organization.service';

const organizationRoute = (path: string, kind: OrganizationKind) => ({
  path,
  title: 'MyERP',
  canActivate: [permissionGuard(organizationPermission(kind, 'VIEW'))],
  data: { kind },
  loadComponent: () => import('./organization/organization-list').then((m) => m.OrganizationList),
});

/** SYS 系統管理（lazy-loaded，`/sys/...`）。 */
export const SYS_ROUTES: Routes = [
  organizationRoute('companies', 'company'),
  organizationRoute('branches', 'branch'),
  organizationRoute('departments', 'department'),
  {
    path: 'users',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.USER.VIEW')],
    loadComponent: () => import('./users/user-list').then((m) => m.UserList),
  },
  {
    path: 'roles',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.ROLE.VIEW')],
    loadComponent: () => import('./roles/role-list').then((m) => m.RoleList),
  },
  {
    path: 'number-rules',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.NUMBER_RULE.VIEW')],
    loadComponent: () => import('./number-rules/number-rule-list').then((m) => m.NumberRuleList),
  },
  {
    path: 'approval-flows',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.APPROVAL_FLOW.VIEW')],
    loadComponent: () => import('./approvals/approval-flow-list').then((m) => m.ApprovalFlowList),
  },
  {
    path: 'approval-delegations',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.APPROVAL_DELEGATION.VIEW')],
    loadComponent: () => import('./approvals/approval-delegation-list').then((m) => m.ApprovalDelegationList),
  },
  {
    path: 'approvals',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.APPROVAL.PROCESS', 'SYS.APPROVAL.VIEW')],
    loadComponent: () => import('./approvals/approval-inbox').then((m) => m.ApprovalInbox),
  },
  {
    path: 'audit-logs',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.AUDIT.VIEW')],
    loadComponent: () => import('./audit/audit-log-list').then((m) => m.AuditLogList),
  },
  {
    path: 'ip-whitelist',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.IP_WHITELIST.VIEW')],
    loadComponent: () => import('./ip-whitelist/ip-whitelist-list').then((m) => m.IpWhitelistList),
  },
  {
    path: 'parameters',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.PARAMETER.VIEW')],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./parameters/parameter-settings').then((m) => m.ParameterSettings),
  },
  {
    // 列印設定（介面 8.56，ADR-027）：同一參數頁元件，只顯示 print 分組
    path: 'print-settings',
    title: 'MyERP',
    data: { view: 'print' },
    canActivate: [permissionGuard('SYS.PARAMETER.VIEW')],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./parameters/parameter-settings').then((m) => m.ParameterSettings),
  },
  {
    // Logo 與印章（介面 8.57，ADR-028）：權限沿用系統參數
    path: 'logo-stamp',
    title: 'MyERP',
    canActivate: [permissionGuard('SYS.PARAMETER.VIEW')],
    loadComponent: () => import('./company-images/company-images').then((m) => m.CompanyImages),
  },
];

export default SYS_ROUTES;
