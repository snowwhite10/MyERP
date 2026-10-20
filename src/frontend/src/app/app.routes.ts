import { Routes } from '@angular/router';
import { authGuard, changePasswordGuard, guestGuard } from './core/auth/auth.guard';
import { authorizationGuard } from './core/auth/permission.service';
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
    path: 'change-password',
    title: 'MyERP',
    canActivate: [changePasswordGuard, i18nScopeGuard('sys')],
    loadComponent: () => import('./pages/change-password/change-password.page').then((m) => m.ChangePasswordPage),
  },
  {
    path: '',
    component: MainLayout,
    // 先確認登入，再載入權限與可存取公司（WU-SYS-004）；模組路由可再加 permissionGuard('XX.YY.VIEW')
    canActivate: [authGuard, authorizationGuard],
    children: [
      {
        path: 'personal-settings',
        canActivate: [i18nScopeGuard('sys')],
        title: 'MyERP',
        loadComponent: () => import('./pages/personal-settings/personal-settings.page').then((m) => m.PersonalSettingsPage),
      },
      {
        path: '',
        pathMatch: 'full',
        title: 'MyERP',
        loadComponent: () => import('./pages/home/home.page').then((m) => m.HomePage),
      },
      {
        path: 'sys',
        canActivate: [i18nScopeGuard('sys')],
        loadChildren: () => import('./modules/sys/sys.routes'),
      },
      {
        path: 'md',
        canActivate: [i18nScopeGuard('md')],
        loadChildren: () => import('./modules/md/md.routes'),
      },
      {
        path: 'inv',
        canActivate: [i18nScopeGuard('inv')],
        loadChildren: () => import('./modules/inv/inv.routes').then(m => m.INV_ROUTES),
      },
      {
        path: 'pur',
        canActivate: [i18nScopeGuard('pur')],
        loadChildren: () => import('./modules/pur/pur.routes').then(m => m.PUR_ROUTES),
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
