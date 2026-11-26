import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { apiErrorKey, blobJsonErrors, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/pur/invoices';
/** Every decimal(19,4)/(19,6) field is kept as the exact server digits (never a JavaScript double). */
const DECIMAL_KEYS = ['totalNet', 'totalTax', 'totalGross', 'quantity', 'unitPrice', 'taxRatePercent', 'net', 'tax', 'gross',
  'netDifference', 'taxDifference', 'grossDifference', 'baseline', 'invoice', 'difference'] as const;

/** POST /{id}/post refusals with a user-facing reason: PUR preflight (008b4) and FI.PURCHASE_POSTING.* passed through unchanged. */
export const PURCHASE_INVOICE_POST_ERROR_CODES = ['PUR.INVOICE.INVALID_POST_STATUS', 'PUR.INVOICE.RATE_SNAPSHOT_MISSING',
  'PUR.INVOICE.BASE_CURRENCY_NOT_SET', 'PUR.INVOICE.BASE_CURRENCY_CHANGED', 'PUR.INVOICE.INVALID_POSTING_SNAPSHOT',
  'PUR.INVOICE.INVALID_POSTING_DIFFERENCE', 'PUR.INVOICE.FI_RECEIPT_MISMATCH', 'PUR.INVOICE.FI_POSTING_FAILED',
  'FI.PURCHASE_POSTING.ACCOUNT_INVALID', 'FI.PURCHASE_POSTING.INCONSISTENT_STATE', 'FI.PURCHASE_POSTING.INVALID_AMOUNTS',
  'FI.PURCHASE_POSTING.INVALID_INPUT', 'FI.PURCHASE_POSTING.LOCK_TIMEOUT', 'FI.PURCHASE_POSTING.MAPPING_MISSING',
  'FI.PURCHASE_POSTING.SOURCE_CONFLICT', 'FI.PURCHASE_POSTING.SOURCE_CONTRACT_UNAVAILABLE', 'FI.PURCHASE_POSTING.SOURCE_MISMATCH',
  'FI.PURCHASE_POSTING.SOURCE_NOT_FOUND', 'FI.PURCHASE_POSTING.SOURCE_NOT_POSTABLE', 'FI.PURCHASE_POSTING.WRITE_FAILED'] as const;
export const PURCHASE_INVOICE_STATUSES = ['Draft', 'Matched', 'Disputed', 'Rejected', 'Posted'] as const;
export type PurchaseInvoiceStatus = (typeof PURCHASE_INVOICE_STATUSES)[number];
export const PURCHASE_INVOICE_SORTS = ['businessDate', 'supplierInvoiceNumber', 'totalGross'] as const;
export const PURCHASE_INVOICE_ERROR_CODES = ['PUR.INVOICE.NOT_FOUND', 'PUR.INVOICE.EXPORT_LIMIT', 'COMMON.PERMISSION.DENIED', 'COMMON.CONCURRENCY.CONFLICT',
  'PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE', 'PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY', 'PUR.INVOICE.INVALID_RESOLUTION',
  'PUR.INVOICE.INVALID_RESOLVE_STATUS', 'PUR.INVOICE.INVALID_MATCH_STATUS', 'PUR.INVOICE.INVALID_SOURCE_IDENTITY',
  'PUR.INVOICE.INVALID_AMOUNT_SOURCE', 'PUR.INVOICE.INVALID_SOURCE_QUANTITY', 'PUR.INVOICE.INVALID_REQUEST_QUANTITY',
  ...PURCHASE_INVOICE_POST_ERROR_CODES] as const;
/** Errors after which the saved invoice may differ from the screen, so the detail is read again (lock timeouts and
 * insufficient receipt quantity change nothing, so the user's input stays). */
export const PURCHASE_INVOICE_RELOAD_CODES = ['COMMON.CONCURRENCY.CONFLICT', 'PUR.INVOICE.INVALID_RESOLUTION',
  'PUR.INVOICE.INVALID_RESOLVE_STATUS', 'PUR.INVOICE.INVALID_MATCH_STATUS', 'PUR.INVOICE.INVALID_SOURCE_IDENTITY',
  'PUR.INVOICE.INVALID_AMOUNT_SOURCE', 'PUR.INVOICE.INVALID_SOURCE_QUANTITY', 'PUR.INVOICE.INVALID_REQUEST_QUANTITY',
  'PUR.INVOICE.INVALID_POST_STATUS', 'PUR.INVOICE.INVALID_POSTING_SNAPSHOT', 'PUR.INVOICE.INVALID_POSTING_DIFFERENCE',
  'FI.PURCHASE_POSTING.SOURCE_MISMATCH', 'FI.PURCHASE_POSTING.SOURCE_NOT_POSTABLE'] as const;
export const PURCHASE_INVOICE_DIFFERENCE_KINDS = ['UNIT_PRICE', 'TAX_RATE', 'NET', 'TAX', 'GROSS'] as const;
export const RESOLVE_REASON_MAX = 500;

export interface PurchaseInvoiceSummary { id: number; supplierId: number; purchaseOrderId: number; supplierInvoiceNumber: string;
  businessDate: string; currency: string; status: PurchaseInvoiceStatus; totalNet: string; totalTax: string; totalGross: string;
  lineCount: number; version: number }
export interface PurchaseInvoiceLine { id: number; lineNo: number; receiptLineId: number; quantity: string; unitPrice: string;
  taxRatePercent: string; net: string; tax: string; gross: string; differences: PurchaseInvoiceDifference[] }
/** One live price/tax difference of a Disputed line (empty for every other state); `difference` = invoice − baseline. */
export interface PurchaseInvoiceDifference { kind: string; baseline: string; invoice: string; difference: string }
export type PurchaseInvoiceAction = 'Accept' | 'Reject';
export interface PurchaseInvoiceDecisionInput { action: PurchaseInvoiceAction | ''; reason: string }
export type PurchaseInvoiceResolveIssue = 'action' | 'reason' | 'reasonLength';
/** MATCH / RESOLVE reply: only the new state and version are used; the screen then reads the saved detail again. */
export interface PurchaseInvoiceActionResult { id: number; version: number; status: PurchaseInvoiceStatus }
/** One immutable RESOLVE decision; `invoiceVersion` is the invoice version that was resolved. */
export interface PurchaseInvoiceResolution { id: number; invoiceVersion: number; lineNo: number; action: 'Accept' | 'Reject' | string;
  reason: string; netDifference: string; taxDifference: string; grossDifference: string; decidedBy: number; decidedAt: string }
/** Posting time and FI source / payable / journal IDs are set only for a Posted invoice (008b6). */
export interface PurchaseInvoiceDetail extends Omit<PurchaseInvoiceSummary, 'lineCount'> { createdAt: string; createdBy: number;
  updatedAt: string | null; lines: PurchaseInvoiceLine[]; resolutions: PurchaseInvoiceResolution[]; postedAt?: string | null;
  fiPurchaseSourceId?: number | null; fiPayableId?: number | null; fiJournalId?: number | null }
/** POST /{id}/post reply; `replayed` = the invoice was already posted by this request and nothing was written again. */
export interface PurchaseInvoicePostResult extends PurchaseInvoiceActionResult { replayed: boolean; fiPurchaseSourceId: number;
  fiPayableId: number; fiJournalId: number; postedAt: string }
export interface PurchaseInvoiceQuery { page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: string; supplierId?: number | null; purchaseOrderId?: number | null; sort?: string }
export type PurchaseInvoiceExportQuery = Omit<Partial<PurchaseInvoiceQuery>, 'page' | 'pageSize'>;
/** The export reads up to 10,000 lines in one response, so it gets a longer wait than a list read. */
export const PURCHASE_INVOICE_EXPORT_TIMEOUT_MS = 15000;

/** List and export send the same filter / sort parameters; blanks and empty IDs are left out. */
function filterParams(params: HttpParams, query: PurchaseInvoiceExportQuery): HttpParams {
  for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
    const value = query[key]?.trim();
    if (value) params = params.set(key, value);
  }
  for (const key of ['supplierId', 'purchaseOrderId'] as const) {
    const value = query[key];
    if (value) params = params.set(key, value);
  }
  return params;
}

/** Client-side filter check mirroring the API's 400 rules, so an impossible query is never sent. */
export function purchaseInvoiceFilterIssue(query: Pick<PurchaseInvoiceQuery, 'from' | 'to' | 'supplierId' | 'purchaseOrderId'>):
  'range' | 'supplier' | 'order' | null {
  if (query.from && query.to && query.from > query.to) return 'range';
  const invalid = (id: number | null | undefined) => id !== null && id !== undefined && !(Number.isSafeInteger(id) && id > 0);
  if (invalid(query.supplierId)) return 'supplier';
  if (invalid(query.purchaseOrderId)) return 'order';
  return null;
}

/** Lines that need a decision (BR-PUR-039a): exactly those whose live differences are not empty. */
export function disputedInvoiceLines(lines: readonly PurchaseInvoiceLine[]): PurchaseInvoiceLine[] {
  return lines.filter(line => (line.differences?.length ?? 0) > 0);
}

/** Client mirror of PUR.INVOICE.INVALID_RESOLUTION: every differing line needs Accept/Reject and a 1–500 character trimmed reason. */
export function purchaseInvoiceResolveIssues(lines: readonly PurchaseInvoiceLine[],
  decisions: Readonly<Record<number, PurchaseInvoiceDecisionInput | undefined>>): Record<number, PurchaseInvoiceResolveIssue> {
  const issues: Record<number, PurchaseInvoiceResolveIssue> = {};
  for (const line of disputedInvoiceLines(lines)) {
    const decision = decisions[line.lineNo];
    const reason = decision?.reason.trim() ?? '';
    if (decision?.action !== 'Accept' && decision?.action !== 'Reject') issues[line.lineNo] = 'action';
    else if (!reason) issues[line.lineNo] = 'reason';
    else if (reason.length > RESOLVE_REASON_MAX) issues[line.lineNo] = 'reasonLength';
  }
  return issues;
}

/** Client for 介面 4.19: list/detail (008c1), MATCH/RESOLVE (008c3b) and POST to FI (008b6); create and revise belong to 008c2. */
@Injectable({ providedIn: 'root' })
export class PurchaseInvoiceService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, PURCHASE_INVOICE_ERROR_CODES); }
  list(query: PurchaseInvoiceQuery): Observable<PagedResult<PurchaseInvoiceSummary>> {
    const params = filterParams(new HttpParams().set('page', query.page).set('pageSize', query.pageSize), query);
    return this.read<PagedResult<PurchaseInvoiceSummary>>(this.http.get(API, { params, responseType: 'text' }));
  }
  /**
   * GET /export with the applied filters and sort (never paging); headers and status follow the Accept-Language set by the
   * API interceptor. Needs PUR.INVOICE.EXPORT; up to 10,000 lines, so it waits longer than a list read (008c3c2).
   */
  export(query: PurchaseInvoiceExportQuery): Observable<HttpResponse<Blob>> {
    return this.http.get(`${API}/export`, { params: filterParams(new HttpParams(), query), observe: 'response', responseType: 'blob' })
      .pipe(blobJsonErrors(), timeout(PURCHASE_INVOICE_EXPORT_TIMEOUT_MS));
  }
  get(id: number): Observable<PurchaseInvoiceDetail> {
    return this.read<PurchaseInvoiceDetail>(this.http.get(`${API}/${id}`, { responseType: 'text' }));
  }
  /** POST /{id}/match?version=N with an empty body (PUR.INVOICE.MATCH). */
  match(id: number, version: number): Observable<PurchaseInvoiceActionResult> {
    return this.read<PurchaseInvoiceActionResult>(this.http.post(`${API}/${id}/match`, null,
      { params: new HttpParams().set('version', version), responseType: 'text' }));
  }
  /** POST /{id}/resolve?version=N with one decision per differing line (PUR.INVOICE.RESOLVE). */
  resolve(id: number, version: number, lines: { lineNo: number; action: PurchaseInvoiceAction; reason: string }[]):
    Observable<PurchaseInvoiceActionResult> {
    return this.read<PurchaseInvoiceActionResult>(this.http.post(`${API}/${id}/resolve`, { lines },
      { params: new HttpParams().set('version', version), responseType: 'text' }));
  }
  /** POST /{id}/post?version=N with an empty body (PUR.INVOICE.POST, company-wide); only a Matched invoice is accepted. */
  post(id: number, version: number): Observable<PurchaseInvoicePostResult> {
    return this.read<PurchaseInvoicePostResult>(this.http.post(`${API}/${id}/post`, null,
      { params: new HttpParams().set('version', version), responseType: 'text' }));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(jsonTextErrors(), map(text => parseJsonWithDecimalText<T>(text, DECIMAL_KEYS)));
  }
}
