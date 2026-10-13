import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { TrackingDraft, TrackingResponse, trackingBody } from './tracking';

const API = '/api/v1/inv/transfers';
export type TransferStatus = 'Draft' | 'PendingApproval' | 'Rejected' | 'Posted';
export interface TransferLine { id: number; lineNo: number; itemId: number; sourceWarehouseId: number; sourceLocationId: number | null;
  destinationWarehouseId: number; destinationLocationId: number | null; quantity: number; tracking: TrackingResponse[] }
export interface TransferSummary { id: number; documentNo: string; businessDate: string; status: TransferStatus; remark: string | null; lineCount: number; totalQuantity: number; version: number }
export interface TransferDetail extends Omit<TransferSummary, 'lineCount' | 'totalQuantity'> {
  postedAt: string | null; lines: TransferLine[]; approval: { instanceId: number; status: string } | null;
}
export interface TransferLineDraft { itemId: number | null; sourceWarehouseId: number | null; sourceLocationId: number | null;
  destinationWarehouseId: number | null; destinationLocationId: number | null; quantity: string; tracking?: TrackingDraft[] }
export interface TransferDraft { businessDate: string; remark: string; lines: TransferLineDraft[] }

const decimal6 = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
/** BR-INV-026：來源與目的的倉庫及儲位組合不可完全相同；同倉不同儲位允許。 */
export function sameBalanceKey(line: TransferLineDraft): boolean {
  return line.sourceWarehouseId === line.destinationWarehouseId && line.sourceLocationId === line.destinationLocationId;
}
export function validTransferLine(line: TransferLineDraft): boolean {
  return !!line.itemId && !!line.sourceWarehouseId && !!line.destinationWarehouseId && !sameBalanceKey(line)
    && decimal6.test(line.quantity) && Number(line.quantity) > 0;
}

@Injectable({ providedIn: 'root' })
export class TransferService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.TRANSFER.NOT_FOUND', 'INV.TRANSFER.NOT_DRAFT',
      'INV.TRANSFER.APPROVAL_STATE_CHANGED', 'INV.STOCK.INSUFFICIENT',
      'INV.TRACKING.REQUIRED', 'INV.TRACKING.MODE_MISMATCH', 'INV.LOT.INSUFFICIENT', 'INV.SERIAL.NOT_AVAILABLE',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: { page: number; pageSize: number; keyword?: string; status?: string; from?: string; to?: string; sort?: string }): Observable<PagedResult<TransferSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    return this.http.get<PagedResult<TransferSummary>>(API, { params });
  }
  get(id: number): Observable<TransferDetail> { return this.http.get<TransferDetail>(`${API}/${id}`); }
  create(draft: TransferDraft): Observable<TransferDetail> { return this.http.post<TransferDetail>(API, this.body(draft)); }
  update(id: number, version: number, draft: TransferDraft): Observable<TransferDetail> {
    return this.http.put<TransferDetail>(`${API}/${id}`, { ...this.body(draft), version });
  }
  submit(id: number, version: number): Observable<TransferDetail> {
    return this.http.post<TransferDetail>(`${API}/${id}/submit`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  private body(draft: TransferDraft) {
    return { businessDate: draft.businessDate, remark: draft.remark.trim() || null,
      lines: draft.lines.map((line, index) => ({ lineNo: index + 1, itemId: line.itemId,
        sourceWarehouseId: line.sourceWarehouseId, sourceLocationId: line.sourceLocationId,
        destinationWarehouseId: line.destinationWarehouseId, destinationLocationId: line.destinationLocationId,
        quantity: line.quantity,
        ...(line.tracking?.length ? { tracking: trackingBody(line.tracking) } : {}) })) };
  }
}
