import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import type { SalesQuotationCreatePage } from './quotation-create-page';
import type { SalesOrderCreatePage } from './order-create-page';

export const SD_ROUTES: Routes = [
  { path: 'orders/new', canActivate: [permissionGuard('SAL.ORDER.CREATE')],
    canDeactivate: [(component: SalesOrderCreatePage) => component.canLeave()],
    loadComponent: () => import('./order-create-page').then(m => m.SalesOrderCreatePage) },
  { path: 'orders/:id/edit', canActivate: [permissionGuard('SAL.ORDER.VIEW'), permissionGuard('SAL.ORDER.EDIT')],
    canDeactivate: [(component: SalesOrderCreatePage) => component.canLeave()],
    loadComponent: () => import('./order-create-page').then(m => m.SalesOrderCreatePage) },
  { path: 'deliveries', canActivate: [permissionGuard('SAL.DELIVERY.VIEW')],
    loadComponent: () => import('./delivery-page').then(m => m.SalesDeliveryPage) },
  { path: 'orders', canActivate: [permissionGuard('SAL.ORDER.VIEW')],
    loadComponent: () => import('./order-page').then(m => m.SalesOrderPage) },
  { path: 'quotations/new', canActivate: [permissionGuard('SAL.QUOTATION.CREATE')],
    canDeactivate: [(component: SalesQuotationCreatePage) => component.canLeave()],
    loadComponent: () => import('./quotation-create-page').then(m => m.SalesQuotationCreatePage) },
  { path: 'quotations/:id/edit', canActivate: [permissionGuard('SAL.QUOTATION.EDIT')],
    canDeactivate: [(component: SalesQuotationCreatePage) => component.canLeave()],
    loadComponent: () => import('./quotation-create-page').then(m => m.SalesQuotationCreatePage) },
  { path: 'quotations', canActivate: [permissionGuard('SAL.QUOTATION.VIEW')],
    loadComponent: () => import('./quotation-page').then(m => m.SalesQuotationPage) },
];
