import { HttpClient, HttpHeaders, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { PagedResult } from '../../shared/list-page/table-query';
import { blobJsonErrors, jsonTextErrors } from '../../core/http/api-error';
import { OrderDraftForm, orderDraftJson } from './order-draft-form';
import { SalesLinePricingMode } from './quotation.service';

const API = '/api/v1/sal/orders';
export type SalesOrderStatus = 'Draft' | 'Pending' | 'Rejected' | 'Approved' | 'Cancelled';
export interface SalesOrderSummary {
  id: number; documentNo: string; customerId: number; customerName: string;
  businessDate: string; status: SalesOrderStatus; originalGross: number; version: number;
}
export interface SalesOrderLine {
  id: number; lineNo: number; itemId: number; itemCode: string; itemName: string;
  quantity: number; taxRateId: number; deliveredQuantity: number; pendingQuantity: number;
  remainingQuantity: number; unitPrice: number; discountRate: number; taxRatePercent: number;
  originalNet: number; originalTax: number; originalGross: number;
  /** ADR-160 saved provenance (converted lines copy the quotation's); absent from older mocks = Legacy. */
  pricingMode?: SalesLinePricingMode; pricingRuleId?: number | null; pricingRuleVersion?: number | null;
  ruleUnitPrice?: number | string | null; ruleDiscountCeiling?: number | string | null;
  overrideReason?: string | null; overrideBy?: number | null; overrideAt?: string | null;
}
export interface SalesOrderEditableDetail extends Omit<SalesOrderDetail, 'lines'> {
  lines: (Omit<SalesOrderLine, 'quantity' | 'unitPrice' | 'discountRate' | 'ruleUnitPrice' | 'ruleDiscountCeiling'> & {
    quantity: string; unitPrice: string; discountRate: string;
    ruleUnitPrice?: string | null; ruleDiscountCeiling?: string | null;
  })[];
}
export interface SalesOrderDetail extends SalesOrderSummary {
  customerCode: string; currencyCode: string; baseCurrencyCode: string;
  exchangeRate: number; sourceQuotationId: number | null; originalNet: number;
  originalTax: number; baseNet: number; baseTax: number; baseGross: number;
  remark: string | null; lines: SalesOrderLine[];
  approvalInstanceId: number | null;
  approvalHistory?: { instanceId: number; status: string; submittedAt: string;
    steps: { stepNo: number; name: string; status: string; actedBy: number | null;
      actedAt: string | null; comment: string | null }[] }[];
}
export interface SalesOrderQuery {
  page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: SalesOrderStatus; customerId?: number; sort?: string;
}
export type SalesOrderExportQuery = Omit<Partial<SalesOrderQuery>, 'page' | 'pageSize'>;
/** 匯出最多 10,000 行，伺服器一次讀出；給較讀取更長的等待時間。 */
export const ORDER_EXPORT_TIMEOUT_MS = 15000;

@Injectable({ providedIn: 'root' })
export class SalesOrderService {
  private readonly http = inject(HttpClient);
  list(query: SalesOrderQuery): Observable<PagedResult<SalesOrderSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.customerId != null) params = params.set('customerId', query.customerId);
    return this.http.get<PagedResult<SalesOrderSummary>>(API, { params }).pipe(timeout(6000));
  }
  /** ADR-141：送目前套用的篩選與排序（不分頁）；語言由 API 攔截器的 Accept-Language 決定。 */
  export(query: SalesOrderExportQuery): Observable<HttpResponse<Blob>> {
    let params = new HttpParams();
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.customerId != null) params = params.set('customerId', query.customerId);
    return this.http.get(`${API}/export`, { params, observe: 'response', responseType: 'blob' })
      .pipe(blobJsonErrors(), timeout(ORDER_EXPORT_TIMEOUT_MS));
  }
  get(id: number): Observable<SalesOrderDetail> {
    return this.http.get<SalesOrderDetail>(`${API}/${id}`).pipe(timeout(6000));
  }
  /** Keep editable DECIMAL(19,6/4) tokens intact before JSON.parse converts them to binary numbers. */
  getEditable(id: number): Observable<SalesOrderEditableDetail> {
    if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Invalid order id');
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(jsonTextErrors(), map(raw => {
      const exact = raw.replace(/("(?:quantity|unitPrice|discountRate|ruleUnitPrice|ruleDiscountCeiling)"\s*:\s*)(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/g,
        '$1"$2"');
      return JSON.parse(exact) as SalesOrderEditableDetail;
    }), timeout(6000));
  }
  /** Receipt read is limited by the server to this caller's own CREATE-scoped order. */
  getCreated(id: number): Observable<SalesOrderDetail> {
    if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Invalid order id');
    return this.http.get<SalesOrderDetail>(`${API}/${id}/created`).pipe(timeout(6000));
  }
  create(draft: OrderDraftForm): Observable<SalesOrderDetail> {
    return this.http.post<SalesOrderDetail>(API, orderDraftJson(draft),
      { headers: new HttpHeaders({ 'Content-Type': 'application/json' }) }).pipe(timeout(6000));
  }
  replace(id: number, version: number, draft: OrderDraftForm): Observable<SalesOrderDetail> {
    if (!Number.isSafeInteger(id) || id <= 0 || !Number.isSafeInteger(version) || version <= 0)
      throw new Error('Invalid order identity or version');
    return this.http.put<SalesOrderDetail>(`${API}/${id}`,
      `{"version":${version},"draft":${orderDraftJson(draft)}}`,
      { headers: new HttpHeaders({ 'Content-Type': 'application/json' }) }).pipe(timeout(6000));
  }
  convertQuotation(quotationId: number): Observable<SalesOrderDetail> {
    if (!Number.isSafeInteger(quotationId) || quotationId <= 0) throw new Error('Invalid quotation id');
    return this.http.post<SalesOrderDetail>(`${API}/from-quotation/${quotationId}`, null).pipe(timeout(6000));
  }
}
