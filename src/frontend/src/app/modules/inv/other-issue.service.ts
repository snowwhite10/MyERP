import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/inv/other-issues';
export type IssueStatus = 'Draft' | 'PendingApproval' | 'Rejected' | 'Posted';
export interface IssueLine { id: number; lineNo: number; itemId: number; warehouseId: number; locationId: number | null; quantity: number; unitCost: number; amount: number }
export interface IssueSummary { id: number; documentNo: string; businessDate: string; status: IssueStatus; remark: string | null; lineCount: number; totalQuantity: number; totalAmount: number; version: number }
export interface IssueDetail extends IssueSummary { lines: IssueLine[]; approval: { instanceId: number; status: string } | null }
export interface IssueLineDraft { itemId: number | null; warehouseId: number | null; locationId: number | null; quantity: string; unitCost: string }
export interface IssueDraft { businessDate: string; remark: string; lines: IssueLineDraft[] }

const decimal6 = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
export function validIssueLine(line: IssueLineDraft): boolean {
  return !!line.itemId && !!line.warehouseId && decimal6.test(line.quantity) && Number(line.quantity) > 0 && decimal6.test(line.unitCost);
}

@Injectable({ providedIn: 'root' })
export class OtherIssueService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.OTHER_ISSUE.NOT_FOUND', 'INV.OTHER_ISSUE.NOT_DRAFT',
      'INV.OTHER_ISSUE.APPROVAL_STATE_CHANGED', 'INV.STOCK.INSUFFICIENT',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: { page: number; pageSize: number; keyword?: string; status?: string; from?: string; to?: string; sort?: string }): Observable<PagedResult<IssueSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    return this.http.get<PagedResult<IssueSummary>>(API, { params });
  }
  get(id: number): Observable<IssueDetail> { return this.http.get<IssueDetail>(`${API}/${id}`); }
  create(draft: IssueDraft): Observable<IssueDetail> { return this.http.post<IssueDetail>(API, this.body(draft)); }
  update(id: number, version: number, draft: IssueDraft): Observable<IssueDetail> {
    return this.http.put<IssueDetail>(`${API}/${id}`, { ...this.body(draft), version });
  }
  submit(id: number, version: number): Observable<IssueDetail> {
    return this.http.post<IssueDetail>(`${API}/${id}/submit`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  private body(draft: IssueDraft) {
    return { businessDate: draft.businessDate, remark: draft.remark.trim() || null,
      lines: draft.lines.map((line, index) => ({ lineNo: index + 1, itemId: line.itemId,
        warehouseId: line.warehouseId, locationId: line.locationId,
        quantity: line.quantity, unitCost: line.unitCost })) };
  }
}
