import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guard';
import { i18nScopeGuard } from './core/i18n/i18n-scope.service';
import { MainLayout } from './layout/main-layout/main-layout';

/**
 * 路由：登入頁在主框架之外；主框架須登入（authGuard），其下放各頁面；業務模組以 loadChildren lazy-load，
 * 並以 i18nScopeGuard 載入模組語言檔，例如
 * `{ path: 'sal', canActivate: [i18nScopeGuard('sal')], loadChildren: () => import('./modules/sal/sal.routes') }`。
 */
export const routes: Routes = [
  {
    path: 'login',
    title: 'MyERP',
    canActivate: [guestGuard, i18nScopeGuard('sys')],
    loadComponent: () => import('./pages/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: MainLayout,
    canActivate: [authGuard],
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
