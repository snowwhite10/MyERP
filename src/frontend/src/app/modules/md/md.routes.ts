import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';

/** MD 主檔模組；子頁面由各主檔工作單元加入。 */
export const MD_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.MASTER.VIEW')],
    loadComponent: () => import('./master-data-home').then((m) => m.MasterDataHome),
  },
];

export default MD_ROUTES;
