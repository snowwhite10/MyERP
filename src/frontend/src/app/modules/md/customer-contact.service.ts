import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { CUSTOMER_API } from './customer.service';

export interface CustomerContact {
  id: number; customerId: number; name: string; position: string | null; mobile: string | null; phone: string | null;
  email: string | null; messagingId: string | null; note: string | null; isDefault: boolean; isActive: boolean; version: number;
}
export interface CustomerAddress {
  id: number; customerId: number; address: string; postalCode: string | null; recipient: string | null; phone: string | null;
  isDefault: boolean; isActive: boolean; version: number;
}
export type ContactDraft = Omit<CustomerContact, 'id' | 'customerId' | 'isActive' | 'version'>;
export type AddressDraft = Omit<CustomerAddress, 'id' | 'customerId' | 'isActive' | 'version'>;
/** 修改時另帶啟用與版本。 */
export type Revision<T> = T & { isActive: boolean; version: number };

const KNOWN_CODES = ['MD.CUSTOMER.NOT_FOUND', 'MD.CUSTOMER_CONTACT.NOT_FOUND', 'MD.CUSTOMER_ADDRESS.NOT_FOUND',
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];

/** 去除前後空白；空字串改為 null（選填欄位）。 */
function clean<T extends object>(body: T): T {
  return Object.fromEntries(Object.entries(body).map(([key, value]) =>
    [key, typeof value === 'string' ? (value.trim() || null) : value])) as T;
}

/** 客戶聯絡人與地址子資源 API（ADR-043）：列表不分頁、預設排第一；修改與刪除帶版本。 */
@Injectable({ providedIn: 'root' })
export class CustomerContactService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string { return apiErrorKey(error, KNOWN_CODES); }

  listContacts(customerId: number): Observable<CustomerContact[]> {
    return this.http.get<CustomerContact[]>(this.url(customerId, 'contacts'));
  }
  createContact(customerId: number, body: ContactDraft): Observable<CustomerContact> {
    return this.http.post<CustomerContact>(this.url(customerId, 'contacts'), clean(body));
  }
  updateContact(customerId: number, id: number, body: Revision<ContactDraft>): Observable<CustomerContact> {
    return this.http.put<CustomerContact>(`${this.url(customerId, 'contacts')}/${id}`, clean(body));
  }
  removeContact(customerId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url(customerId, 'contacts')}/${id}`, { params: new HttpParams().set('version', version) });
  }

  listAddresses(customerId: number): Observable<CustomerAddress[]> {
    return this.http.get<CustomerAddress[]>(this.url(customerId, 'addresses'));
  }
  createAddress(customerId: number, body: AddressDraft): Observable<CustomerAddress> {
    return this.http.post<CustomerAddress>(this.url(customerId, 'addresses'), clean(body));
  }
  updateAddress(customerId: number, id: number, body: Revision<AddressDraft>): Observable<CustomerAddress> {
    return this.http.put<CustomerAddress>(`${this.url(customerId, 'addresses')}/${id}`, clean(body));
  }
  removeAddress(customerId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url(customerId, 'addresses')}/${id}`, { params: new HttpParams().set('version', version) });
  }

  private url(customerId: number, child: 'contacts' | 'addresses'): string { return `${CUSTOMER_API}/${customerId}/${child}`; }
}
