import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/sal/returns';
/** Money (decimal 19,4), quantities and rates (19,6) keep the server's exact digits as text; never a JavaScript double. */
const DECIMAL_KEYS = ['exchangeRate', 'quantity', 'unbilledQuantity', 'billedQuantity', 'originalNet', 'originalTax',
  'originalGross', 'baseNet', 'baseTax', 'baseGross'] as const;
const SOURCE_DECIMAL_KEYS = ['exchangeRate', 'postedQuantity', 'returnedQuantity', 'returnableQuantity',
  'invoicedQuantity', 'unbilledRemainingQuantity', 'quantity', 'creditedQuantity', 'remainingQuantity'] as const;
const QUANTITY_TEXT = /^\d+(\.\d+)?$/;

/** Draft and Rejected are editable; Pending waits for SYS approval; Posted has received the goods (ADR-184). */
export type SalesReturnStatus = 'Draft' | 'Pending' | 'Rejected' | 'Posted';
export interface SalesReturnSummary {
  id: number; documentNo: string; orderId: number; orderDocumentNo: string; customerId: number; customerName: string;
  businessDate: string; currencyCode: string; baseCurrencyCode: string; status: SalesReturnStatus; originalGross: string;
  baseGross: string; postedAt: string | null; lineCount: number; version: number;
}
/** Billed portion credited against one Posted invoice line (snapshot amounts). */
export interface SalesReturnAllocation {
  id: number; invoiceId: number; invoiceDocumentNo: string; invoiceLineId: number; quantity: string; originalNet: string;
  originalTax: string; originalGross: string; baseNet: string; baseTax: string; baseGross: string;
}
export interface SalesReturnLine {
  id: number; lineNo: number; deliveryId: number; deliveryDocumentNo: string; deliveryLineId: number; orderLineId: number;
  orderLineNo: number; itemId: number; itemCode: string; itemName: string; warehouseId: number; locationId: number | null;
  quantity: string; unbilledQuantity: string; billedQuantity: string; originalOutboundMovementId: number;
  invMovementId: number | null; originalNet: string; originalTax: string; originalGross: string; baseNet: string;
  baseTax: string; baseGross: string; allocations: SalesReturnAllocation[];
  /** MD destination labels (WU-SD-008b2); null when the warehouse / location was soft-deleted. */
  warehouseCode?: string | null; warehouseName?: string | null; warehouseActive?: boolean | null;
  locationCode?: string | null; locationName?: string | null; locationActive?: boolean | null;
}
export interface SalesReturnDetail {
  id: number; documentNo: string; requestKey: string; orderId: number; orderDocumentNo: string; customerId: number;
  customerCode: string; customerName: string; businessDate: string; currencyCode: string; baseCurrencyCode: string;
  exchangeRate: string; status: SalesReturnStatus; remark: string | null; approvalInstanceId: number | null;
  postedAt: string | null; fiCreditNoteId: number | null; originalNet: string; originalTax: string;
  originalGross: string; baseNet: string; baseTax: string; baseGross: string; version: number; lines: SalesReturnLine[];
}
export interface SalesReturnQuery {
  page: number; pageSize: number; keyword?: string; from?: string; to?: string; orderId?: number; customerId?: number;
  status?: SalesReturnStatus; sort?: string;
}

/** An Approved order with returnable, untracked Posted delivery lines in the SAL.RETURN.CREATE scope (ADR-193). */
export interface SalesReturnSourceOrderSummary {
  id: number; documentNo: string; customerId: number; customerCode: string; customerName: string; businessDate: string;
  currencyCode: string; openLineCount: number; version: number;
}
/** A Posted invoice line the return may credit; `remainingQuantity` excludes Posted returns only. */
export interface SalesReturnSourceInvoiceLine {
  invoiceLineId: number; invoiceId: number; invoiceDocumentNo: string; invoiceDate: string; quantity: string;
  creditedQuantity: string; remainingQuantity: string;
}
/** One returnable delivery line; balances are advisory, CREATE rechecks them under lock (BR-SD-044). */
export interface SalesReturnSourceLine {
  deliveryId: number; deliveryDocumentNo: string; deliveryDate: string; deliveryLineId: number; deliveryLineNo: number;
  orderLineId: number; orderLineNo: number; itemId: number; itemCode: string; itemName: string; warehouseId: number;
  locationId: number | null; trackingUnsupported: boolean; postedQuantity: string; returnedQuantity: string;
  returnableQuantity: string; invoicedQuantity: string; unbilledRemainingQuantity: string;
  invoiceLines: SalesReturnSourceInvoiceLine[];
  /** MD labels of the default destination (WU-SD-008b3a); null when the warehouse / location was soft-deleted. */
  warehouseCode?: string | null; warehouseName?: string | null; warehouseActive?: boolean | null;
  locationCode?: string | null; locationName?: string | null; locationActive?: boolean | null;
}
export interface SalesReturnSourceOrder {
  id: number; documentNo: string; customerId: number; customerCode: string; customerName: string; businessDate: string;
  currencyCode: string; baseCurrencyCode: string; exchangeRate: string; version: number; lines: SalesReturnSourceLine[];
}
export interface SalesReturnSourceQuery { page: number; pageSize: number; keyword?: string; customerId?: number; sort?: string; }

/**
 * CREATE / PUT draft body (ADR-185): no customer, order, currency, amount, status, INV or FI field. The line quantity is
 * `unbilledQuantity + Σ billed.quantity`; every quantity stays decimal text and is sent as an exact number literal.
 */
export interface SalesReturnDraftRequest {
  requestKey: string; businessDate: string; remark: string | null;
  lines: {
    lineNo: number; deliveryLineId: number; warehouseId: number; locationId: number | null; unbilledQuantity: string;
    billed: { invoiceLineId: number; quantity: string }[];
  }[];
}
/** `replayed` is true for 200: the same request key and content was already saved; nothing was created again. */
export interface SalesReturnCreateResult { saved: SalesReturnDetail; replayed: boolean; }

/** Request keys: 1–64 letters, digits, `-` or `_` (server rule); a UUID fits. */
export function newReturnRequestKey(): string { return crypto.randomUUID(); }

function id(value: number, what: string): number {
  if (!Number.isSafeInteger(value) || value <= 0) throw new Error(`Invalid ${what}`);
  return value;
}

function quantity(value: string): string {
  if (!QUANTITY_TEXT.test(value)) throw new Error('Invalid quantity text');
  return value;
}

/** JSON of a draft with every quantity as an exact number literal (never through a JavaScript double). */
export function returnDraftJson(request: SalesReturnDraftRequest): string {
  const lines = request.lines.map(line => {
    const billed = line.billed.map(portion =>
      `{"invoiceLineId":${id(portion.invoiceLineId, 'invoice line')},"quantity":${quantity(portion.quantity)}}`);
    return `{"lineNo":${id(line.lineNo, 'line number')},"deliveryLineId":${id(line.deliveryLineId, 'delivery line')},` +
      `"warehouseId":${id(line.warehouseId, 'warehouse')},` +
      `"locationId":${line.locationId == null ? 'null' : id(line.locationId, 'location')},` +
      `"unbilledQuantity":${quantity(line.unbilledQuantity)},"billed":[${billed.join(',')}]}`;
  });
  return `{"requestKey":${JSON.stringify(request.requestKey)},"businessDate":${JSON.stringify(request.businessDate)},` +
    `"remark":${JSON.stringify(request.remark)},"lines":[${lines.join(',')}]}`;
}

const JSON_HEADERS = new HttpHeaders({ 'Content-Type': 'application/json' });

/**
 * REQ_F4112.7.1 (WU-SD-008b1, ADR-192/193): sales return HTTP client. Each call needs its own SAL.RETURN action; the
 * server applies that action's data scope to the source order. Writes allow 20 s because SUBMIT may post INV and FI.
 */
@Injectable({ providedIn: 'root' })
export class SalesReturnService {
  private readonly http = inject(HttpClient);

  list(query: SalesReturnQuery): Observable<PagedResult<SalesReturnSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'sort', 'status'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    for (const key of ['orderId', 'customerId'] as const) {
      const value = query[key];
      if (value != null) params = params.set(key, value);
    }
    return this.http.get(API, { params, responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<PagedResult<SalesReturnSummary>>(raw, DECIMAL_KEYS)), timeout(6000));
  }

  get(returnId: number): Observable<SalesReturnDetail> {
    return this.http.get(`${API}/${id(returnId, 'return id')}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesReturnDetail>(raw, DECIMAL_KEYS)), timeout(6000));
  }

  /** ADR-193: Approved orders with returnable Posted delivery lines (SAL.RETURN.CREATE scope); for the order picker. */
  sourceOrders(query: SalesReturnSourceQuery): Observable<PagedResult<SalesReturnSourceOrderSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const keyword = query.keyword?.trim(), sort = query.sort?.trim();
    if (keyword) params = params.set('keyword', keyword);
    if (sort) params = params.set('sort', sort);
    if (query.customerId != null) params = params.set('customerId', query.customerId);
    return this.http.get<PagedResult<SalesReturnSourceOrderSummary>>(`${API}/source-orders`, { params })
      .pipe(timeout(6000));
  }

  /** ADR-193: the order header the return copies and its returnable lines with unbilled and creditable balances. */
  sourceOrder(orderId: number): Observable<SalesReturnSourceOrder> {
    return this.http.get(`${API}/source-orders/${id(orderId, 'order id')}`, { responseType: 'text' }).pipe(
      jsonTextErrors(), map(raw => parseJsonWithDecimalText<SalesReturnSourceOrder>(raw, SOURCE_DECIMAL_KEYS)),
      timeout(6000));
  }

  /** 201 = new Draft, 200 = identical replay of the same request key, so resending the same body is safe. */
  create(request: SalesReturnDraftRequest): Observable<SalesReturnCreateResult> {
    const body = returnDraftJson(request);
    return this.http.post(API, body, { observe: 'response', responseType: 'text', headers: JSON_HEADERS }).pipe(
      jsonTextErrors(), map(response => ({
        saved: parseJsonWithDecimalText<SalesReturnDetail>(response.body ?? '', DECIMAL_KEYS),
        replayed: response.status === 200
      })), timeout(20000));
  }

  /** Replaces a Draft (or revises a Rejected return back to Draft); the request key must stay the saved one. */
  replace(returnId: number, version: number, draft: SalesReturnDraftRequest): Observable<SalesReturnDetail> {
    const body = `{"version":${id(version, 'version')},"draft":${returnDraftJson(draft)}}`;
    return this.http.put(`${API}/${id(returnId, 'return id')}`, body, { responseType: 'text', headers: JSON_HEADERS })
      .pipe(jsonTextErrors(), map(raw => parseJsonWithDecimalText<SalesReturnDetail>(raw, DECIMAL_KEYS)),
        timeout(20000));
  }

  /** Soft-deletes a current-version Draft. */
  delete(returnId: number, version: number): Observable<void> {
    const params = new HttpParams().set('version', id(version, 'version'));
    return this.http.delete<void>(`${API}/${id(returnId, 'return id')}`, { params }).pipe(timeout(20000));
  }

  /** Sends a Draft to SYS approval; with no applicable step the server posts it and answers Posted. */
  submit(returnId: number, version: number): Observable<SalesReturnDetail> {
    const body = `{"version":${id(version, 'version')}}`;
    return this.http.post(`${API}/${id(returnId, 'return id')}/submit`, body,
      { responseType: 'text', headers: JSON_HEADERS }).pipe(jsonTextErrors(),
      map(raw => parseJsonWithDecimalText<SalesReturnDetail>(raw, DECIMAL_KEYS)), timeout(20000));
  }
}
