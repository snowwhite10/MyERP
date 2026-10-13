import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';

export const INV_ROUTES: Routes = [
  { path: 'other-receipts', canActivate: [permissionGuard('INV.OTHER_RECEIPT.VIEW')],
    loadComponent: () => import('./other-receipt-page').then(m => m.OtherReceiptPage) },
  { path: 'other-receipts/new', canActivate: [permissionGuard('INV.OTHER_RECEIPT.CREATE')],
    loadComponent: () => import('./other-receipt-form').then(m => m.OtherReceiptForm) },
  { path: 'other-receipts/:id/edit', canActivate: [permissionGuard('INV.OTHER_RECEIPT.EDIT')],
    loadComponent: () => import('./other-receipt-form').then(m => m.OtherReceiptForm) },
];
