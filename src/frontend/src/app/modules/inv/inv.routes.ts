import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';

export const INV_ROUTES: Routes = [
  { path: 'other-receipts', canActivate: [permissionGuard('INV.OTHER_RECEIPT.VIEW')],
    loadComponent: () => import('./other-receipt-page').then(m => m.OtherReceiptPage) },
  { path: 'other-receipts/new', canActivate: [permissionGuard('INV.OTHER_RECEIPT.CREATE')],
    loadComponent: () => import('./other-receipt-form').then(m => m.OtherReceiptForm) },
  { path: 'other-receipts/:id/edit', canActivate: [permissionGuard('INV.OTHER_RECEIPT.EDIT')],
    loadComponent: () => import('./other-receipt-form').then(m => m.OtherReceiptForm) },
  { path: 'other-issues', canActivate: [permissionGuard('INV.OTHER_ISSUE.VIEW')],
    loadComponent: () => import('./other-issue-page').then(m => m.OtherIssuePage) },
  { path: 'other-issues/new', canActivate: [permissionGuard('INV.OTHER_ISSUE.CREATE')],
    loadComponent: () => import('./other-issue-form').then(m => m.OtherIssueForm) },
  { path: 'other-issues/:id/edit', canActivate: [permissionGuard('INV.OTHER_ISSUE.EDIT')],
    loadComponent: () => import('./other-issue-form').then(m => m.OtherIssueForm) },
];
