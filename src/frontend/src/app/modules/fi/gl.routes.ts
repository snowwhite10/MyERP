import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { unsavedChangesGuard } from '../../shared/document-page/unsaved-changes.guard';

/** FI general ledger (`/gl`) routes; the API prefix is also `/api/v1/gl`. */
export const GL_ROUTES: Routes = [
  { path: 'periods', canActivate: [permissionGuard('GL.PERIOD.VIEW')], canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./gl-period-page').then(m => m.GlPeriodPage) },
  { path: 'journals', canActivate: [permissionGuard('GL.JOURNAL.VIEW')],
    loadComponent: () => import('./gl-journal-page').then(m => m.GlJournalPage) },
];
