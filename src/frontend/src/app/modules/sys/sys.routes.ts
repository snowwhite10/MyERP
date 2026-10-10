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
];

export default SYS_ROUTES;
