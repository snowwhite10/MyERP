import { HttpClient, HttpErrorResponse, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, map, Observable, throwError, timeout, TimeoutError } from 'rxjs';
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
export const PURCHASE_ORDER_ERROR_CODES = ['PUR.ORDER.NOT_FOUND', 'PUR.ORDER.EXPORT_LIMIT', 'COMMON.PERMISSION.DENIED',
  'COMMON.CONCURRENCY.CONFLICT', 'PUR.ORDER.NOT_SUBMITTABLE', 'PUR.ORDER.NOT_DELETABLE', 'PUR.ORDER.NOT_CANCELLABLE',
  'PUR.ORDER.HAS_RECEIPTS', 'PUR.ORDER.EMPTY', 'PUR.ORDER.INVALID_REQUISITION_SOURCE', 'PUR.ORDER.REQUISITION_INSUFFICIENT',
  'MD.SUPPLIER.NOT_FOUND', 'MD.SUPPLIER.NOT_APPROVED', 'MD.ITEM.NOT_FOUND', 'MD.ITEM.INACTIVE'];
/** Errors meaning the saved order changed underneath the screen: show the reason, then re-read the saved detail. */
const RELOAD_DETAIL_CODES = ['COMMON.CONCURRENCY.CONFLICT', 'PUR.ORDER.NOT_SUBMITTABLE', 'PUR.ORDER.NOT_DELETABLE',
  'PUR.ORDER.NOT_CANCELLABLE', 'PUR.ORDER.HAS_RECEIPTS', 'PUR.ORDER.EMPTY'];
/** Server limit for the CANCEL reason (BR-PUR-027, WU-PUR-006c2). */
export const PURCHASE_ORDER_REASON_MAX = 500;

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
/** SUBMIT / CANCEL reply: the saved status and version (SUBMIT also carries the SYS approval state). */
export interface PurchaseOrderActionResult { id: number; documentNo: string | null; status: PurchaseOrderStatus; version: number;
  approval?: { status: string } | null }
/** One requisition line that lacked approved quantity at SUBMIT; quantities keep the server's exact digits. */
export interface PurchaseOrderShortage { requisitionLineId: number; orderLineNos: number[]; requested: string; available: string }
/**
 * A refused lifecycle action: message key, 5xx trace ID, per-line reasons (shortages / invalid source lines / field errors)
 * and what the screen must re-read — the saved detail when the order changed, the list when it is gone.
 */
export interface PurchaseOrderActionFailure { key: string; traceId: string | null; shortages: PurchaseOrderShortage[];
  lineNos: number[]; fields: string[]; reload: 'none' | 'detail' | 'list' }
export type PurchaseOrderReasonIssue = 'required' | 'length';

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

const lineNo = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) > 0;
const decimalText = (value: unknown): value is string => typeof value === 'string' && /^-?\d+(\.\d+)?$/.test(value);
function shortageOf(value: unknown): PurchaseOrderShortage | null {
  const s = value as Partial<PurchaseOrderShortage> | null;
  if (!s || typeof s !== 'object' || !lineNo(s.requisitionLineId) || !Array.isArray(s.orderLineNos) || !s.orderLineNos.every(lineNo)
    || !decimalText(s.requested) || !decimalText(s.available)) return null;
  return { requisitionLineId: s.requisitionLineId, orderLineNos: [...s.orderLineNos], requested: s.requested, available: s.available };
}

/** 介面 4.1 SUBMIT / DELETE / CANCEL refusal (REQ_IO_A4.1.4): reason, per-line details and the re-read the screen needs. */
export function purchaseOrderActionFailure(error: unknown): PurchaseOrderActionFailure {
  const body = (error instanceof HttpErrorResponse ? error.error : null) as
    { errorCode?: unknown; shortages?: unknown; lineNos?: unknown } | null;
  const code = body?.errorCode;
  const shortages = Array.isArray(body?.shortages) ? body.shortages.map(shortageOf) : [];
  const lineNos = Array.isArray(body?.lineNos) ? body.lineNos : [];
  const reload = error instanceof HttpErrorResponse && error.status === 404 ? 'list'
    : RELOAD_DETAIL_CODES.includes(code as string) ? 'detail' : 'none';
  return { key: apiErrorKey(error, PURCHASE_ORDER_ERROR_CODES), traceId: apiErrorTraceId(error),
    shortages: shortages.every(x => x !== null) ? shortages : [],
    lineNos: lineNos.every(lineNo) ? [...lineNos].sort((a, b) => a - b) : [], fields: apiValidationDetails(error), reload };
}

/** Mirrors the server's CANCEL reason rule: 1–500 characters after trimming. */
export function purchaseOrderCancelReasonIssue(reason: string): PurchaseOrderReasonIssue | null {
  const trimmed = reason.trim();
  return !trimmed ? 'required' : trimmed.length > PURCHASE_ORDER_REASON_MAX ? 'length' : null;
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

/** Client for 介面 4.1: list, saved detail (also re-read for print), CSV export (006e4) and SUBMIT / DELETE / CANCEL (006f1). */
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
  /** POST /{id}/submit with the saved version; the whole order is consumed or nothing changes. */
  submit(id: number, version: number): Observable<PurchaseOrderActionResult> {
    return this.act(this.http.post(`${API}/${id}/submit`, { version }, { responseType: 'text' }));
  }
  /** DELETE /{id}?version= soft-deletes an unconsumed Draft / Rejected order (204). */
  remove(id: number, version: number): Observable<void> {
    return this.http.delete(`${API}/${id}`, { params: new HttpParams().set('version', version), responseType: 'text' })
      .pipe(jsonTextErrors(), map(() => undefined));
  }
  /** POST /{id}/cancel with the saved version and the trimmed reason; releases the requisition quantity once. */
  cancel(id: number, version: number, reason: string): Observable<PurchaseOrderActionResult> {
    return this.act(this.http.post(`${API}/${id}/cancel`, { version, reason: reason.trim() }, { responseType: 'text' }));
  }
  /** Error bodies are parsed with shortage quantities kept as exact decimal text. */
  private act(request: Observable<string>): Observable<PurchaseOrderActionResult> {
    return request.pipe(catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || typeof error.error !== 'string') return throwError(() => error);
      let parsed: unknown = null;
      try { parsed = parseJsonWithDecimalText(error.error, ['requested', 'available']); } catch { parsed = null; }
      return throwError(() => new HttpErrorResponse({ error: parsed, headers: error.headers, status: error.status,
        statusText: error.statusText, url: error.url ?? undefined }));
    }), map(text => JSON.parse(text) as PurchaseOrderActionResult));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(jsonTextErrors(), map(text => parseJsonWithDecimalText<T>(text, DECIMAL_KEYS)));
  }
}
