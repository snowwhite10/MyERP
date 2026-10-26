import { Injectable } from '@angular/core';
import { CategoryPage, MasterCategory, MasterCategoryService } from './master-category.service';

export const CUSTOMER_CATEGORY_API = '/api/v1/md/customer-categories';

export type CustomerCategory = MasterCategory;

@Injectable({ providedIn: 'root' })
export class CustomerCategoryService extends MasterCategoryService {
  constructor() { super(CUSTOMER_CATEGORY_API, 'MD.CUSTOMER_CATEGORY'); }
}

export const CUSTOMER_CATEGORY_PAGE: CategoryPage = {
  urlQuery: true,
  api: CustomerCategoryService, permission: 'MD.CUSTOMER_CATEGORY', i18n: 'md.customerCategory',
};
