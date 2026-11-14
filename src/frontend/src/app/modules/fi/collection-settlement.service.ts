import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/ar/collection-settlements';
/** Money keys read as exact decimal text (`decimal(19,4)` can exceed a JavaScript number). */
const AMOUNT_KEYS = ['totalAmount', 'amount', 'receivableOpenAmount', 'receiptUnappliedAmount'] as const;

/** Settlement states shown to users (`Approved` only exists inside the posting transaction; deleted rows are never listed). */
export type CollectionSettlementStatus = 'Draft' | 'Pending' | 'Rejected' | 'Approved' | 'Posted';
export const COLLECTION_SETTLEMENT_STATUSES = ['Draft', 'Pending', 'Rejected', 'Posted'] as const;

/** List row (`CollectionSettlementSummary`, WU-FI-005b4b). Customer labels are null when MD has no current-company record. */
export interface CollectionSettlementSummary {
  id: number; documentNo: string; customerId: number; businessDate: string; currency: string;
  receiptId: number; receiptDocumentNo: string | null;
  /** Exact decimal text: the sum of the lines. */
  totalAmount: string;
  lineCount: number; status: CollectionSettlementStatus; version: number;
  customerCode: string | null; customerName: string | null;
}

/** One saved line; `receivableOpenAmount` is the receivable's open amount when read (posting re-checks it). */
export interface CollectionSettlementLine { lineNo: number; receivableId: number; amount: string; receivableOpenAmount: string }

/** Detail (`CollectionSettlementDocumentDetail`): receipt number and its current unapplied amount (null until the receipt is posted). */
export interface CollectionSettlementDetail extends Omit<CollectionSettlementSummary, 'lineCount'> {
  receiptUnappliedAmount: string | null; remark: string | null;
  createdAt: string; createdBy: number; updatedAt: string | null; updatedBy: number | null;
  lines: CollectionSettlementLine[];
}

export interface CollectionSettlementApproval { instanceId: number; status: string; currentStepNo: number | null; stepCount: number }
/** Posting receipt returned when SYS approved at once: what was applied and the receipt's unapplied balance after it. */
export interface CollectionSettlementPosting {
  baseCurrency: string; totalAmount: string; receiptUnappliedAmount: string; journalId: number;
  allocations: { receivableId: number; amount: string; receivableOpenAmount: string }[];
}
export interface CollectionSettlementWorkflowResult {
  id: number; documentNo: string; status: CollectionSettlementStatus; version: number;
  approval: CollectionSettlementApproval; posting: CollectionSettlementPosting | null;
}

/** One refused line from a 422 `lineErrors` extension (1-based document line, BR-FI-040). */
export interface SettlementLineError { line: number; receivableId: number; reason: string }

export interface CollectionSettlementQuery {
  page: number; pageSize: number; keyword?: string; status?: string; from?: string; to?: string; sort?: string;
  customerId?: number | null; receiptId?: number | null;
}

/** Line reasons the server sends inside `lineErrors` (CollectionWriteOffLineReasons); each has an `errors.json` text. */
export const SETTLEMENT_LINE_REASONS = [
  'FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_NOT_FOUND', 'FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_MISMATCH',
  'FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_AFTER_SETTLEMENT', 'FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN',
] as const;

/**
 * Codes the settlement screens name (WU-FI-005c2b1). Write-off codes come back when a submit re-checks the receipt and lines,
 * or when SYS approves at once and FI refuses to post; anything else uses the shared generic messages.
 */
export const COLLECTION_SETTLEMENT_ERROR_CODES = [
  'FI.COLLECTION.SETTLEMENT_NOT_FOUND', 'FI.COLLECTION.SETTLEMENT_INVALID_STATUS',
  'FI.COLLECTION.SETTLEMENT_APPROVAL_RESULT_INVALID', 'FI.COLLECTION.SETTLEMENT_APPROVAL_STATE_CHANGED',
  'FI.COLLECTION_WRITEOFF.ACCOUNT_INVALID', 'FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID', 'FI.COLLECTION_WRITEOFF.INCONSISTENT_STATE',
  'FI.COLLECTION_WRITEOFF.INVALID_INPUT', 'FI.COLLECTION_WRITEOFF.INVALID_SOURCE', 'FI.COLLECTION_WRITEOFF.LOCK_TIMEOUT',
  'FI.COLLECTION_WRITEOFF.MAPPING_MISSING', 'FI.COLLECTION_WRITEOFF.RECEIPT_INSUFFICIENT', 'FI.COLLECTION_WRITEOFF.RECEIPT_MISMATCH',
  'FI.COLLECTION_WRITEOFF.RECEIPT_NOT_POSTED', 'FI.COLLECTION_WRITEOFF.SOURCE_CONFLICT',
  'FI.COLLECTION_WRITEOFF.SOURCE_CONTRACT_UNAVAILABLE', 'FI.COLLECTION_WRITEOFF.SOURCE_MISMATCH',
  'FI.COLLECTION_WRITEOFF.SOURCE_NOT_FOUND', 'FI.COLLECTION_WRITEOFF.SOURCE_NOT_POSTABLE', 'FI.COLLECTION_WRITEOFF.WRITE_FAILED',
  'SYS.NUMBER_RULE.NOT_CONFIGURED', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED',
] as const;

/**
 * Line errors of a 422 response, in line order. Unknown reasons keep their code (the page shows a generic line text for them);
 * malformed entries are dropped so a bad payload never breaks the screen.
 */
export function settlementLineErrors(error: unknown): SettlementLineError[] {
  if (!(error instanceof HttpErrorResponse) || error.status !== 422) return [];
  const body = error.error as { lineErrors?: unknown } | null;
  if (!Array.isArray(body?.lineErrors)) return [];
  return (body.lineErrors as Partial<SettlementLineError>[])
    .filter((e): e is SettlementLineError => Number.isInteger(e?.line) && Number.isInteger(e?.receivableId) && typeof e?.reason === 'string')
    .sort((a, b) => a.line - b.line);
}

/** i18n key of one line reason: the known codes under `errors.`, otherwise the generic line text. */
export function settlementLineReasonKey(reason: string): string {
  return (SETTLEMENT_LINE_REASONS as readonly string[]).includes(reason) ? `errors.${reason}` : 'fi.collectionSettlement.lineReasonUnknown';
}

@Injectable({ providedIn: 'root' })
export class CollectionSettlementService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, COLLECTION_SETTLEMENT_ERROR_CODES); }
  list(query: CollectionSettlementQuery): Observable<PagedResult<CollectionSettlementSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    for (const key of ['customerId', 'receiptId'] as const) {
      const value = query[key];
      if (value) params = params.set(key, value);
    }
    return this.read<PagedResult<CollectionSettlementSummary>>(this.http.get(API, { params, responseType: 'text' }));
  }
  get(id: number): Observable<CollectionSettlementDetail> {
    return this.read<CollectionSettlementDetail>(this.http.get(`${API}/${id}`, { responseType: 'text' }));
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  submit(id: number, version: number): Observable<CollectionSettlementWorkflowResult> {
    return this.read<CollectionSettlementWorkflowResult>(this.http.post(`${API}/${id}/submit`, { version }, { responseType: 'text' }));
  }
  withdraw(id: number, version: number): Observable<CollectionSettlementWorkflowResult> {
    return this.read<CollectionSettlementWorkflowResult>(this.http.post(`${API}/${id}/withdraw`, { version }, { responseType: 'text' }));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(jsonTextErrors(), map(text => parseJsonWithDecimalText<T>(text, AMOUNT_KEYS)));
  }
}
