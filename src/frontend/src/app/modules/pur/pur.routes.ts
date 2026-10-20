import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';

export const PUR_ROUTES: Routes = [
  { path: 'requisitions', canActivate: [permissionGuard('PUR.REQUISITION.VIEW')],
    loadComponent: () => import('./requisition-page').then(m => m.RequisitionPage) },
];
