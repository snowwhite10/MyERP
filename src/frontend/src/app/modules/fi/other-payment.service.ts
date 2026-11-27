import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

export type OtherPaymentStatus = 'Draft' | 'Pending' | 'Rejected' | 'Approved' | 'Posted';
export const OTHER_PAYMENT_STATUSES = ['Draft', 'Pending', 'Rejected', 'Posted'] as const;
export interface OtherPaymentSummary {
  id: number; documentNo: string; payeeDescription: string; businessDate: string; currency: string; amount: string;
  expenseCategoryId: number; expenseCategoryCode: string | null; expenseCategoryName: string | null;
  bankAccountId: number; bankAccountCode: string | null; bankAccountName: string | null;
  bankReference: string; status: OtherPaymentStatus; version: number; journalId: number | null;
}
export interface OtherPaymentQuery {
  page: number; pageSize: number; keyword?: string; status?: string; from?: string; to?: string; sort?: string;
}
export interface OtherPaymentMapRow {
  expenseCategoryId: number; expenseCategoryCode: string | null; expenseCategoryName: string | null;
  expenseCategoryActive: boolean; configured: boolean; version: number;
  expenseAccount: { id: number; code: string | null; name: string | null; isActive: boolean; isPostable: boolean; isUsable: boolean } | null;
}

@Injectable({ providedIn: 'root' })
export class OtherPaymentService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, []); }
  list(query: OtherPaymentQuery): Observable<PagedResult<OtherPaymentSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    return this.http.get('/api/v1/ap/other-payments', { params, responseType: 'text' }).pipe(jsonTextErrors(),
      map(text => parseJsonWithDecimalText<PagedResult<OtherPaymentSummary>>(text, ['amount'])));
  }
  maps(): Observable<{ items: OtherPaymentMapRow[] }> {
    return this.http.get<{ items: OtherPaymentMapRow[] }>('/api/v1/gl/other-payment-account-map');
  }
}
