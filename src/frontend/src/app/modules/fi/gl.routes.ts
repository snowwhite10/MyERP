import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';

/** FI general ledger (`/gl`) routes; the API prefix is also `/api/v1/gl`. */
export const GL_ROUTES: Routes = [
  { path: 'periods', canActivate: [permissionGuard('GL.PERIOD.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./gl-period-page').then(m => m.GlPeriodPage) },
  { path: 'journals', canActivate: [permissionGuard('GL.JOURNAL.VIEW')],
    loadComponent: () => import('./gl-journal-page').then(m => m.GlJournalPage) },
  { path: 'sales-invoice-account-map', canActivate: [permissionGuard('GL.SALES_MAP.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./sales-map-page').then(m => m.SalesMapPage) },
  { path: 'purchase-invoice-account-map', canActivate: [permissionGuard('GL.PURCHASE_MAP.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./purchase-map-page').then(m => m.PurchaseMapPage) },
];
