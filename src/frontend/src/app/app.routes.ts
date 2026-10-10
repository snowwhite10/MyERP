import { Routes } from '@angular/router';
import { MainLayout } from './layout/main-layout/main-layout';

/**
 * 路由：主框架下放各頁面；業務模組以 loadChildren lazy-load，並以 i18nScopeGuard 載入模組語言檔，例如
 * `{ path: 'sal', canActivate: [i18nScopeGuard('sal')], loadChildren: () => import('./modules/sal/sal.routes') }`。
 */
export const routes: Routes = [
  {
    path: '',
    component: MainLayout,
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'MyERP',
        loadComponent: () => import('./pages/home/home.page').then((m) => m.HomePage),
      },
      {
        path: 'forbidden',
        data: { status: 403 },
        loadComponent: () => import('./pages/errors/error-page').then((m) => m.ErrorPage),
      },
      {
        path: '**',
        data: { status: 404 },
        loadComponent: () => import('./pages/errors/error-page').then((m) => m.ErrorPage),
      },
    ],
  },
];
