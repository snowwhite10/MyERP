import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';

/** FI general ledger (`/gl`) routes; the API prefix is also `/api/v1/gl`. */
export const GL_ROUTES: Routes = [
  { path: 'periods', canActivate: [permissionGuard('GL.PERIOD.VIEW')],
    loadComponent: () => import('./gl-period-page').then(m => m.GlPeriodPage) },
];
