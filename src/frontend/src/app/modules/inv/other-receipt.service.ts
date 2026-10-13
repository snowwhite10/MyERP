import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { TrackingDraft, TrackingResponse, trackingBody } from './tracking';

const API = '/api/v1/inv/other-receipts';
export type ReceiptStatus = 'Draft' | 'PendingApproval' | 'Rejected' | 'Posted';
export interface ReceiptLine { id: number; lineNo: number; itemId: number; warehouseId: number; locationId: number | null; quantity: number; unitCost: number; amount: number; tracking: TrackingResponse[] }
export interface ReceiptSummary { id: number; documentNo: string; businessDate: string; status: ReceiptStatus; remark: string | null; lineCount: number; totalQuantity: number; totalAmount: number; version: number }
export interface ReceiptDetail extends ReceiptSummary { lines: ReceiptLine[]; approval: { instanceId: number; status: string } | null }
export interface ReceiptLineDraft { itemId: number | null; warehouseId: number | null; locationId: number | null; quantity: string; unitCost: string; tracking?: TrackingDraft[] }
export interface ReceiptDraft { businessDate: string; remark: string; lines: ReceiptLineDraft[] }

const decimal6 = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
export function validReceiptLine(line: ReceiptLineDraft): boolean {
  return !!line.itemId && !!line.warehouseId && decimal6.test(line.quantity) && Number(line.quantity) > 0 && decimal6.test(line.unitCost);
}

@Injectable({ providedIn: 'root' })
export class OtherReceiptService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.OTHER_RECEIPT.NOT_FOUND', 'INV.OTHER_RECEIPT.NOT_DRAFT',
      'INV.OTHER_RECEIPT.APPROVAL_STATE_CHANGED', 'INV.STOCK.INSUFFICIENT',
      'INV.TRACKING.REQUIRED', 'INV.TRACKING.MODE_MISMATCH', 'INV.LOT.ATTRIBUTE_MISMATCH', 'INV.SERIAL.IN_STOCK',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: { page: number; pageSize: number; keyword?: string; status?: string; from?: string; to?: string; sort?: string }): Observable<PagedResult<ReceiptSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    return this.http.get<PagedResult<ReceiptSummary>>(API, { params });
  }
  get(id: number): Observable<ReceiptDetail> { return this.http.get<ReceiptDetail>(`${API}/${id}`); }
  create(draft: ReceiptDraft): Observable<ReceiptDetail> { return this.http.post<ReceiptDetail>(API, this.body(draft)); }
  update(id: number, version: number, draft: ReceiptDraft): Observable<ReceiptDetail> {
    return this.http.put<ReceiptDetail>(`${API}/${id}`, { ...this.body(draft), version });
  }
  submit(id: number, version: number): Observable<ReceiptDetail> {
    return this.http.post<ReceiptDetail>(`${API}/${id}/submit`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  private body(draft: ReceiptDraft) {
    return { businessDate: draft.businessDate, remark: draft.remark.trim() || null,
      lines: draft.lines.map((line, index) => ({ lineNo: index + 1, itemId: line.itemId,
        warehouseId: line.warehouseId, locationId: line.locationId,
        quantity: line.quantity, unitCost: line.unitCost,
        ...(line.tracking ? { tracking: trackingBody(line.tracking) } : {}) })) };
  }
}
