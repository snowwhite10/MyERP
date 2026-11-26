import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/pur/invoices';
/** Every decimal(19,4)/(19,6) field is kept as the exact server digits (never a JavaScript double). */
const DECIMAL_KEYS = ['totalNet', 'totalTax', 'totalGross', 'quantity', 'unitPrice', 'taxRatePercent', 'net', 'tax', 'gross',
  'netDifference', 'taxDifference', 'grossDifference'] as const;

export const PURCHASE_INVOICE_STATUSES = ['Draft', 'Matched', 'Disputed', 'Rejected', 'Posted'] as const;
export type PurchaseInvoiceStatus = (typeof PURCHASE_INVOICE_STATUSES)[number];
export const PURCHASE_INVOICE_SORTS = ['businessDate', 'supplierInvoiceNumber', 'totalGross'] as const;
export const PURCHASE_INVOICE_ERROR_CODES = ['PUR.INVOICE.NOT_FOUND', 'COMMON.PERMISSION.DENIED'] as const;

export interface PurchaseInvoiceSummary { id: number; supplierId: number; purchaseOrderId: number; supplierInvoiceNumber: string;
  businessDate: string; currency: string; status: PurchaseInvoiceStatus; totalNet: string; totalTax: string; totalGross: string;
  lineCount: number; version: number }
export interface PurchaseInvoiceLine { id: number; lineNo: number; receiptLineId: number; quantity: string; unitPrice: string;
  taxRatePercent: string; net: string; tax: string; gross: string }
/** One immutable RESOLVE decision; `invoiceVersion` is the invoice version that was resolved. */
export interface PurchaseInvoiceResolution { id: number; invoiceVersion: number; lineNo: number; action: 'Accept' | 'Reject' | string;
  reason: string; netDifference: string; taxDifference: string; grossDifference: string; decidedBy: number; decidedAt: string }
export interface PurchaseInvoiceDetail extends Omit<PurchaseInvoiceSummary, 'lineCount'> { createdAt: string; createdBy: number;
  updatedAt: string | null; lines: PurchaseInvoiceLine[]; resolutions: PurchaseInvoiceResolution[] }
export interface PurchaseInvoiceQuery { page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: string; supplierId?: number | null; purchaseOrderId?: number | null; sort?: string }

/** Client-side filter check mirroring the API's 400 rules, so an impossible query is never sent. */
export function purchaseInvoiceFilterIssue(query: Pick<PurchaseInvoiceQuery, 'from' | 'to' | 'supplierId' | 'purchaseOrderId'>):
  'range' | 'supplier' | 'order' | null {
  if (query.from && query.to && query.from > query.to) return 'range';
  const invalid = (id: number | null | undefined) => id !== null && id !== undefined && !(Number.isSafeInteger(id) && id > 0);
  if (invalid(query.supplierId)) return 'supplier';
  if (invalid(query.purchaseOrderId)) return 'order';
  return null;
}

/** Read-only client for 介面 4.19 (WU-PUR-008c1); create, revise, MATCH and RESOLVE belong to 008c2／008c3. */
@Injectable({ providedIn: 'root' })
export class PurchaseInvoiceService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, PURCHASE_INVOICE_ERROR_CODES); }
  list(query: PurchaseInvoiceQuery): Observable<PagedResult<PurchaseInvoiceSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    for (const key of ['supplierId', 'purchaseOrderId'] as const) {
      const value = query[key];
      if (value) params = params.set(key, value);
    }
    return this.read<PagedResult<PurchaseInvoiceSummary>>(this.http.get(API, { params, responseType: 'text' }));
  }
  get(id: number): Observable<PurchaseInvoiceDetail> {
    return this.read<PurchaseInvoiceDetail>(this.http.get(`${API}/${id}`, { responseType: 'text' }));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(jsonTextErrors(), map(text => parseJsonWithDecimalText<T>(text, DECIMAL_KEYS)));
  }
}
