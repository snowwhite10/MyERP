import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import { PARTNER_PAGE } from './business-partner.service';
import { CUSTOMER_CATEGORY_PAGE } from './customer-category.service';
import { CUSTOMER_PAGE } from './customer.service';
import { ITEM_BRAND_PAGE } from './item-brand.service';
import { CATEGORY_PAGE } from './master-category.service';
import { SUPPLIER_CATEGORY_PAGE } from './supplier-category.service';
import { SUPPLIER_PAGE } from './supplier.service';

/** MD 主檔模組；子頁面由各主檔工作單元加入。 */
export const MD_ROUTES: Routes = [
  {
    path: 'customers',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.CUSTOMER.VIEW')],
    providers: [{ provide: PARTNER_PAGE, useValue: CUSTOMER_PAGE }],
    loadComponent: () => import('./partner-list').then((m) => m.PartnerList),
  },
  {
    path: 'suppliers',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.SUPPLIER.VIEW')],
    providers: [{ provide: PARTNER_PAGE, useValue: SUPPLIER_PAGE }],
    loadComponent: () => import('./partner-list').then((m) => m.PartnerList),
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
    path: 'uoms',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.UOM.VIEW')],
    loadComponent: () => import('./uom-page').then((m) => m.UomPage),
  },
  {
    path: 'item-brands',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.ITEM_BRAND.VIEW')],
    providers: [{ provide: CATEGORY_PAGE, useValue: ITEM_BRAND_PAGE }],
    loadComponent: () => import('./category-list').then((m) => m.CategoryList),
  },
  {
    path: 'items',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.ITEM.VIEW')],
    loadComponent: () => import('./item-page').then((m) => m.ItemPage),
  },
  {
    path: 'item-categories',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.ITEM_CATEGORY.VIEW')],
    loadComponent: () => import('./item-category-page').then((m) => m.ItemCategoryPage),
  },
  {
    path: 'warehouses',
    title: 'MyERP',
    canActivate: [permissionGuard('MD.WAREHOUSE.VIEW')],
    loadComponent: () => import('./warehouse-page').then((m) => m.WarehousePage),
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
