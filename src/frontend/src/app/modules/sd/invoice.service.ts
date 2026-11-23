import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { jsonTextErrors } from '../../core/http/api-error';
import { formatDecimalText, parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/sal/invoices';
/** Money (decimal 19,4), quantities and rates (19,6) keep the server's exact digits as text; never a JavaScript double. */
const DECIMAL_KEYS = ['exchangeRate', 'taxRatePercent', 'quantity', 'originalNet', 'originalTax', 'originalGross',
  'baseNet', 'baseTax', 'baseGross'] as const;

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
}
