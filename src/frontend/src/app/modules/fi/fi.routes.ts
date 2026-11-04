import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';

/** FI 應收（`/ar`）路由；API 前綴同為 `/api/v1/ar`。 */
export const FI_ROUTES: Routes = [
  { path: 'credit-limits', canActivate: [permissionGuard('AR.CREDIT.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./credit-limit-page').then(m => m.CreditLimitPage) },
];
