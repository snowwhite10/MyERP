import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.service';
import type { SalesQuotationCreatePage } from './quotation-create-page';
import type { SalesOrderCreatePage } from './order-create-page';
import type { SalesDeliveryCreatePage } from './delivery-create-page';
import type { SalesDeliveryEditPage } from './delivery-edit-page';
import type { SalesPriceRuleFormPage } from './price-rule-form-page';
import type { SalesInvoiceIssuePage } from './invoice-issue-page';

export const SD_ROUTES: Routes = [
  { path: 'orders/new', canActivate: [permissionGuard('SAL.ORDER.CREATE')],
    canDeactivate: [(component: SalesOrderCreatePage) => component.canLeave()],
    loadComponent: () => import('./order-create-page').then(m => m.SalesOrderCreatePage) },
  { path: 'orders/:id/edit', canActivate: [permissionGuard('SAL.ORDER.VIEW'), permissionGuard('SAL.ORDER.EDIT')],
    canDeactivate: [(component: SalesOrderCreatePage) => component.canLeave()],
    loadComponent: () => import('./order-create-page').then(m => m.SalesOrderCreatePage) },
  { path: 'deliveries/new', canActivate: [permissionGuard('SAL.DELIVERY.CREATE')],
    canDeactivate: [(component: SalesDeliveryCreatePage) => component.canLeave()],
    loadComponent: () => import('./delivery-create-page').then(m => m.SalesDeliveryCreatePage) },
  { path: 'deliveries/:id/edit', canActivate: [permissionGuard('SAL.DELIVERY.VIEW'), permissionGuard('SAL.DELIVERY.EDIT')],
    canDeactivate: [(component: SalesDeliveryEditPage) => component.canLeave()],
    loadComponent: () => import('./delivery-edit-page').then(m => m.SalesDeliveryEditPage) },
  { path: 'deliveries', canActivate: [permissionGuard('SAL.DELIVERY.VIEW')],
    loadComponent: () => import('./delivery-page').then(m => m.SalesDeliveryPage) },
  { path: 'invoices/new', canActivate: [permissionGuard('SAL.INVOICE.ISSUE')],
    canDeactivate: [(component: SalesInvoiceIssuePage) => component.canLeave()],
    loadComponent: () => import('./invoice-issue-page').then(m => m.SalesInvoiceIssuePage) },
  { path: 'invoices', canActivate: [permissionGuard('SAL.INVOICE.VIEW')],
    loadComponent: () => import('./invoice-page').then(m => m.SalesInvoicePage) },
  { path: 'returns', canActivate: [permissionGuard('SAL.RETURN.VIEW')],
    loadComponent: () => import('./return-page').then(m => m.SalesReturnPage) },
  { path: 'orders', canActivate: [permissionGuard('SAL.ORDER.VIEW')],
    loadComponent: () => import('./order-page').then(m => m.SalesOrderPage) },
  { path: 'quotations/new', canActivate: [permissionGuard('SAL.QUOTATION.CREATE')],
    canDeactivate: [(component: SalesQuotationCreatePage) => component.canLeave()],
    loadComponent: () => import('./quotation-create-page').then(m => m.SalesQuotationCreatePage) },
  { path: 'quotations/:id/edit', canActivate: [permissionGuard('SAL.QUOTATION.EDIT')],
    canDeactivate: [(component: SalesQuotationCreatePage) => component.canLeave()],
    loadComponent: () => import('./quotation-create-page').then(m => m.SalesQuotationCreatePage) },
  { path: 'quotations', canActivate: [permissionGuard('SAL.QUOTATION.VIEW')],
    loadComponent: () => import('./quotation-page').then(m => m.SalesQuotationPage) },
  { path: 'price-rules/new', canActivate: [permissionGuard('SAL.PRICING.CREATE')],
    canDeactivate: [(component: SalesPriceRuleFormPage) => component.canLeave()],
    loadComponent: () => import('./price-rule-form-page').then(m => m.SalesPriceRuleFormPage) },
  { path: 'price-rules/:id/edit', canActivate: [permissionGuard('SAL.PRICING.VIEW'), permissionGuard('SAL.PRICING.EDIT')],
    canDeactivate: [(component: SalesPriceRuleFormPage) => component.canLeave()],
    loadComponent: () => import('./price-rule-form-page').then(m => m.SalesPriceRuleFormPage) },
  { path: 'price-rules', canActivate: [permissionGuard('SAL.PRICING.VIEW')],
    loadComponent: () => import('./price-rule-page').then(m => m.SalesPriceRulePage) },
];
