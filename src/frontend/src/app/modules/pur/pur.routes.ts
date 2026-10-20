import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';

export const PUR_ROUTES: Routes = [
  { path: 'requisitions/new', canActivate: [permissionGuard('PUR.REQUISITION.CREATE')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./requisition-form').then(m => m.RequisitionForm) },
  { path: 'requisitions/:id/edit', canActivate: [permissionGuard('PUR.REQUISITION.EDIT')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./requisition-form').then(m => m.RequisitionForm) },
  { path: 'requisitions', canActivate: [permissionGuard('PUR.REQUISITION.VIEW')],
    loadComponent: () => import('./requisition-page').then(m => m.RequisitionPage) },
];
