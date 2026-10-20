import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../../shared/list-page/table-query';
import { apiErrorKey } from '../../core/http/api-error';
import { SelectDialogQuery } from '../../shared/dialogs/select-dialog';

const API = '/api/v1/pur/requisitions';
export type RequisitionStatus = 'Draft' | 'PendingApproval' | 'Rejected' | 'Approved' | 'Closed';
export interface RequisitionSummary { id: number; documentNo: string; businessDate: string;
  requesterId: number; status: RequisitionStatus; lineCount: number; version: number;
  branchId: number | null; departmentId: number | null }
export interface RequisitionLine { id: number; lineNo: number; itemId: number; requiredDate: string;
  quantity: number; orderedQuantity: number; remark: string | null }
export interface RequisitionApprovalHistory { instanceId: number; status: string; submittedAt: string;
  steps: { stepNo: number; name: string; status: string; actedBy: number | null;
    actedAt: string | null; comment: string | null }[] }
export interface RequisitionDetail extends RequisitionSummary { remark: string | null; createdAt: string;
  lines: RequisitionLine[]; approvalHistory: RequisitionApprovalHistory[] }
export interface RequisitionLineDraft { itemId: number | null; requiredDate: string; quantity: string; remark: string }
export interface RequisitionDraft { businessDate: string; remark: string; lines: RequisitionLineDraft[] }
export interface RequisitionSubmission { requisition: RequisitionDetail; approval: {
  instanceId: number; status: string; currentStepNo: number | null; stepCount: number } }
export interface RequisitionQuery { page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: RequisitionStatus; requesterId?: number; sort?: string }
export interface RequisitionItemOption { id: number; code: string; name: string; isActive: boolean }

export const requisitionQuantityPattern = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;
export function validRequisitionDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  if (year < 1) return false;
  const date = new Date(0);
  date.setUTCFullYear(year, month - 1, day);
  return date.getUTCFullYear() === year && date.getUTCMonth() + 1 === month && date.getUTCDate() === day;
}
export function validRequisitionDraft(draft: RequisitionDraft): boolean {
  return validRequisitionDate(draft.businessDate) && draft.remark.trim().length <= 500
    && draft.lines.length >= 1 && draft.lines.length <= 1000
    && draft.lines.every(line => Number.isSafeInteger(line.itemId) && (line.itemId ?? 0) > 0
      && validRequisitionDate(line.requiredDate) && line.requiredDate >= draft.businessDate
      && line.remark.trim().length <= 500 && requisitionQuantityPattern.test(line.quantity.trim())
      && /[1-9]/.test(line.quantity));
}

@Injectable({ providedIn: 'root' })
export class RequisitionService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, [
    'PUR.REQUISITION.NOT_FOUND', 'PUR.REQUISITION.NOT_SUBMITTABLE', 'PUR.REQUISITION.NOT_EDITABLE',
    'PUR.REQUISITION.NOT_DELETABLE', 'PUR.REQUISITION.EMPTY', 'PUR.REQUISITION.APPROVAL_STATE_CHANGED',
    'MD.ITEM.INACTIVE', 'MD.ITEM.NOT_FOUND',
    'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']); }
  itemOptions(query: SelectDialogQuery): Observable<PagedResult<RequisitionItemOption>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword.trim()) params = params.set('keyword', query.keyword.trim());
    return this.http.get<PagedResult<RequisitionItemOption>>(`${API}/item-options`, { params });
  }
  savedItemOptions(id: number): Observable<RequisitionItemOption[]> {
    return this.http.get<RequisitionItemOption[]>(`${API}/${id}/saved-item-options`);
  }
  list(query: RequisitionQuery): Observable<PagedResult<RequisitionSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.requesterId) params = params.set('requesterId', query.requesterId);
    return this.http.get<PagedResult<RequisitionSummary>>(API, { params });
  }
  get(id: number): Observable<RequisitionDetail> { return this.http.get<RequisitionDetail>(`${API}/${id}`); }
  getForEdit(id: number): Observable<RequisitionDetail> { return this.http.get<RequisitionDetail>(`${API}/${id}/edit`); }
  create(draft: RequisitionDraft): Observable<RequisitionDetail> {
    return this.http.post<RequisitionDetail>(API, this.body(draft));
  }
  update(id: number, version: number, draft: RequisitionDraft): Observable<RequisitionDetail> {
    return this.http.put<RequisitionDetail>(`${API}/${id}`, this.body(draft),
      { params: new HttpParams().set('version', version) });
  }
  submit(id: number, version: number): Observable<RequisitionSubmission> {
    return this.http.post<RequisitionSubmission>(`${API}/${id}/submit`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  private body(draft: RequisitionDraft) {
    return { businessDate: draft.businessDate, remark: draft.remark.trim() || null,
      lines: draft.lines.map((line, index) => ({ lineNo: index + 1, itemId: line.itemId,
        requiredDate: line.requiredDate, quantity: line.quantity.trim(), remark: line.remark.trim() || null })) };
  }
}
