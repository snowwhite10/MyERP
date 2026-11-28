import { HttpClient, HttpErrorResponse, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout, TimeoutError } from 'rxjs';
import { apiErrorKey, apiErrorTraceId, apiValidationDetails, blobJsonErrors, jsonTextErrors, NETWORK_ERROR_KEY,
  RATE_LIMITED_ERROR_KEY } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { validIsoDate } from '../../shared/date';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/pur/purchase-orders';
/** Every decimal field stays as the exact server digits (never a JavaScript double). */
const DECIMAL_KEYS = ['baseGross', 'baseNet', 'baseTax', 'exchangeRate', 'quantity', 'unitPrice', 'taxRatePercent',
  'originalNet', 'originalTax', 'originalGross', 'requisitionQuantity', 'requisitionOrderedQuantity',
  'requisitionRemainingQuantity'] as const;
export const PURCHASE_ORDER_EXPORT_FILE = 'purchase-orders.csv';
export const PURCHASE_ORDER_EXPORT_TIMEOUT_MS = 120_000;
export const PURCHASE_ORDER_STATUSES = ['Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled'] as const;
export type PurchaseOrderStatus = (typeof PURCHASE_ORDER_STATUSES)[number];
export const PURCHASE_ORDER_SORTS = ['-businessDate', 'businessDate', 'documentNo', '-documentNo'] as const;
export type PurchaseOrderSort = (typeof PURCHASE_ORDER_SORTS)[number];
export const PURCHASE_ORDER_ERROR_CODES = ['PUR.ORDER.NOT_FOUND', 'PUR.ORDER.EXPORT_LIMIT', 'COMMON.PERMISSION.DENIED'];

export interface PurchaseOrderSummary { id: number; documentNo: string | null; businessDate: string | null; supplierId: number;
  status: PurchaseOrderStatus; currency: string; baseGross: string | null; lineCount: number; version: number }
export interface PurchaseOrderLine { id: number; lineNo: number; itemId: number; requisitionLineId: number | null;
  awardId: number | null; quantity: string; unitPrice: string; taxRateId: number | null; taxRatePercent: string;
  originalNet: string | null; originalTax: string | null; originalGross: string | null;
  baseNet: string | null; baseTax: string | null; baseGross: string | null;
  requisitionId: number | null; requisitionDocumentNo: string | null; requisitionLineNo: number | null;
  requisitionQuantity: string | null; requisitionOrderedQuantity: string | null; requisitionRemainingQuantity: string | null }
export interface PurchaseOrderApprovalHistory { instanceId: number; status: string; submittedAt: string;
  steps: { stepNo: number; name: string; status: string; actedBy: number | null; actedAt: string | null; comment: string | null }[] }
export interface PurchaseOrderDetail { id: number; documentNo: string | null; status: PurchaseOrderStatus; version: number;
  supplierId: number; rfqId: number | null; rfqDocumentNo: string | null; businessDate: string | null; currency: string;
  baseCurrencyCode: string | null; exchangeRate: string | null; baseNet: string | null; baseTax: string | null;
  baseGross: string | null; remark: string | null; lines: PurchaseOrderLine[]; approvalHistory: PurchaseOrderApprovalHistory[] }
export interface PurchaseOrderFilter { keyword: string; from: string; to: string; status: string;
  supplierId: number | null; requisitionLineId: number | null }
export interface PurchaseOrderQuery extends PurchaseOrderFilter { page: number; pageSize: number; sort: string }
export type PurchaseOrderFilterIssue = 'range' | 'date' | 'supplier' | 'source';
export interface PurchaseOrderExportFailure { key: string; traceId: string | null; details: string[] }

const positiveId = (value: number | null) => value === null || (Number.isSafeInteger(value) && value > 0);
/** The first reason the typed filters cannot be sent; null when valid (mirrors the server's 400 rules). */
export function purchaseOrderFilterIssue(filter: PurchaseOrderFilter): PurchaseOrderFilterIssue | null {
  if ((filter.from && !validIsoDate(filter.from)) || (filter.to && !validIsoDate(filter.to))) return 'date';
  if (filter.from && filter.to && filter.from > filter.to) return 'range';
  if (!positiveId(filter.supplierId)) return 'supplier';
  if (!positiveId(filter.requisitionLineId)) return 'source';
  return null;
}

/** 介面 4.1 export errors: permission, 10,000-line limit, invalid filters, timeout, network, rate limit; 5xx keeps trace ID. */
export function purchaseOrderExportFailure(error: unknown): PurchaseOrderExportFailure {
  const result = (key: string, details: string[] = []) => ({ key, traceId: apiErrorTraceId(error), details });
  if (error instanceof TimeoutError) return result('pur.order.exportTimeout');
  if (!(error instanceof HttpErrorResponse)) return result('pur.order.exportFailed');
  if (error.status === 0) return result(NETWORK_ERROR_KEY);
  if (error.status === 429) return result(RATE_LIMITED_ERROR_KEY);
  if (error.status === 403) return result('pur.order.exportDenied');
  const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
  if (error.status === 422 && code === 'PUR.ORDER.EXPORT_LIMIT') return result('errors.PUR.ORDER.EXPORT_LIMIT');
  if (error.status === 400) return result('pur.order.exportInvalid', apiValidationDetails(error));
  return result('pur.order.exportFailed');
}

function filterParams(params: HttpParams, filter: PurchaseOrderFilter & { sort: string }): HttpParams {
  for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
    const value = filter[key].trim();
    if (value) params = params.set(key, value);
  }
  if (filter.supplierId) params = params.set('supplierId', filter.supplierId);
  if (filter.requisitionLineId) params = params.set('requisitionLineId', filter.requisitionLineId);
  return params;
}

/** Read-only client for 介面 4.1 (WU-PUR-006e4): list, saved detail (also re-read for print) and CSV export. */
@Injectable({ providedIn: 'root' })
export class PurchaseOrderService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, PURCHASE_ORDER_ERROR_CODES); }
  list(query: PurchaseOrderQuery): Observable<PagedResult<PurchaseOrderSummary>> {
    const params = filterParams(new HttpParams().set('page', query.page).set('pageSize', query.pageSize), query);
    return this.read<PagedResult<PurchaseOrderSummary>>(this.http.get(API, { params, responseType: 'text' }));
  }
  get(id: number): Observable<PurchaseOrderDetail> {
    return this.read<PurchaseOrderDetail>(this.http.get(`${API}/${id}`, { responseType: 'text' }));
  }
  /** GET /export with the applied filters and sort, never paging; headers follow the interceptor's Accept-Language. */
  export(filter: PurchaseOrderFilter & { sort: string }): Observable<HttpResponse<Blob>> {
    return this.http.get(`${API}/export`, { params: filterParams(new HttpParams(), filter), observe: 'response', responseType: 'blob' })
      .pipe(blobJsonErrors(), timeout(PURCHASE_ORDER_EXPORT_TIMEOUT_MS));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(jsonTextErrors(), map(text => parseJsonWithDecimalText<T>(text, DECIMAL_KEYS)));
  }
}
