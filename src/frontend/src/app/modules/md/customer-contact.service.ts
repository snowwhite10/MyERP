import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { CUSTOMER_API } from './customer.service';
import { SUPPLIER_API } from './supplier.service';

/** 往來對象聯絡人；回應另帶 `customerId` 或 `supplierId`，畫面不使用。 */
export interface PartnerContact {
  id: number; name: string; position: string | null; mobile: string | null; phone: string | null;
  email: string | null; messagingId: string | null; note: string | null; isDefault: boolean; isActive: boolean; version: number;
}
export interface CustomerContact extends PartnerContact { customerId: number }
export interface SupplierContact extends PartnerContact { supplierId: number }
export interface CustomerAddress {
  id: number; customerId: number; address: string; postalCode: string | null; recipient: string | null; phone: string | null;
  isDefault: boolean; isActive: boolean; version: number;
}
export type ContactDraft = Omit<PartnerContact, 'id' | 'isActive' | 'version'>;
export type AddressDraft = Omit<CustomerAddress, 'id' | 'customerId' | 'isActive' | 'version'>;
/** 修改時另帶啟用與版本。 */
export type Revision<T> = T & { isActive: boolean; version: number };

const KNOWN_CODES = ['MD.CUSTOMER.NOT_FOUND', 'MD.CUSTOMER_CONTACT.NOT_FOUND', 'MD.CUSTOMER_ADDRESS.NOT_FOUND',
  'MD.SUPPLIER.NOT_FOUND', 'MD.SUPPLIER_CONTACT.NOT_FOUND', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];

/** 去除前後空白；空字串改為 null（選填欄位）。 */
function clean<T extends object>(body: T): T {
  return Object.fromEntries(Object.entries(body).map(([key, value]) =>
    [key, typeof value === 'string' ? (value.trim() || null) : value])) as T;
}

/** 往來對象聯絡人子資源 API（ADR-043）：列表不分頁、預設排第一；修改與刪除帶版本。 */
export abstract class PartnerContactApi {
  protected readonly http = inject(HttpClient);

  protected constructor(private readonly base: string) {}

  errorKey(error: unknown): string { return apiErrorKey(error, KNOWN_CODES); }

  listContacts(partnerId: number): Observable<PartnerContact[]> {
    return this.http.get<PartnerContact[]>(this.url(partnerId, 'contacts'));
  }
  createContact(partnerId: number, body: ContactDraft): Observable<PartnerContact> {
    return this.http.post<PartnerContact>(this.url(partnerId, 'contacts'), clean(body));
  }
  updateContact(partnerId: number, id: number, body: Revision<ContactDraft>): Observable<PartnerContact> {
    return this.http.put<PartnerContact>(`${this.url(partnerId, 'contacts')}/${id}`, clean(body));
  }
  removeContact(partnerId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url(partnerId, 'contacts')}/${id}`, { params: new HttpParams().set('version', version) });
  }

  protected url(partnerId: number, child: string): string { return `${this.base}/${partnerId}/${child}`; }
}

/** 客戶聯絡人與送貨地址。 */
@Injectable({ providedIn: 'root' })
export class CustomerContactService extends PartnerContactApi {
  constructor() { super(CUSTOMER_API); }

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
}

/** 供應商聯絡人；供應商沒有地址子資料（ADR-043）。 */
@Injectable({ providedIn: 'root' })
export class SupplierContactService extends PartnerContactApi {
  constructor() { super(SUPPLIER_API); }
}
