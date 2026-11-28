import { HttpClient, HttpErrorResponse, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout, TimeoutError } from 'rxjs';
import { apiErrorKey, apiErrorTraceId, apiValidationDetails, blobJsonErrors, jsonTextErrors, NETWORK_ERROR_KEY,
  RATE_LIMITED_ERROR_KEY } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { validIsoDate } from '../../shared/date';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/pur/purchase-receipts';
/** Quantities and costs stay as the exact server digits (never a JavaScript double). */
const DECIMAL_KEYS = ['totalQuantity', 'quantity', 'unitCost'] as const;
export const PURCHASE_RECEIPT_EXPORT_FILE = 'purchase-receipts.csv';
export const PURCHASE_RECEIPT_EXPORT_TIMEOUT_MS = 120_000;
/** Saved receipt states (BR-PUR-028): stock is posted only when a receipt becomes Posted. */
export const PURCHASE_RECEIPT_STATUSES = ['Draft', 'PendingApproval', 'Rejected', 'Posted'] as const;
export type PurchaseReceiptStatus = (typeof PURCHASE_RECEIPT_STATUSES)[number];
export const PURCHASE_RECEIPT_SORTS = ['-businessDate', 'businessDate', 'documentNo', '-documentNo'] as const;
export const PURCHASE_RECEIPT_ERROR_CODES = ['PUR.RECEIPT.NOT_FOUND', 'PUR.RECEIPT.EXPORT_LIMIT', 'COMMON.PERMISSION.DENIED'];

export interface PurchaseReceiptSummary { id: number; documentNo: string; businessDate: string; purchaseOrderId: number;
  purchaseOrderDocumentNo: string | null; supplierId: number; status: PurchaseReceiptStatus; currency: string; lineCount: number;
  totalQuantity: string; version: number }
export interface PurchaseReceiptTracking { seqNo: number; lotNo: string | null; serialNo: string | null; quantity: string;
  manufactureDate: string | null; expiryDate: string | null }
export interface PurchaseReceiptLine { id: number; lineNumber: number; purchaseOrderLineId: number; materialId: number;
  quantity: string; warehouseId: number; unitCost: string; tracking: PurchaseReceiptTracking[] }
export interface PurchaseReceiptDetail { id: number; documentNo: string; status: PurchaseReceiptStatus; version: number;
  purchaseOrderId: number; supplierId: number; currency: string; businessDate: string; lines: PurchaseReceiptLine[] }
export interface PurchaseReceiptFilter { keyword: string; from: string; to: string; status: string; purchaseOrderId: number | null }
export interface PurchaseReceiptQuery extends PurchaseReceiptFilter { page: number; pageSize: number; sort: string }
export type PurchaseReceiptFilterIssue = 'range' | 'date' | 'order';
export interface PurchaseReceiptExportFailure { key: string; traceId: string | null; details: string[] }

/** The first reason the typed filters cannot be sent; null when valid (mirrors the server's 400 rules). */
export function purchaseReceiptFilterIssue(filter: PurchaseReceiptFilter): PurchaseReceiptFilterIssue | null {
  if ((filter.from && !validIsoDate(filter.from)) || (filter.to && !validIsoDate(filter.to))) return 'date';
  if (filter.from && filter.to && filter.from > filter.to) return 'range';
  const order = filter.purchaseOrderId;
  if (order !== null && !(Number.isSafeInteger(order) && order > 0)) return 'order';
  return null;
}

/** 介面 4.4 export errors: permission, 10,000-line limit, invalid filters, timeout, network, rate limit; 5xx keeps trace ID. */
export function purchaseReceiptExportFailure(error: unknown): PurchaseReceiptExportFailure {
  const result = (key: string, details: string[] = []) => ({ key, traceId: apiErrorTraceId(error), details });
  if (error instanceof TimeoutError) return result('pur.receipt.exportTimeout');
  if (!(error instanceof HttpErrorResponse)) return result('pur.receipt.exportFailed');
  if (error.status === 0) return result(NETWORK_ERROR_KEY);
  if (error.status === 429) return result(RATE_LIMITED_ERROR_KEY);
  if (error.status === 403) return result('pur.receipt.exportDenied');
  const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
  if (error.status === 422 && code === 'PUR.RECEIPT.EXPORT_LIMIT') return result('errors.PUR.RECEIPT.EXPORT_LIMIT');
  if (error.status === 400) return result('pur.receipt.exportInvalid', apiValidationDetails(error));
  return result('pur.receipt.exportFailed');
}

function filterParams(params: HttpParams, filter: PurchaseReceiptFilter & { sort: string }): HttpParams {
  for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
    const value = filter[key].trim();
    if (value) params = params.set(key, value);
  }
  if (filter.purchaseOrderId) params = params.set('purchaseOrderId', filter.purchaseOrderId);
  return params;
}

/** Client for 介面 4.4 (WU-PUR-007e1): list, saved detail (also re-read for print) and server CSV export. */
@Injectable({ providedIn: 'root' })
export class PurchaseReceiptService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, PURCHASE_RECEIPT_ERROR_CODES); }
  list(query: PurchaseReceiptQuery): Observable<PagedResult<PurchaseReceiptSummary>> {
    const params = filterParams(new HttpParams().set('page', query.page).set('pageSize', query.pageSize), query);
    return this.read<PagedResult<PurchaseReceiptSummary>>(this.http.get(API, { params, responseType: 'text' }));
  }
  get(id: number): Observable<PurchaseReceiptDetail> {
    return this.read<PurchaseReceiptDetail>(this.http.get(`${API}/${id}`, { responseType: 'text' }));
  }
  /** GET /export with the applied filters and sort, never paging; headers follow the interceptor's Accept-Language. */
  export(filter: PurchaseReceiptFilter & { sort: string }): Observable<HttpResponse<Blob>> {
    return this.http.get(`${API}/export`, { params: filterParams(new HttpParams(), filter), observe: 'response', responseType: 'blob' })
      .pipe(blobJsonErrors(), timeout(PURCHASE_RECEIPT_EXPORT_TIMEOUT_MS));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(jsonTextErrors(), map(text => parseJsonWithDecimalText<T>(text, DECIMAL_KEYS)));
  }
}
