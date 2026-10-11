import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { CUSTOMER_CATEGORY_PAGE } from './customer-category.service';
import { CATEGORY_PAGE } from './master-category.service';
import { SUPPLIER_CATEGORY_PAGE } from './supplier-category.service';

/** MD 主檔模組；子頁面由各主檔工作單元加入。 */
export const MD_ROUTES: Routes = [
  {
    path: 'customers',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.CUSTOMER.VIEW')],
    loadComponent: () => import('./customer-list').then((m) => m.CustomerList),
  },
  {
    path: 'customer-categories',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.CUSTOMER_CATEGORY.VIEW')],
    providers: [{ provide: CATEGORY_PAGE, useValue: CUSTOMER_CATEGORY_PAGE }],
    loadComponent: () => import('./category-list').then((m) => m.CategoryList),
  },
  {
    path: 'supplier-categories',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.SUPPLIER_CATEGORY.VIEW')],
    providers: [{ provide: CATEGORY_PAGE, useValue: SUPPLIER_CATEGORY_PAGE }],
    loadComponent: () => import('./category-list').then((m) => m.CategoryList),
  },
  {
    path: '',
    pathMatch: 'full',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.MASTER.VIEW')],
    loadComponent: () => import('./master-data-home').then((m) => m.MasterDataHome),
  },
];

export default MD_ROUTES;
