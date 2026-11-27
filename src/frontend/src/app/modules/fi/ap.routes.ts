import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';

/** FI accounts payable routes. */
export const AP_ROUTES: Routes = [
  { path: 'other-payments', canActivate: [permissionGuard('AP.OTHER_PAYMENT.VIEW')],
    loadComponent: () => import('./other-payment-page').then(m => m.OtherPaymentPage) },
];
