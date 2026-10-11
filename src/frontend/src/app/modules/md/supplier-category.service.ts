import { Injectable } from '@angular/core';
import { CategoryPage, MasterCategory, MasterCategoryService } from './master-category.service';

export const SUPPLIER_CATEGORY_API = '/api/v1/md/supplier-categories';

export type SupplierCategory = MasterCategory;

@Injectable({ providedIn: 'root' })
export class SupplierCategoryService extends MasterCategoryService {
  constructor() { super(SUPPLIER_CATEGORY_API, 'MD.SUPPLIER_CATEGORY'); }
}

export const SUPPLIER_CATEGORY_PAGE: CategoryPage = {
  api: SupplierCategoryService, permission: 'MD.SUPPLIER_CATEGORY', i18n: 'md.supplierCategory',
};
