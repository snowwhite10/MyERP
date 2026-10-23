import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';

export const PUR_ROUTES: Routes = [
  { path: 'rfqs/:id/comparison', canActivate: [permissionGuard('PUR.RFQ.COMPARE')],
    loadComponent: () => import('./comparison-page').then(m => m.ComparisonPage) },
  { path: 'rfqs/new', canActivate: [permissionGuard('PUR.RFQ.CREATE')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./rfq-form').then(m => m.RfqForm) },
  { path: 'rfqs/:id/edit', canActivate: [permissionGuard('PUR.RFQ.EDIT')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./rfq-form').then(m => m.RfqForm) },
  { path: 'rfqs/:id/quotations', canActivate: [permissionGuard('PUR.QUOTATION.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./quotation-page').then(m => m.QuotationPage) },
  { path: 'rfqs', canActivate: [permissionGuard('PUR.RFQ.VIEW')],
    loadComponent: () => import('./rfq-page').then(m => m.RfqPage) },
  { path: 'requisitions/new', canActivate: [permissionGuard('PUR.REQUISITION.CREATE')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./requisition-form').then(m => m.RequisitionForm) },
  { path: 'requisitions/:id/edit', canActivate: [permissionGuard('PUR.REQUISITION.EDIT')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./requisition-form').then(m => m.RequisitionForm) },
  { path: 'requisitions', canActivate: [permissionGuard('PUR.REQUISITION.VIEW')],
    loadComponent: () => import('./requisition-page').then(m => m.RequisitionPage) },
];
