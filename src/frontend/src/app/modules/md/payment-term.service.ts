import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { CategoryQuery, MasterCategory } from './master-category.service';

export const PAYMENT_TERM_API = '/api/v1/md/payment-terms';
export type DueType = 'NetDays' | 'ClosingDays' | 'ClosingMonthDay';
export interface PaymentRule { dueType: DueType; days: number | null; closingDay: number | null; months: number | null; paymentDay: number | null }
export interface PaymentTerm extends MasterCategory, PaymentRule {}
export interface PaymentDueDate { code: string; documentDate: string; dueDate: string }

export function paymentRule(value: PaymentRule): PaymentRule {
  return { dueType: value.dueType,
    days: value.dueType === 'ClosingMonthDay' ? null : value.days,
    closingDay: value.dueType === 'NetDays' ? null : value.closingDay,
    months: value.dueType === 'ClosingMonthDay' ? value.months : null,
    paymentDay: value.dueType === 'ClosingMonthDay' ? value.paymentDay : null };
}

export function validPaymentRule(value: PaymentRule): boolean {
  const v = paymentRule(value);
  const integer = (n: number | null, min: number, max: number) => n !== null && Number.isInteger(n) && n >= min && n <= max;
  if (v.dueType !== 'ClosingMonthDay' && !integer(v.days, 0, 365)) return false;
  if (v.dueType !== 'NetDays' && !integer(v.closingDay, 1, 31)) return false;
  if (v.dueType === 'ClosingMonthDay') return integer(v.months, 0, 12) && integer(v.paymentDay, 1, 31)
    && (v.months !== 0 || v.paymentDay! > v.closingDay!);
  return true;
}

@Injectable({ providedIn: 'root' })
export class PaymentTermService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.PAYMENT_TERM.DUPLICATE_CODE', 'MD.PAYMENT_TERM.NOT_FOUND',
      'MD.PAYMENT_TERM.IN_USE', 'MD.PAYMENT_TERM.INACTIVE', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: CategoryQuery): Observable<PagedResult<PaymentTerm>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<PaymentTerm>>(PAYMENT_TERM_API, { params });
  }
  create(body: PaymentRule & { code: string; name: string }): Observable<PaymentTerm> {
    return this.http.post<PaymentTerm>(PAYMENT_TERM_API,
      { ...paymentRule(body), code: body.code.trim().toUpperCase(), name: body.name.trim() });
  }
  update(id: number, body: PaymentRule & { name: string; isActive: boolean; version: number }): Observable<PaymentTerm> {
    return this.http.put<PaymentTerm>(`${PAYMENT_TERM_API}/${id}`,
      { ...paymentRule(body), name: body.name.trim(), isActive: body.isActive, version: body.version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${PAYMENT_TERM_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  dueDate(code: string, documentDate: string): Observable<PaymentDueDate> {
    return this.http.get<PaymentDueDate>(`${PAYMENT_TERM_API}/due-date`,
      { params: new HttpParams().set('code', code).set('documentDate', documentDate) });
  }
}
