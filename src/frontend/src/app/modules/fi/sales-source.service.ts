import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout, TimeoutError } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/ar/sales-sources';
const DECIMALS = ['grossAmount', 'baseGrossAmount', 'openBaseAmount', 'netAmount', 'taxAmount',
  'baseNetAmount', 'baseTaxAmount', 'exchangeRate', 'quantity'] as const;
export type SalesSourceType = 'Invoice' | 'Return';
export interface SalesSourceSummary {
  id: number; customerId: number; sourceType: SalesSourceType; sourceId: number; sourceVersion: number;
  businessDate: string; currency: string; baseCurrency: string; grossAmount: string;
  baseGrossAmount: string; glJournalId: number; receivableId: number | null; openBaseAmount: string | null;
}
export interface SalesCreditAllocation {
  id: number; creditSourceId: number; receivableId: number; originalSourceId: number;
  invoiceLineId: number; quantity: string; grossAmount: string; baseGrossAmount: string;
}
export interface SalesSourceDetail {
  source: SalesSourceSummary; netAmount: string; taxAmount: string; baseNetAmount: string;
  baseTaxAmount: string; exchangeRate: string; allocations: SalesCreditAllocation[];
}
export interface SalesSourceQuery {
  page: number; pageSize: number; customerId?: number; sourceType?: SalesSourceType;
  from?: string; to?: string; sort?: string;
}

@Injectable({ providedIn: 'root' })
export class SalesSourceService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    if (error instanceof TimeoutError || (typeof error === 'object' && error !== null && 'name' in error && error.name === 'TimeoutError'))
      return 'fi.salesSource.timeout';
    if (typeof error === 'object' && error !== null && 'status' in error && error.status === 404)
      return 'fi.salesSource.notFound';
    return apiErrorKey(error, ['FI.SALES_SOURCE.NOT_FOUND', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: SalesSourceQuery): Observable<PagedResult<SalesSourceSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.customerId) params = params.set('customerId', query.customerId);
    for (const key of ['sourceType', 'from', 'to', 'sort'] as const) {
      const value = query[key];
      if (value) params = params.set(key, value);
    }
    return this.read<PagedResult<SalesSourceSummary>>(this.http.get(API, { params, responseType: 'text' }));
  }
  get(id: number): Observable<SalesSourceDetail> {
    return this.read<SalesSourceDetail>(this.http.get(`${API}/${id}`, { responseType: 'text' }));
  }
  byKey(type: SalesSourceType, id: number): Observable<SalesSourceDetail> {
    return this.read<SalesSourceDetail>(this.http.get(`${API}/by-key/${type}/${id}`, { responseType: 'text' }));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(timeout(15000), jsonTextErrors(), map(body => parseJsonWithDecimalText<T>(body, DECIMALS)));
  }
}
