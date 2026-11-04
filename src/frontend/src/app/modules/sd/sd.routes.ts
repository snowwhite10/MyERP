import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import type { SalesQuotationCreatePage } from './quotation-create-page';

export const SD_ROUTES: Routes = [
  { path: 'quotations/new', canActivate: [permissionGuard('SAL.QUOTATION.CREATE')],
    canDeactivate: [(component: SalesQuotationCreatePage) => component.canLeave()],
    loadComponent: () => import('./quotation-create-page').then(m => m.SalesQuotationCreatePage) },
  { path: 'quotations', canActivate: [permissionGuard('SAL.QUOTATION.VIEW')],
    loadComponent: () => import('./quotation-page').then(m => m.SalesQuotationPage) },
];
