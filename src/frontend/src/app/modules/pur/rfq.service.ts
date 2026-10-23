import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { SelectDialogQuery } from '../../shared/dialogs/select-dialog';
import { requisitionQuantityPattern, validRequisitionDate } from './requisition.service';
import { RequisitionItemOption } from './requisition.service';

export type RfqStatus = 'Draft' | 'Sent' | 'Closed';
export interface RfqSummary { id: number; documentNo: string; businessDate: string; replyDeadline: string;
  ownerId: number; status: RfqStatus; lineCount: number; invitationCount: number; version: number }
export interface RfqDetail extends Omit<RfqSummary, 'lineCount' | 'invitationCount'> {
  remark: string | null; sentAt: string | null; createdAt: string;
  lines: { id: number; lineNo: number; itemId: number; requisitionLineId: number | null;
    requiredDate: string; quantity: number; remark: string | null }[];
  invitations: { id: number; supplierId: number; supplierCodeSnapshot: string | null;
    supplierNameSnapshot: string | null; sentAt: string | null }[];
}
export interface RfqQuery { page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: RfqStatus; requisitionLineId?: number; sort?: string }
export interface RfqLineDraft { itemId: number | null; requisitionLineId: number | null;
  requiredDate: string; quantity: string; remark: string }
export interface RfqDraft { businessDate: string; replyDeadline: string; remark: string;
  lines: RfqLineDraft[]; supplierIds: number[] }
export interface RfqSupplierOption { id: number; code: string; name: string; isActive: boolean; isApproved: boolean }
export interface RfqSavedOptions { items: RequisitionItemOption[]; suppliers: RfqSupplierOption[]; sources: RfqSourceOption[] }
export interface RfqSourceOption { id: number; documentNo: string; lineNo: number;
  itemId: number; remainingQuantity: number; isAvailable?: boolean }

/** Field paths match the RFQ editor and server validation response. */
export function rfqFieldErrors(draft: RfqDraft): string[] {
  const errors: string[] = [];
  const validDate = validRequisitionDate(draft.businessDate);
  if (!validDate) errors.push('businessDate');
  if (!validRequisitionDate(draft.replyDeadline) ||
    (validDate && draft.replyDeadline < draft.businessDate)) errors.push('replyDeadline');
  if (draft.remark.trim().length > 500) errors.push('remark');
  if (draft.lines.length < 1 || draft.lines.length > 1000) errors.push('lines');
  if (draft.supplierIds.length < 1 || draft.supplierIds.length > 50 ||
    draft.supplierIds.some(id => !Number.isSafeInteger(id) || id <= 0) ||
    new Set(draft.supplierIds).size !== draft.supplierIds.length) errors.push('supplierIds');
  draft.lines.forEach((line, index) => {
    const path = `lines.${index}`;
    if (!Number.isSafeInteger(line.itemId) || (line.itemId ?? 0) <= 0) errors.push(`${path}.itemId`);
    if (line.requisitionLineId !== null &&
      (!Number.isSafeInteger(line.requisitionLineId) || line.requisitionLineId <= 0))
      errors.push(`${path}.requisitionLineId`);
    if (!validRequisitionDate(line.requiredDate) ||
      (validDate && line.requiredDate < draft.businessDate)) errors.push(`${path}.requiredDate`);
    if (!requisitionQuantityPattern.test(line.quantity.trim()) || !/[1-9]/.test(line.quantity))
      errors.push(`${path}.quantity`);
    if (line.remark.trim().length > 500) errors.push(`${path}.remark`);
  });
  return errors;
}

@Injectable({ providedIn: 'root' })
export class RfqService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
      if (code === 'MD.ITEM.NOT_FOUND' || code === 'MD.ITEM.INACTIVE') return 'pur.rfq.itemUnavailable';
      if (code === 'MD.SUPPLIER.NOT_FOUND' || code === 'MD.SUPPLIER.NOT_APPROVED')
        return 'pur.rfq.supplierUnavailable';
      if (code === 'PUR.REQUISITION.LINE_NOT_FOUND') return 'pur.rfq.sourceUnavailable';
    }
    return apiErrorKey(error, [
    'PUR.RFQ.NOT_FOUND', 'PUR.RFQ.NOT_EDITABLE', 'PUR.RFQ.INVALID_SOURCE',
    'PUR.RFQ.NOT_SENDABLE', 'PUR.RFQ.NOT_CLOSABLE',
    'COMMON.PERMISSION.DENIED', 'COMMON.CONCURRENCY.CONFLICT']);
  }
  list(query: RfqQuery): Observable<PagedResult<RfqSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.requisitionLineId) params = params.set('requisitionLineId', query.requisitionLineId);
    return this.http.get<PagedResult<RfqSummary>>('/api/v1/pur/rfqs', { params });
  }
  get(id: number): Observable<RfqDetail> { return this.http.get<RfqDetail>(`/api/v1/pur/rfqs/${id}`); }
  getForEdit(id: number): Observable<RfqDetail> {
    return this.http.get<RfqDetail>(`/api/v1/pur/rfqs/${id}/edit`);
  }
  savedOptions(id: number): Observable<RfqSavedOptions> {
    return this.http.get<RfqSavedOptions>(`/api/v1/pur/rfqs/${id}/saved-options`);
  }
  itemOptions(query: SelectDialogQuery): Observable<PagedResult<RequisitionItemOption>> {
    return this.http.get<PagedResult<RequisitionItemOption>>('/api/v1/pur/rfqs/item-options',
      { params: this.optionParams(query) });
  }
  supplierOptions(query: SelectDialogQuery): Observable<PagedResult<RfqSupplierOption>> {
    return this.http.get<PagedResult<RfqSupplierOption>>('/api/v1/pur/rfqs/supplier-options',
      { params: this.optionParams(query) });
  }
  sourceOptions(query: SelectDialogQuery, itemId: number | null): Observable<PagedResult<RfqSourceOption>> {
    let params = this.optionParams(query);
    if (itemId) params = params.set('itemId', itemId);
    return this.http.get<PagedResult<RfqSourceOption>>('/api/v1/pur/rfqs/source-options', { params });
  }
  private optionParams(query: SelectDialogQuery): HttpParams {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword.trim()) params = params.set('keyword', query.keyword.trim());
    return params;
  }
  create(draft: RfqDraft): Observable<RfqDetail> {
    return this.http.post<RfqDetail>('/api/v1/pur/rfqs', this.body(draft));
  }
  update(id: number, version: number, draft: RfqDraft): Observable<RfqDetail> {
    return this.http.put<RfqDetail>(`/api/v1/pur/rfqs/${id}`, this.body(draft),
      { params: new HttpParams().set('version', version) });
  }
  send(id: number, version: number): Observable<RfqDetail> {
    return this.http.post<RfqDetail>(`/api/v1/pur/rfqs/${id}/send`, null,
      { params: new HttpParams().set('version', version) });
  }
  close(id: number, version: number): Observable<RfqDetail> {
    return this.http.post<RfqDetail>(`/api/v1/pur/rfqs/${id}/close`, null,
      { params: new HttpParams().set('version', version) });
  }
  private body(draft: RfqDraft) {
    return { businessDate: draft.businessDate, replyDeadline: draft.replyDeadline,
      remark: draft.remark.trim() || null, supplierIds: draft.supplierIds,
      lines: draft.lines.map((line, index) => ({ lineNo: index + 1, itemId: line.itemId,
        requisitionLineId: line.requisitionLineId, requiredDate: line.requiredDate,
        quantity: line.quantity.trim(), remark: line.remark.trim() || null })) };
  }
}
