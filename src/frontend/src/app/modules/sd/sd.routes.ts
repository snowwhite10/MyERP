import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';

export const SD_ROUTES: Routes = [
  { path: 'quotations', canActivate: [permissionGuard('SAL.QUOTATION.VIEW')],
    loadComponent: () => import('./quotation-page').then(m => m.SalesQuotationPage) },
];
