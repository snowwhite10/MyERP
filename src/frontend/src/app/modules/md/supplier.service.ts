import { Injectable } from '@angular/core';
import { BusinessPartner, BusinessPartnerService, PartnerPage } from './business-partner.service';
import { SupplierCategoryService } from './supplier-category.service';

export const SUPPLIER_API = '/api/v1/md/suppliers';

export type Supplier = BusinessPartner;

@Injectable({ providedIn: 'root' })
export class SupplierService extends BusinessPartnerService {
  constructor() { super(SUPPLIER_API, 'MD.SUPPLIER'); }
}

export const SUPPLIER_PAGE: PartnerPage = {
  api: SupplierService, categoryApi: SupplierCategoryService, permission: 'MD.SUPPLIER',
  categoryPermission: 'MD.SUPPLIER_CATEGORY.VIEW', i18n: 'md.supplier', testId: 'md-supplier',
  contacts: 'supplier', imports: 'supplier',
};
