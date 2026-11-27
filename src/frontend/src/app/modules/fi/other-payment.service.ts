import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
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
export interface OtherPaymentDetail extends OtherPaymentSummary { remark: string | null; createdAt: string; createdBy: number;
  updatedAt: string | null; updatedBy: number | null; }
export interface OtherPaymentDraft { payeeDescription: string; expenseCategoryId: number; bankAccountId: number;
  bankReference: string; businessDate: string; amount: string; remark: string | null; }
export interface OtherPaymentWorkflowResult { id: number; documentNo: string; status: OtherPaymentStatus; version: number;
  approval: unknown; posting: unknown | null; }
export interface OtherPaymentMapWriteResult { row: OtherPaymentMapRow; changed: boolean; }
const API = '/api/v1/ap/other-payments';
const MAP_API = '/api/v1/gl/other-payment-account-map';

@Injectable({ providedIn: 'root' })
export class OtherPaymentService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    const code = error instanceof HttpErrorResponse ? (error.error as { errorCode?: unknown } | null)?.errorCode : null;
    if (typeof code === 'string') {
      if (code.endsWith('BANK_REFERENCE_DUPLICATE')) return 'fi.otherPayment.error.reference';
      if (code === 'COMMON.CONCURRENCY.CONFLICT' || code.endsWith('VERSION_CONFLICT') || code.endsWith('SOURCE_CONFLICT'))
        return 'fi.otherPayment.error.version';
      if (code.endsWith('NOT_FOUND') || code.endsWith('SOURCE_NOT_FOUND')) return 'fi.otherPayment.error.missing';
      if (code.endsWith('INVALID_STATUS') || code.endsWith('APPROVAL_STATE_CHANGED')) return 'fi.otherPayment.error.status';
      if (code.endsWith('MAPPING_MISSING')) return 'fi.otherPayment.error.mapping';
      if (code.includes('CATEGORY_')) return 'fi.otherPayment.error.category';
      if (code.includes('FUND_ACCOUNT_')) return 'fi.otherPayment.error.fund';
      if (code.endsWith('ACCOUNT_INVALID') || code.endsWith('NOT_EXPENSE_ACCOUNT')) return 'fi.otherPayment.error.account';
      if (code.endsWith('BASE_CURRENCY_MISSING') || code.endsWith('BASE_CURRENCY_CHANGED')) return 'fi.otherPayment.error.currency';
      if (code === 'SYS.NUMBER_RULE.NOT_CONFIGURED') return 'fi.otherPayment.error.numberRule';
      if (code.startsWith('FI.OTHER_PAYMENT_POSTING.')) return 'fi.otherPayment.error.posting';
    }
    return apiErrorKey(error, []);
  }
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
    return this.http.get<{ items: OtherPaymentMapRow[] }>(MAP_API);
  }
  detail(id: number): Observable<OtherPaymentDetail> {
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(text => parseJsonWithDecimalText<OtherPaymentDetail>(text, ['amount'])));
  }
  create(draft: OtherPaymentDraft): Observable<OtherPaymentDetail> {
    return this.http.post<OtherPaymentDetail>(API, draft);
  }
  update(id: number, version: number, draft: OtherPaymentDraft): Observable<OtherPaymentDetail> {
    return this.http.put<OtherPaymentDetail>(`${API}/${id}`, { ...draft, version });
  }
  submit(id: number, version: number): Observable<OtherPaymentWorkflowResult> {
    return this.http.post<OtherPaymentWorkflowResult>(`${API}/${id}/submit`, { version });
  }
  withdraw(id: number, version: number): Observable<OtherPaymentWorkflowResult> {
    return this.http.post<OtherPaymentWorkflowResult>(`${API}/${id}/withdraw`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  saveMap(categoryId: number, version: number, code: string): Observable<OtherPaymentMapWriteResult> {
    return this.http.put<OtherPaymentMapWriteResult>(`${MAP_API}/${categoryId}`, { version, expenseAccountCode: code.trim() });
  }
}
