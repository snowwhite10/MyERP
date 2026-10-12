import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { BusinessPartner, BusinessPartnerService, PartnerPage } from './business-partner.service';
import { CustomerCategoryService } from './customer-category.service';

export const CUSTOMER_API = '/api/v1/md/customers';

export type Customer = BusinessPartner;

@Injectable({ providedIn: 'root' })
export class CustomerService extends BusinessPartnerService {
  constructor() { super(CUSTOMER_API, 'MD.CUSTOMER'); }

  submit(id: number, version: number): Observable<{ status: 'Pending' | 'Approved' | 'Rejected' }> {
    return this.http.post<{ status: 'Pending' | 'Approved' | 'Rejected' }>(`${CUSTOMER_API}/${id}/submit`, { version });
  }

  delete(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${CUSTOMER_API}/${id}`, { params: new HttpParams().set('version', version) });
  }

  override errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.CUSTOMER.NOT_FOUND', 'MD.CUSTOMER.DUPLICATE_CODE', 'MD.CUSTOMER.NOT_APPROVED',
      'MD.CUSTOMER.APPROVAL_PENDING', 'MD.CUSTOMER.ALREADY_APPROVED', 'MD.CUSTOMER.IN_USE',
      'MD.CUSTOMER.APPROVAL_STATE_CHANGED', 'SYS.APPROVAL.ALREADY_PENDING',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
}

export const CUSTOMER_PAGE: PartnerPage = {
  api: CustomerService, categoryApi: CustomerCategoryService, permission: 'MD.CUSTOMER',
  categoryPermission: 'MD.CUSTOMER_CATEGORY.VIEW', i18n: 'md.customer', testId: 'md-customer', contacts: 'customer', imports: 'customer',
};
