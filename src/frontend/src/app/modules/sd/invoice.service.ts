import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { jsonTextErrors } from '../../core/http/api-error';
import { formatDecimalText, parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/sal/invoices';
/** Money (decimal 19,4), quantities and rates (19,6) keep the server's exact digits as text; never a JavaScript double. */
const DECIMAL_KEYS = ['exchangeRate', 'taxRatePercent', 'quantity', 'originalNet', 'originalTax', 'originalGross',
  'baseNet', 'baseTax', 'baseGross'] as const;
const SOURCE_DECIMAL_KEYS = ['exchangeRate', 'taxRatePercent', 'postedQuantity', 'invoicedQuantity',
  'remainingQuantity'] as const;

/** Only Posted invoices are readable; Issuing exists inside the ISSUE transaction only (ADR-179). */
export type SalesInvoiceStatus = 'Issuing' | 'Posted';
export interface SalesInvoiceSummary {
  id: number; documentNo: string; orderId: number; orderDocumentNo: string; customerId: number; customerName: string;
  businessDate: string; currencyCode: string; baseCurrencyCode: string; originalGross: string; baseGross: string;
  postedAt: string | null; fiJournalId: number | null; lineCount: number; version: number;
}
export interface SalesInvoiceLine {
  id: number; lineNo: number; deliveryId: number; deliveryDocumentNo: string; deliveryLineId: number; orderLineId: number;
  orderLineNo: number; itemId: number; itemCode: string; itemName: string; taxRateId: number; taxRatePercent: string;
  quantity: string; originalNet: string; originalTax: string; originalGross: string; baseNet: string; baseTax: string;
  baseGross: string;
}
export interface SalesInvoiceDetail {
  id: number; documentNo: string; requestKey: string; orderId: number; orderDocumentNo: string; customerId: number;
  customerCode: string; customerName: string; businessDate: string; currencyCode: string; baseCurrencyCode: string;
  exchangeRate: string; status: SalesInvoiceStatus; postedAt: string | null; fiSalesSourceId: number | null;
  fiReceivableId: number | null; fiJournalId: number | null; originalNet: string; originalTax: string;
  originalGross: string; baseNet: string; baseTax: string; baseGross: string; version: number;
  lines: SalesInvoiceLine[];
}
export interface SalesInvoiceQuery {
  page: number; pageSize: number; keyword?: string; from?: string; to?: string; orderId?: number; customerId?: number;
  sort?: string;
}

/** An Approved order with open Posted delivery lines, in the SAL.INVOICE.ISSUE scope (ADR-181). */
export interface SalesInvoiceSourceOrderSummary {
  id: number; documentNo: string; customerId: number; customerCode: string; customerName: string; businessDate: string;
  currencyCode: string; openLineCount: number; version: number;
}
/** One open Posted delivery line; `remainingQuantity` is advisory, ISSUE rechecks it under lock. */
export interface SalesInvoiceSourceLine {
  deliveryId: number; deliveryDocumentNo: string; deliveryDate: string; deliveryLineId: number; deliveryLineNo: number;
  orderLineId: number; orderLineNo: number; itemId: number; itemCode: string; itemName: string; taxRatePercent: string;
  postedQuantity: string; invoicedQuantity: string; remainingQuantity: string;
}
export interface SalesInvoiceSourceOrder {
  id: number; documentNo: string; customerId: number; customerCode: string; customerName: string; businessDate: string;
  currencyCode: string; baseCurrencyCode: string; exchangeRate: string; version: number; lines: SalesInvoiceSourceLine[];
}
export interface SalesInvoiceSourceQuery { page: number; pageSize: number; keyword?: string; customerId?: number; sort?: string; }
/** ISSUE body (ADR-177): no amount, tax, customer, currency, state or FI field; quantities stay decimal text. */
export interface SalesInvoiceIssueRequest {
  requestKey: string; businessDate: string; lines: { lineNo: number; deliveryLineId: number; quantity: string }[];
}
/** `replayed` is true for 200: the same request key and content was already issued; nothing was posted again. */
export interface SalesInvoiceIssueResult { invoice: SalesInvoiceDetail; replayed: boolean; }

/** ISSUE request keys: 1–64 letters, digits, `-` or `_` (server rule); a UUID fits. */
export function newInvoiceRequestKey(): string { return crypto.randomUUID(); }

/** JSON with every quantity as an exact number literal (never through a JavaScript double). */
export function invoiceIssueJson(request: SalesInvoiceIssueRequest): string {
  const lines = request.lines.map(line => {
    if (!/^\d+(\.\d+)?$/.test(line.quantity)) throw new Error('Invalid quantity text');
    return `{"lineNo":${line.lineNo},"deliveryLineId":${line.deliveryLineId},"quantity":${line.quantity}}`;
  });
  return `{"requestKey":${JSON.stringify(request.requestKey)},"businessDate":${JSON.stringify(request.businessDate)},` +
    `"lines":[${lines.join(',')}]}`;
}

/** Saved money: digit grouping, at least two decimals, every saved digit kept (no rounding). */
export function invoiceMoney(value: string): string {
  const text = formatDecimalText(String(value));
  if (!/^-?[\d,]+(\.\d+)?$/.test(text)) return text;
  const [integer, fraction = ''] = text.split('.');
  return `${integer}.${fraction.padEnd(2, '0')}`;
}

/** REQ_F4112.5.1 (WU-SD-007b1): reads Posted invoices; the server applies the SAL.INVOICE.VIEW scope of the source order. */
@Injectable({ providedIn: 'root' })
export class SalesInvoiceService {
  private readonly http = inject(HttpClient);
  list(query: SalesInvoiceQuery): Observable<PagedResult<SalesInvoiceSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    for (const key of ['orderId', 'customerId'] as const) {
      const value = query[key];
      if (value != null) params = params.set(key, value);
    }
    return this.http.get(API, { params, responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<PagedResult<SalesInvoiceSummary>>(raw, DECIMAL_KEYS)), timeout(6000));
  }
  get(id: number): Observable<SalesInvoiceDetail> {
    if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Invalid invoice id');
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesInvoiceDetail>(raw, DECIMAL_KEYS)), timeout(6000));
  }
  /** ADR-181: Approved orders with open Posted delivery lines (SAL.INVOICE.ISSUE scope); for the order picker. */
  sourceOrders(query: SalesInvoiceSourceQuery): Observable<PagedResult<SalesInvoiceSourceOrderSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const keyword = query.keyword?.trim(), sort = query.sort?.trim();
    if (keyword) params = params.set('keyword', keyword);
    if (sort) params = params.set('sort', sort);
    if (query.customerId != null) params = params.set('customerId', query.customerId);
    return this.http.get<PagedResult<SalesInvoiceSourceOrderSummary>>(`${API}/source-orders`, { params }).pipe(timeout(6000));
  }
  /** ADR-181: the order header the invoice copies and its open lines with posted / invoiced / remaining quantities. */
  sourceOrder(orderId: number): Observable<SalesInvoiceSourceOrder> {
    if (!Number.isSafeInteger(orderId) || orderId <= 0) throw new Error('Invalid order id');
    return this.http.get(`${API}/source-orders/${orderId}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesInvoiceSourceOrder>(raw, SOURCE_DECIMAL_KEYS)), timeout(6000));
  }
  /**
   * ADR-179: issue and post in one transaction. 201 = new invoice, 200 = identical replay of the same request key
   * (nothing posted again), so resending the same body after a timeout is safe. Allows 20 s: the post includes FI/GL.
   */
  issue(request: SalesInvoiceIssueRequest): Observable<SalesInvoiceIssueResult> {
    return this.http.post(API, invoiceIssueJson(request), { observe: 'response', responseType: 'text',
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }) }).pipe(jsonTextErrors(),
      map(response => ({ invoice: parseJsonWithDecimalText<SalesInvoiceDetail>(response.body ?? '', DECIMAL_KEYS),
        replayed: response.status === 200 })), timeout(20000));
  }
}
