import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const TAX_CODE_API = '/api/v1/md/tax-codes';
export const TAX_RATE_API = '/api/v1/md/tax-rates';
export type TaxType = 'Output' | 'Input' | 'Both';
export type TaxDirection = 'Output' | 'Input';
/** currentRatePercent 為今日適用稅率（百分比），沒有適用期間時為 null（ADR-038）。 */
export interface TaxCode {
  id: number; code: string; name: string; taxType: TaxType; currentRatePercent: number | null;
  isActive: boolean; version: number;
}
export interface TaxCodeDraft { name: string; taxType: TaxType }
/** effectiveTo 為 null 表示持續有效；期間含首尾且同稅別不可重疊。 */
export interface TaxRate {
  id: number; taxCodeId: number; effectiveFrom: string; effectiveTo: string | null; ratePercent: number; version: number;
}
export interface TaxRateDraft { effectiveFrom: string; effectiveTo: string | null; ratePercent: number }
export interface TaxCalculationRequest {
  taxCode: string; direction: TaxDirection; date: string; amount: number; priceIncludesTax: boolean; currencyCode: string;
}
export interface TaxCalculation { ratePercent: number; net: number; tax: number; gross: number }
export interface ListQuery { page: number; pageSize: number; keyword?: string; sort?: string }

@Injectable({ providedIn: 'root' })
export class TaxService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.TAX_CODE.DUPLICATE_CODE', 'MD.TAX_CODE.NOT_FOUND', 'MD.TAX_CODE.IN_USE',
      'MD.TAX_RATE.OVERLAP', 'MD.TAX_RATE.NOT_FOUND', 'MD.TAX_RATE.TAX_CODE_INACTIVE', 'MD.TAX_RATE.DIRECTION_MISMATCH',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: ListQuery): Observable<PagedResult<TaxCode>> {
    return this.http.get<PagedResult<TaxCode>>(TAX_CODE_API, { params: this.params(query) });
  }
  create(body: TaxCodeDraft & { code: string }): Observable<TaxCode> {
    return this.http.post<TaxCode>(TAX_CODE_API, { code: body.code.trim().toUpperCase(), name: body.name.trim(), taxType: body.taxType });
  }
  update(id: number, body: TaxCodeDraft & { isActive: boolean; version: number }): Observable<TaxCode> {
    return this.http.put<TaxCode>(`${TAX_CODE_API}/${id}`,
      { name: body.name.trim(), taxType: body.taxType, isActive: body.isActive, version: body.version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${TAX_CODE_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  listRates(taxCodeId: number, query: ListQuery & { on?: string | null }): Observable<PagedResult<TaxRate>> {
    let params = this.params(query);
    if (query.on) params = params.set('on', query.on);
    return this.http.get<PagedResult<TaxRate>>(`${TAX_CODE_API}/${taxCodeId}/rates`, { params });
  }
  createRate(taxCodeId: number, body: TaxRateDraft): Observable<TaxRate> {
    return this.http.post<TaxRate>(`${TAX_CODE_API}/${taxCodeId}/rates`, body);
  }
  updateRate(taxCodeId: number, id: number, body: TaxRateDraft & { version: number }): Observable<TaxRate> {
    return this.http.put<TaxRate>(`${TAX_CODE_API}/${taxCodeId}/rates/${id}`, body);
  }
  removeRate(taxCodeId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${TAX_CODE_API}/${taxCodeId}/rates/${id}`, { params: new HttpParams().set('version', version) });
  }
  calculate(body: TaxCalculationRequest): Observable<TaxCalculation> {
    return this.http.post<TaxCalculation>(`${TAX_RATE_API}/calculate`,
      { ...body, taxCode: body.taxCode.trim().toUpperCase(), currencyCode: body.currencyCode.trim().toUpperCase() });
  }
  private params(query: ListQuery): HttpParams {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return params;
  }
}
