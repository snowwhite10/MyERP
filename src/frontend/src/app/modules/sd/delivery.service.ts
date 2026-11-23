import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { PagedResult } from '../../shared/list-page/table-query';
import { jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { DeliveryDraftForm, deliveryDraftJson, DeliverySourceOrder, DeliveryTrackingMode } from './delivery-draft-form';

const API = '/api/v1/sal/deliveries';
/** Quantity fields keep the server's exact digits as text (DECIMAL(19,6) can exceed a JavaScript number). */
const DECIMAL_KEYS = ['quantity', 'totalQuantity'] as const;
const ORDER_DECIMAL_KEYS = ['quantity', 'deliveredQuantity', 'pendingQuantity', 'remainingQuantity'] as const;

export type SalesDeliveryStatus = 'Draft' | 'Pending' | 'Rejected' | 'Posted';
export interface SalesDeliverySummary {
  id: number; documentNo: string; orderId: number; orderDocumentNo: string; customerId: number;
  customerName: string; businessDate: string; status: SalesDeliveryStatus; lineCount: number;
  totalQuantity: string; version: number;
}
export interface SalesDeliveryTracking { seqNo: number; lotNo: string | null; serialNo: string | null; quantity: string; }
export interface SalesDeliveryLine {
  id: number; lineNo: number; orderLineId: number; orderLineNo: number; itemId: number; itemCode: string;
  itemName: string; warehouseId: number; locationId: number | null; quantity: string;
  tracking: SalesDeliveryTracking[]; invMovementId: number | null;
  /** Saved warehouse/location labels from MD (ADR-153); null when MD no longer has the record (soft-deleted). */
  warehouseCode?: string | null; warehouseName?: string | null; warehouseActive?: boolean | null;
  locationCode?: string | null; locationName?: string | null; locationActive?: boolean | null;
  /** Current MD tracking mode of the item (ADR-151); null or absent when unknown. */
  trackingMode?: DeliveryTrackingMode | null;
}
/** "code · name" of a saved warehouse or location (ADR-153); empty when MD has no label, so callers show the deleted-ID fallback. */
export function referenceLabel(code?: string | null, name?: string | null): string {
  return code ? (name ? `${code} · ${name}` : code) : '';
}
export interface SalesDeliveryApprovalRound {
  instanceId: number; status: string; submittedAt: string;
  steps: { stepNo: number; name: string; status: string; actedBy: number | null; actedAt: string | null; comment: string | null }[];
}
export interface SalesDeliveryDetail {
  id: number; documentNo: string; orderId: number; orderDocumentNo: string; customerId: number;
  customerCode: string; customerName: string; businessDate: string; status: SalesDeliveryStatus;
  remark: string | null; approvalInstanceId: number | null; postedAt: string | null; version: number;
  lines: SalesDeliveryLine[]; approvalHistory?: SalesDeliveryApprovalRound[] | null;
}
/** One lot or serial with positive on-hand stock at the line's warehouse/location (ADR-152); quantity is exact text. */
export interface DeliveryTrackingOption { number: string; quantity: string; manufactureDate: string | null; expiryDate: string | null; }
export interface DeliveryTrackingOptions { trackingMode: DeliveryTrackingMode | null; items: DeliveryTrackingOption[]; truncated: boolean; }
export interface SalesDeliveryQuery {
  page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: SalesDeliveryStatus; orderId?: number; customerId?: number; sort?: string;
}

/** Paged picker query of the delivery form options (ADR-155). */
export interface DeliveryOptionQuery { page: number; pageSize: number; keyword?: string }
/** An Approved order a delivery can be created from (ADR-155); no amounts. */
export interface DeliveryOrderOption { id: number; documentNo: string; customerCode: string; customerName: string; businessDate: string; }
/** An active warehouse or location (ADR-155). */
export interface DeliveryReferenceOption { id: number; code: string; name: string; }

/** `page`, `pageSize` and the trimmed keyword (omitted when blank) of an MD option picker; reused by returns (ADR-197). */
export function optionParams(query: DeliveryOptionQuery): HttpParams {
  const params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
  const keyword = query.keyword?.trim();
  return keyword ? params.set('keyword', keyword) : params;
}

/** REQ_F4112.3.1: reads saved deliveries; the server applies the VIEW data scope through the source order. */
@Injectable({ providedIn: 'root' })
export class SalesDeliveryService {
  private readonly http = inject(HttpClient);
  list(query: SalesDeliveryQuery): Observable<PagedResult<SalesDeliverySummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    for (const key of ['orderId', 'customerId'] as const) {
      const value = query[key];
      if (value != null) params = params.set(key, value);
    }
    return this.http.get(API, { params, responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<PagedResult<SalesDeliverySummary>>(raw, DECIMAL_KEYS)), timeout(6000));
  }
  get(id: number): Observable<SalesDeliveryDetail> {
    if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Invalid delivery id');
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesDeliveryDetail>(raw, DECIMAL_KEYS)), timeout(6000));
  }
  /**
   * REQ_F4112.3.2: the source order's advisory remaining balance, read as exact text. ADR-155: needs SAL.DELIVERY.CREATE
   * or EDIT (not SAL.ORDER.VIEW); any status is returned so the page can say the order is no longer Approved.
   */
  sourceOrder(orderId: number): Observable<DeliverySourceOrder> {
    if (!Number.isSafeInteger(orderId) || orderId <= 0) throw new Error('Invalid order id');
    return this.http.get(`${API}/order-options/${orderId}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<DeliverySourceOrder>(raw, ORDER_DECIMAL_KEYS)), timeout(6000));
  }
  /** ADR-155: Approved orders in the SAL.DELIVERY.CREATE data scope (no SAL.ORDER.VIEW); keyword matches number or customer. */
  orderOptions(query: DeliveryOptionQuery): Observable<PagedResult<DeliveryOrderOption>> {
    return this.http.get<PagedResult<DeliveryOrderOption>>(`${API}/order-options`, { params: optionParams(query) }).pipe(timeout(6000));
  }
  /** ADR-155: active warehouses for SAL.DELIVERY.CREATE or EDIT (no MD.WAREHOUSE.VIEW). */
  warehouseOptions(query: DeliveryOptionQuery): Observable<PagedResult<DeliveryReferenceOption>> {
    return this.http.get<PagedResult<DeliveryReferenceOption>>(`${API}/warehouse-options`, { params: optionParams(query) }).pipe(timeout(6000));
  }
  /** ADR-155: active locations of one active warehouse; 404 MD.WAREHOUSE.NOT_FOUND when it was deactivated or deleted. */
  locationOptions(warehouseId: number, query: DeliveryOptionQuery): Observable<PagedResult<DeliveryReferenceOption>> {
    if (!Number.isSafeInteger(warehouseId) || warehouseId <= 0) throw new Error('Invalid warehouse id');
    return this.http.get<PagedResult<DeliveryReferenceOption>>(`${API}/warehouse-options/${warehouseId}/locations`,
      { params: optionParams(query) }).pipe(timeout(6000));
  }
  /**
   * ADR-152: lots/serials of the order line's item in stock at exactly this warehouse and location (null = none); needs
   * SAL.DELIVERY.CREATE or EDIT. On-hand only: other Drafts are not subtracted and posting rechecks availability.
   */
  trackingOptions(orderLineId: number, warehouseId: number, locationId: number | null, keyword?: string): Observable<DeliveryTrackingOptions> {
    if (!Number.isSafeInteger(orderLineId) || orderLineId <= 0 || !Number.isSafeInteger(warehouseId) || warehouseId <= 0 ||
      (locationId != null && (!Number.isSafeInteger(locationId) || locationId <= 0))) throw new Error('Invalid tracking option query');
    let params = new HttpParams().set('orderLineId', orderLineId).set('warehouseId', warehouseId);
    if (locationId != null) params = params.set('locationId', locationId);
    const text = keyword?.trim();
    if (text) params = params.set('keyword', text);
    return this.http.get(`${API}/tracking-options`, { params, responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<DeliveryTrackingOptions>(raw, ['quantity'])), timeout(6000));
  }
  /** REQ_F4112.3.2: POST a Draft; the 201 body is the saved Draft (mapped without VIEW, so a CREATE-only user gets it). */
  create(draft: DeliveryDraftForm): Observable<SalesDeliveryDetail> {
    return this.http.post(API, deliveryDraftJson(draft), { responseType: 'text',
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }) }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesDeliveryDetail>(raw, DECIMAL_KEYS)), timeout(6000));
  }
  /** REQ_F4112.3.3: replace every line of a current-version Draft/Rejected delivery; the 200 body is the saved Draft. */
  replace(id: number, version: number, draft: DeliveryDraftForm): Observable<SalesDeliveryDetail> {
    if (!Number.isSafeInteger(id) || id <= 0 || !Number.isSafeInteger(version) || version <= 0)
      throw new Error('Invalid delivery identity or version');
    return this.http.put(`${API}/${id}`, `{"version":${version},"draft":${deliveryDraftJson(draft)}}`, { responseType: 'text',
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }) }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesDeliveryDetail>(raw, DECIMAL_KEYS)), timeout(6000));
  }
  /** REQ_F4112.3.3: soft-delete a current-version Draft (204, no body). */
  remove(id: number, version: number): Observable<void> {
    if (!Number.isSafeInteger(id) || id <= 0 || !Number.isSafeInteger(version) || version <= 0)
      throw new Error('Invalid delivery identity or version');
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) }).pipe(timeout(6000));
  }
  /** REQ_F4112.3.4: send a current-version Draft to SYS approval; 200 is Pending, or Posted when no step applies (stock issued). */
  submit(id: number, version: number): Observable<SalesDeliveryDetail> { return this.command(id, version, 'submit'); }
  /** REQ_F4112.3.4: withdraw a current-version Pending delivery back to Draft (cancels the SYS round, releases the reservation). */
  withdraw(id: number, version: number): Observable<SalesDeliveryDetail> { return this.command(id, version, 'withdraw'); }
  private command(id: number, version: number, action: 'submit' | 'withdraw'): Observable<SalesDeliveryDetail> {
    if (!Number.isSafeInteger(id) || id <= 0 || !Number.isSafeInteger(version) || version <= 0)
      throw new Error('Invalid delivery identity or version');
    return this.http.post(`${API}/${id}/${action}`, `{"version":${version}}`, { responseType: 'text',
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }) }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesDeliveryDetail>(raw, DECIMAL_KEYS)), timeout(6000));
  }
}
