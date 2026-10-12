import { Injectable } from '@angular/core';
import { BusinessPartner, BusinessPartnerService, PartnerPage } from './business-partner.service';
import { CustomerCategoryService } from './customer-category.service';

export const CUSTOMER_API = '/api/v1/md/customers';

export type Customer = BusinessPartner;

@Injectable({ providedIn: 'root' })
export class CustomerService extends BusinessPartnerService {
  constructor() { super(CUSTOMER_API, 'MD.CUSTOMER'); }
}

export const CUSTOMER_PAGE: PartnerPage = {
  api: CustomerService, categoryApi: CustomerCategoryService, permission: 'MD.CUSTOMER',
  categoryPermission: 'MD.CUSTOMER_CATEGORY.VIEW', i18n: 'md.customer', testId: 'md-customer', contacts: true, imports: true,
};
