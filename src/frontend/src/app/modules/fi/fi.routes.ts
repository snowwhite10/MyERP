import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';

/** FI 應收（`/ar`）路由；API 前綴同為 `/api/v1/ar`。 */
export const FI_ROUTES: Routes = [
  { path: 'credit-limits', canActivate: [permissionGuard('AR.CREDIT.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./credit-limit-page').then(m => m.CreditLimitPage) },
  { path: 'sales-collections', canActivate: [permissionGuard('AR.COLLECTION.VIEW')],
    loadComponent: () => import('./collection-receipt-page').then(m => m.CollectionReceiptPage) },
  { path: 'sales-collections/new', canActivate: [permissionGuard('AR.COLLECTION.CREATE')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./collection-receipt-form').then(m => m.CollectionReceiptForm) },
  { path: 'sales-collections/:id/edit', canActivate: [permissionGuard('AR.COLLECTION.EDIT')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./collection-receipt-form').then(m => m.CollectionReceiptForm) },
  { path: 'collection-settlements', canActivate: [permissionGuard('AR.SETTLEMENT.VIEW')],
    loadComponent: () => import('./collection-settlement-page').then(m => m.CollectionSettlementPage) },
];
