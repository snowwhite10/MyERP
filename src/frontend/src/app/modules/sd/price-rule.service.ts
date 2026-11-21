import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { jsonTextErrors } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';

const API = '/api/v1/sal/price-rules';
/** DECIMAL(19,6) price and DECIMAL(7,4) ceiling keep the server digits (never pass through a double). */
const DECIMAL_KEYS = ['unitPrice', 'discountCeiling'] as const;

/** Saved price rule (REQ_F4112.4.1). Labels are current MD values (ADR-161); null item label = item deleted in MD. */
export interface SalesPriceRule {
  id: number; itemId: number; customerId: number | null; currencyCode: string;
  validFrom: string; validTo: string; unitPrice: string; discountCeiling: string;
  isActive: boolean; version: number; createdAt: string; updatedAt: string | null;
  itemCode: string | null; itemName: string | null; customerCode: string | null; customerName: string | null;
}

export interface SalesPriceRuleQuery {
  page: number; pageSize: number; sort?: string; itemId?: number; customerId?: number;
  defaultOnly?: boolean; currencyCode?: string; asOf?: string; active?: boolean;
}

/** ADR-162: a choice offered to pricing users without MD view permission (active, current company). */
export interface PricingOption { id: number; code: string; name: string; }
/** Active currency with its amount decimal places (ADR-162). */
export interface PricingCurrencyOption { code: string; name: string; decimalPlaces: number; }
export interface PricingOptionQuery { page: number; pageSize: number; keyword?: string; }

export const PRICE_RULE_SORTS = ['validFrom', 'id'] as const;
export const PRICE_RULE_DEFAULT_SORT = '-validFrom';

@Injectable({ providedIn: 'root' })
export class SalesPriceRuleService {
  private readonly http = inject(HttpClient);

  list(query: SalesPriceRuleQuery): Observable<PagedResult<SalesPriceRule>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.sort) params = params.set('sort', query.sort);
    if (query.itemId != null) params = params.set('itemId', query.itemId);
    if (query.customerId != null) params = params.set('customerId', query.customerId);
    else if (query.defaultOnly) params = params.set('defaultOnly', true);
    const currency = query.currencyCode?.trim();
    if (currency) params = params.set('currencyCode', currency);
    if (query.asOf) params = params.set('asOf', query.asOf);
    if (query.active != null) params = params.set('active', query.active);
    return this.http.get(API, { params, responseType: 'text' }).pipe(jsonTextErrors(),
      map(text => parseJsonWithDecimalText<PagedResult<SalesPriceRule>>(text, DECIMAL_KEYS)), timeout(6000));
  }

  /** Active items a rule can name (ADR-162); needs SAL.PRICING.VIEW/CREATE/EDIT, not MD.ITEM.VIEW. */
  itemOptions(query: PricingOptionQuery): Observable<PagedResult<PricingOption>> {
    return this.http.get<PagedResult<PricingOption>>(`${API}/item-options`, { params: optionParams(query) })
      .pipe(timeout(6000));
  }

  /** Active, approved customers a customer rule can name (ADR-162); not MD.CUSTOMER.VIEW. */
  customerOptions(query: PricingOptionQuery): Observable<PagedResult<PricingOption>> {
    return this.http.get<PagedResult<PricingOption>>(`${API}/customer-options`, { params: optionParams(query) })
      .pipe(timeout(6000));
  }

  /** Active currencies of the current company in code order (ADR-162). */
  currencyOptions(): Observable<PricingCurrencyOption[]> {
    return this.http.get<PricingCurrencyOption[]>(`${API}/currency-options`).pipe(timeout(6000));
  }

  get(id: number): Observable<SalesPriceRule> {
    requireId(id);
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(text => parseJsonWithDecimalText<SalesPriceRule>(text, DECIMAL_KEYS)), timeout(6000));
  }

  /** ADR-158: retire with the version last read; 409 stale/lock timeout, 422 already inactive. */
  deactivate(id: number, version: number): Observable<SalesPriceRule> {
    requireId(id);
    if (!Number.isSafeInteger(version) || version <= 0) throw new Error('Invalid price rule version');
    return this.http.post(`${API}/${id}/deactivate`, `{"version":${version}}`, { responseType: 'text',
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }) }).pipe(jsonTextErrors(),
      map(text => parseJsonWithDecimalText<SalesPriceRule>(text, DECIMAL_KEYS)), timeout(6000));
  }
}

function optionParams(query: PricingOptionQuery): HttpParams {
  const params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
  const keyword = query.keyword?.trim();
  return keyword ? params.set('keyword', keyword) : params;
}

function requireId(id: number): void {
  if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Invalid price rule id');
}

/** Outcome keys of a failed deactivate; null for errors the page shows generically (network, 5xx, 403, timeout). */
export function priceRuleDeactivateFailureKey(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
  if (error.status === 404) return 'sd.priceRule.deactivateUnavailable';
  if (error.status === 422 && code === 'SAL.PRICING.RULE_INACTIVE') return 'sd.priceRule.deactivateAlreadyInactive';
  if (error.status === 409 && code === 'SAL.PRICING.RULE_LOCK_TIMEOUT') return 'sd.priceRule.deactivateBusy';
  if (error.status === 409) return 'sd.priceRule.deactivateConflict';
  return null;
}
