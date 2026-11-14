import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/ar/sales-collections';
/** Money keys read as exact decimal text (`decimal(19,4)` can exceed a JavaScript number). */
const AMOUNT_KEYS = ['amount', 'unappliedAmount'] as const;
const JSON_HEADERS = new HttpHeaders({ 'Content-Type': 'application/json' });

/** Receipt states shown to users (`Approved` only exists inside the posting transaction; `Deleted` rows are never listed). */
export type CollectionReceiptStatus = 'Draft' | 'Pending' | 'Rejected' | 'Approved' | 'Posted';
export const COLLECTION_RECEIPT_STATUSES = ['Draft', 'Pending', 'Rejected', 'Posted'] as const;

/** List row (`CollectionReceiptSummary`, WU-FI-005b4a). MD labels are null when MD has no current-company record. */
export interface CollectionReceiptSummary {
  id: number; documentNo: string; customerId: number; businessDate: string; currency: string;
  /** Exact decimal text. */
  amount: string;
  bankAccountId: number; bankReference: string; status: CollectionReceiptStatus; version: number;
  /** Posted receipt's current unapplied balance (exact text); null until Posted. */
  unappliedAmount: string | null;
  customerCode: string | null; customerName: string | null; bankAccountCode: string | null; bankAccountName: string | null;
}

/** Detail (`CollectionReceiptDocumentDetail`) with remark, audit fields and the GL journal once Posted. */
export interface CollectionReceiptDetail extends CollectionReceiptSummary {
  remark: string | null; createdAt: string; createdBy: number; updatedAt: string | null; updatedBy: number | null;
  journalId: number | null;
}

export interface CollectionReceiptApproval { instanceId: number; status: string; currentStepNo: number | null; stepCount: number }
export interface CollectionReceiptPosting { amount: string; unappliedAmount: string; journalId: number; baseCurrency: string }
/** Submit / withdraw result: `posting` is set when SYS approved at once (no approval steps) and the receipt was posted. */
export interface CollectionReceiptWorkflowResult {
  id: number; documentNo: string; status: CollectionReceiptStatus; version: number;
  approval: CollectionReceiptApproval; posting: CollectionReceiptPosting | null;
}

/** Form values as typed (the amount keeps the user's decimal text; IDs come from the pickers). */
export interface CollectionReceiptDraft {
  customerId: number | null; businessDate: string; amount: string; bankAccountId: number | null;
  bankReference: string; remark: string;
}
export type CollectionReceiptField = 'customerId' | 'businessDate' | 'amount' | 'bankAccountId' | 'bankReference' | 'remark';
export const COLLECTION_RECEIPT_FIELDS: readonly CollectionReceiptField[] =
  ['customerId', 'businessDate', 'amount', 'bankAccountId', 'bankReference', 'remark'];

export interface CollectionReceiptQuery {
  page: number; pageSize: number; keyword?: string; status?: string; from?: string; to?: string; sort?: string;
  customerId?: number | null; bankAccountId?: number | null;
}

/** Same limits as the server (BR-FI-156): amount > 0, ≤ 15 integer digits and 4 decimals; reference ≤ 50; remark ≤ 500. */
export const BANK_REFERENCE_MAX = 50;
export const REMARK_MAX = 500;
const AMOUNT = /^(?:0|[1-9]\d{0,14})(?:\.\d{1,4})?$/;
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/;

/** Plain decimal text of the amount field (spaces and thousands separators removed). */
export function amountText(draft: CollectionReceiptDraft): string { return draft.amount.trim().replaceAll(',', ''); }

/** i18n suffix (under `fi.collectionReceipt.issue`) per invalid field; empty when the draft may be sent. */
export function receiptDraftIssues(draft: CollectionReceiptDraft): Partial<Record<CollectionReceiptField, string>> {
  const issues: Partial<Record<CollectionReceiptField, string>> = {};
  if (!draft.customerId) issues.customerId = 'required';
  if (!/^\d{4}-\d{2}-\d{2}$/.test(draft.businessDate)) issues.businessDate = 'required';
  const amount = amountText(draft);
  if (!amount) issues.amount = 'required';
  else if (!AMOUNT.test(amount)) issues.amount = 'amountFormat';
  else if (/^0+(\.0+)?$/.test(amount)) issues.amount = 'amountPositive';
  if (!draft.bankAccountId) issues.bankAccountId = 'required';
  const reference = draft.bankReference.trim();
  if (!reference) issues.bankReference = 'required';
  else if (reference.length > BANK_REFERENCE_MAX) issues.bankReference = 'tooLong';
  else if (CONTROL.test(reference)) issues.bankReference = 'control';
  if (draft.remark.trim().length > REMARK_MAX) issues.remark = 'tooLong';
  return issues;
}

/**
 * Request JSON: the amount is written as a JSON number from the validated digits, never through a JavaScript double;
 * reference trimmed, blank remark → null. `version` is added for a replacement.
 */
export function receiptJson(draft: CollectionReceiptDraft, version?: number): string {
  const amount = amountText(draft);
  if (!AMOUNT.test(amount)) throw new Error('Invalid receipt amount');
  const fields = JSON.stringify({ ...(version === undefined ? {} : { version }), customerId: draft.customerId,
    businessDate: draft.businessDate, bankAccountId: draft.bankAccountId, bankReference: draft.bankReference.trim(),
    remark: draft.remark.trim() || null });
  return `${fields.slice(0, -1)},"amount":${amount}}`;
}

/**
 * Codes this screen names (WU-FI-005c1). Posting codes come back when SYS approves at once on submit and FI refuses to
 * post; MD codes when the chosen customer is no longer usable; anything else uses the shared generic messages.
 */
export const COLLECTION_RECEIPT_ERROR_CODES = [
  'FI.COLLECTION.RECEIPT_NOT_FOUND', 'FI.COLLECTION.RECEIPT_INVALID_STATUS', 'FI.COLLECTION.BANK_REFERENCE_DUPLICATE',
  'FI.COLLECTION.FUND_ACCOUNT_NOT_FOUND', 'FI.COLLECTION.FUND_ACCOUNT_INACTIVE', 'FI.COLLECTION.FUND_ACCOUNT_CURRENCY_MISMATCH',
  'FI.COLLECTION.BASE_CURRENCY_MISSING', 'FI.COLLECTION.BASE_CURRENCY_CHANGED', 'FI.COLLECTION.RECEIPT_APPROVAL_RESULT_INVALID',
  'FI.COLLECTION.RECEIPT_APPROVAL_STATE_CHANGED',
  'FI.COLLECTION_POSTING.ACCOUNT_INVALID', 'FI.COLLECTION_POSTING.BANK_REFERENCE_DUPLICATE',
  'FI.COLLECTION_POSTING.FUND_ACCOUNT_INVALID', 'FI.COLLECTION_POSTING.INCONSISTENT_STATE', 'FI.COLLECTION_POSTING.INVALID_INPUT',
  'FI.COLLECTION_POSTING.INVALID_SOURCE', 'FI.COLLECTION_POSTING.LOCK_TIMEOUT', 'FI.COLLECTION_POSTING.MAPPING_MISSING',
  'FI.COLLECTION_POSTING.SOURCE_CONFLICT', 'FI.COLLECTION_POSTING.SOURCE_CONTRACT_UNAVAILABLE',
  'FI.COLLECTION_POSTING.SOURCE_MISMATCH', 'FI.COLLECTION_POSTING.SOURCE_NOT_FOUND', 'FI.COLLECTION_POSTING.SOURCE_NOT_POSTABLE',
  'FI.COLLECTION_POSTING.WRITE_FAILED',
  'MD.CUSTOMER.NOT_FOUND', 'MD.CUSTOMER.NOT_APPROVED', 'SYS.NUMBER_RULE.NOT_CONFIGURED',
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED',
] as const;

/** Form fields named in a 400 `errors` map (case-insensitive, as ASP.NET may send `Amount` or `$.amount`). */
export function receiptFieldErrors(error: unknown): CollectionReceiptField[] {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) return [];
  const errors = (error.error as { errors?: unknown } | null)?.errors;
  if (!errors || typeof errors !== 'object') return [];
  const keys = Object.keys(errors).map(key => key.replace(/^\$\./, '').toLowerCase());
  return COLLECTION_RECEIPT_FIELDS.filter(field => keys.includes(field.toLowerCase()));
}

@Injectable({ providedIn: 'root' })
export class CollectionReceiptService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, COLLECTION_RECEIPT_ERROR_CODES); }
  list(query: CollectionReceiptQuery): Observable<PagedResult<CollectionReceiptSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    for (const key of ['customerId', 'bankAccountId'] as const) {
      const value = query[key];
      if (value) params = params.set(key, value);
    }
    return this.read<PagedResult<CollectionReceiptSummary>>(this.http.get(API, { params, responseType: 'text' }));
  }
  get(id: number): Observable<CollectionReceiptDetail> {
    return this.read<CollectionReceiptDetail>(this.http.get(`${API}/${id}`, { responseType: 'text' }));
  }
  create(draft: CollectionReceiptDraft): Observable<CollectionReceiptDetail> {
    return this.read<CollectionReceiptDetail>(this.http.post(API, receiptJson(draft), { headers: JSON_HEADERS, responseType: 'text' }));
  }
  update(id: number, version: number, draft: CollectionReceiptDraft): Observable<CollectionReceiptDetail> {
    return this.read<CollectionReceiptDetail>(this.http.put(`${API}/${id}`, receiptJson(draft, version),
      { headers: JSON_HEADERS, responseType: 'text' }));
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  submit(id: number, version: number): Observable<CollectionReceiptWorkflowResult> {
    return this.read<CollectionReceiptWorkflowResult>(this.http.post(`${API}/${id}/submit`, { version }, { responseType: 'text' }));
  }
  withdraw(id: number, version: number): Observable<CollectionReceiptWorkflowResult> {
    return this.read<CollectionReceiptWorkflowResult>(this.http.post(`${API}/${id}/withdraw`, { version }, { responseType: 'text' }));
  }
  private read<T>(request: Observable<string>): Observable<T> {
    return request.pipe(jsonTextErrors(), map(text => parseJsonWithDecimalText<T>(text, AMOUNT_KEYS)));
  }
}
