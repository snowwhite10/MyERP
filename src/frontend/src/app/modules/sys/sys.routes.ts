import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
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
];

export default SYS_ROUTES;
