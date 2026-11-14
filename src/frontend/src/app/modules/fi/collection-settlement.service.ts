import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/ar/collection-settlements';
/** Money keys read as exact decimal text (`decimal(19,4)` can exceed a JavaScript number). */
const AMOUNT_KEYS = ['totalAmount', 'amount', 'receivableOpenAmount', 'receiptUnappliedAmount', 'originalAmount', 'openAmount'] as const;
const JSON_HEADERS = new HttpHeaders({ 'Content-Type': 'application/json' });

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

/** One posted receivable that still has an open amount (`OpenReceivableSummary`, WU-FI-005c2a); amounts are exact text. */
export interface OpenReceivable {
  receivableId: number; sourceType: string; sourceId: number; postingDate: string; currency: string;
  originalAmount: string; openAmount: string;
}

/** Form line as typed: the amount keeps the user's text; open amount and source come from the picker or the saved detail. */
export interface SettlementDraftLine {
  receivableId: number; amount: string;
  /** Open amount when read (exact text); null when unknown. Posting re-checks it. */
  openAmount: string | null;
  sourceType: string | null; sourceId: number | null; postingDate: string | null;
}
/** Form values (WU-FI-005c2b2). The customer follows the chosen collection and is not sent (the server derives it). */
export interface SettlementDraft {
  receiptId: number | null; customerId: number | null; businessDate: string; remark: string; lines: SettlementDraftLine[];
}
export type SettlementHeaderField = 'receiptId' | 'businessDate' | 'lines' | 'remark';
export type SettlementLineField = 'receivableId' | 'amount';
/** Issue keys (suffixes under `fi.collectionSettlement.issue`, or full `errors.*` keys) per header field and per line index. */
export interface SettlementDraftIssues {
  header: Partial<Record<SettlementHeaderField, string>>;
  lines: Record<number, string>;
}

/** Same limits as the server (BR-FI-157): 1–1000 lines, each amount > 0 with ≤ 15 integer digits and 4 decimals; remark ≤ 500. */
export const SETTLEMENT_MAX_LINES = 1000;
export const SETTLEMENT_REMARK_MAX = 500;
const AMOUNT = /^(?:0|[1-9]\d{0,14})(?:\.\d{1,4})?$/;
const SCALE = 10_000n;

/** Plain decimal text of an amount field (spaces and thousands separators removed). */
export function lineAmountText(line: SettlementDraftLine): string { return line.amount.trim().replaceAll(',', ''); }

/** Exact amount in 1/10000 units; null when the text is not a non-negative decimal with at most 4 significant decimals. */
export function amountUnits(text: string | null | undefined): bigint | null {
  const value = (text ?? '').trim().replaceAll(',', '');
  const match = /^(\d+)(?:\.(\d+))?$/.exec(value);
  if (!match) return null;
  const fraction = (match[2] ?? '').replace(/0+$/, '');
  if (fraction.length > 4) return null;
  return BigInt(match[1]) * SCALE + BigInt(fraction.padEnd(4, '0'));
}

/** Plain decimal text of 1/10000 units (trailing fraction zeros dropped). */
export function unitsText(units: bigint): string {
  const negative = units < 0n;
  const abs = negative ? -units : units;
  const fraction = (abs % SCALE).toString().padStart(4, '0').replace(/0+$/, '');
  return `${negative ? '-' : ''}${abs / SCALE}${fraction ? '.' + fraction : ''}`;
}

/** Exact total of the valid line amounts (invalid ones count as 0) as plain text. */
export function settlementTotalText(draft: SettlementDraft): string {
  return unitsText(draft.lines.reduce((sum, line) => {
    const text = lineAmountText(line);
    return sum + (AMOUNT.test(text) ? amountUnits(text)! : 0n);
  }, 0n));
}

/**
 * Screen checks before sending (mirroring BR-FI-157 where the screen knows the numbers): collection and date required, the
 * date not before the collection's date, 1–1000 lines, each amount valid, positive and not above the receivable's open
 * amount, and the total not above the collection's unapplied cash. The server re-checks everything.
 */
export function settlementDraftIssues(draft: SettlementDraft, receiptUnapplied: string | null, receiptDate: string | null): SettlementDraftIssues {
  const header: Partial<Record<SettlementHeaderField, string>> = {};
  const lines: Record<number, string> = {};
  if (!draft.receiptId) header.receiptId = 'receiptRequired';
  if (!/^\d{4}-\d{2}-\d{2}$/.test(draft.businessDate)) header.businessDate = 'required';
  else if (receiptDate && draft.businessDate < receiptDate) header.businessDate = 'beforeReceipt';
  if (draft.remark.trim().length > SETTLEMENT_REMARK_MAX) header.remark = 'tooLong';
  if (!draft.lines.length) header.lines = 'noLines';
  else if (draft.lines.length > SETTLEMENT_MAX_LINES) header.lines = 'tooManyLines';
  draft.lines.forEach((line, index) => {
    const text = lineAmountText(line);
    if (!text) lines[index] = 'required';
    else if (!AMOUNT.test(text)) lines[index] = 'amountFormat';
    else if (/^0+(\.0+)?$/.test(text)) lines[index] = 'amountPositive';
    else {
      const open = amountUnits(line.openAmount);
      if (open !== null && amountUnits(text)! > open) lines[index] = 'exceedsOpen';
    }
  });
  const unapplied = amountUnits(receiptUnapplied);
  if (!header.lines && unapplied !== null && amountUnits(settlementTotalText(draft))! > unapplied) header.lines = 'exceedsUnapplied';
  return { header, lines };
}

/** True when the screen found something to fix. */
export function hasSettlementIssues(issues: SettlementDraftIssues): boolean {
  return Object.keys(issues.header).length > 0 || Object.keys(issues.lines).length > 0;
}

/**
 * Request JSON in screen line order: amounts are written as JSON numbers from the validated digits, never through a
 * JavaScript double; blank remark → null. `version` is added for a replacement. The customer is not sent.
 */
export function settlementJson(draft: SettlementDraft, version?: number): string {
  const lines = draft.lines.map(line => {
    const amount = lineAmountText(line);
    if (!AMOUNT.test(amount)) throw new Error('Invalid settlement amount');
    return `{"receivableId":${JSON.stringify(line.receivableId)},"amount":${amount}}`;
  });
  const fields = JSON.stringify({ ...(version === undefined ? {} : { version }), receiptId: draft.receiptId,
    businessDate: draft.businessDate, remark: draft.remark.trim() || null });
  return `${fields.slice(0, -1)},"lines":[${lines.join(',')}]}`;
}

/**
 * Fields named in a 400 `errors` map: header fields and `lines[i].receivableId` / `lines[i].amount` (request order = screen
 * order). Case-insensitive, with or without a `$.` prefix.
 */
export function settlementFieldErrors(error: unknown): { header: SettlementHeaderField[]; lines: Record<number, SettlementLineField> } {
  const result = { header: [] as SettlementHeaderField[], lines: {} as Record<number, SettlementLineField> };
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) return result;
  const errors = (error.error as { errors?: unknown } | null)?.errors;
  if (!errors || typeof errors !== 'object') return result;
  for (const raw of Object.keys(errors)) {
    const key = raw.replace(/^\$\./, '').toLowerCase();
    const line = /^lines\[(\d+)\](?:\.(receivableid|amount))?$/.exec(key);
    if (line) result.lines[Number(line[1])] = line[2] === 'receivableid' ? 'receivableId' : 'amount';
    else for (const field of ['receiptId', 'businessDate', 'lines', 'remark'] as const)
      if (key === field.toLowerCase() && !result.header.includes(field)) result.header.push(field);
  }
  return result;
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
  'FI.COLLECTION.SETTLEMENT_NOT_FOUND', 'FI.COLLECTION.SETTLEMENT_INVALID_STATUS', 'FI.COLLECTION.BASE_CURRENCY_MISSING',
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
  create(draft: SettlementDraft): Observable<CollectionSettlementDetail> {
    return this.read<CollectionSettlementDetail>(this.http.post(API, settlementJson(draft), { headers: JSON_HEADERS, responseType: 'text' }));
  }
  update(id: number, version: number, draft: SettlementDraft): Observable<CollectionSettlementDetail> {
    return this.read<CollectionSettlementDetail>(this.http.put(`${API}/${id}`, settlementJson(draft, version),
      { headers: JSON_HEADERS, responseType: 'text' }));
  }
  /** One customer's open posted receivables usable on `postedOnOrBefore` (oldest first, ADR-132). */
  openReceivables(customerId: number, postedOnOrBefore: string, page: number, pageSize: number): Observable<PagedResult<OpenReceivable>> {
    let params = new HttpParams().set('customerId', customerId).set('page', page).set('pageSize', pageSize);
    if (postedOnOrBefore) params = params.set('postedOnOrBefore', postedOnOrBefore);
    return this.read<PagedResult<OpenReceivable>>(this.http.get(`${API}/open-receivables`, { params, responseType: 'text' }));
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
