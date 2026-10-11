import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const CURRENCY_API = '/api/v1/md/currencies';
export interface Currency {
  id: number; code: string; name: string; symbol: string | null; decimalPlaces: number; isBase: boolean;
  isActive: boolean; version: number;
}
export interface CurrencyDraft { name: string; symbol: string | null; decimalPlaces: number; isBase: boolean }
/** 1 外幣 = rate 本位幣；effectiveTo 為 null 表示持續有效（ADR-037）。 */
export interface ExchangeRate {
  id: number; currencyId: number; effectiveFrom: string; effectiveTo: string | null; rate: number; version: number;
}
export interface ExchangeRateDraft { effectiveFrom: string; effectiveTo: string | null; rate: number }
export interface ListQuery { page: number; pageSize: number; keyword?: string; sort?: string }

@Injectable({ providedIn: 'root' })
export class CurrencyService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.CURRENCY.DUPLICATE_CODE', 'MD.CURRENCY.NOT_FOUND', 'MD.CURRENCY.BASE_LOCKED',
      'MD.CURRENCY.BASE_REQUIRED', 'MD.CURRENCY.BASE_INACTIVE', 'MD.CURRENCY.IS_BASE', 'MD.CURRENCY.IN_USE',
      'MD.EXCHANGE_RATE.OVERLAP', 'MD.EXCHANGE_RATE.NOT_FOUND', 'MD.EXCHANGE_RATE.BASE_CURRENCY',
      'MD.EXCHANGE_RATE.NO_BASE_CURRENCY', 'MD.EXCHANGE_RATE.CURRENCY_INACTIVE',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: ListQuery): Observable<PagedResult<Currency>> {
    return this.http.get<PagedResult<Currency>>(CURRENCY_API, { params: this.params(query) });
  }
  create(body: CurrencyDraft & { code: string }): Observable<Currency> {
    return this.http.post<Currency>(CURRENCY_API, { ...this.clean(body), code: body.code.trim().toUpperCase() });
  }
  update(id: number, body: CurrencyDraft & { isActive: boolean; version: number }): Observable<Currency> {
    return this.http.put<Currency>(`${CURRENCY_API}/${id}`, { ...this.clean(body), isActive: body.isActive, version: body.version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${CURRENCY_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  listRates(currencyId: number, query: ListQuery & { on?: string | null }): Observable<PagedResult<ExchangeRate>> {
    let params = this.params(query);
    if (query.on) params = params.set('on', query.on);
    return this.http.get<PagedResult<ExchangeRate>>(`${CURRENCY_API}/${currencyId}/rates`, { params });
  }
  createRate(currencyId: number, body: ExchangeRateDraft): Observable<ExchangeRate> {
    return this.http.post<ExchangeRate>(`${CURRENCY_API}/${currencyId}/rates`, body);
  }
  updateRate(currencyId: number, id: number, body: ExchangeRateDraft & { version: number }): Observable<ExchangeRate> {
    return this.http.put<ExchangeRate>(`${CURRENCY_API}/${currencyId}/rates/${id}`, body);
  }
  removeRate(currencyId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${CURRENCY_API}/${currencyId}/rates/${id}`, { params: new HttpParams().set('version', version) });
  }
  private params(query: ListQuery): HttpParams {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return params;
  }
  private clean(body: CurrencyDraft): CurrencyDraft {
    return { name: body.name.trim(), symbol: body.symbol?.trim() || null, decimalPlaces: body.decimalPlaces, isBase: body.isBase };
  }
}
