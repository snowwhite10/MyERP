import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';

/** FI accounts payable routes. */
export const AP_ROUTES: Routes = [
  { path: 'other-payments', canActivate: [permissionGuard('AP.OTHER_PAYMENT.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./other-payment-page').then(m => m.OtherPaymentPage) },
];
