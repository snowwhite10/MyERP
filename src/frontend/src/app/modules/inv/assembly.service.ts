import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { InventorySavedReferences, INVENTORY_REFERENCE_ERROR_CODES } from './selection-options.service';
import { TrackingDraft, TrackingResponse, trackingBody } from './tracking';

const API = '/api/v1/inv/assemblies';
export type AssemblyStatus = 'Draft' | 'PendingApproval' | 'Rejected' | 'Posted';
export interface AssemblySummary { id: number; documentNo: string; businessDate: string; status: AssemblyStatus;
  finishedItemId: number; finishedItemCode: string | null; finishedItemName: string | null;
  finishedQuantity: number; componentCount: number; version: number }
export interface AssemblyLine { id: number; lineNo: number; role: 'Finished' | 'Component'; itemId: number;
  warehouseId: number; locationId: number | null; quantity: number; postedUnitCost: number | null; tracking: TrackingResponse[] }
export interface AssemblyDetail { id: number; documentNo: string; businessDate: string; status: AssemblyStatus;
  remark: string | null; postedAt: string | null; createdAt: string; version: number; lines: AssemblyLine[];
  approval: { instanceId: number; status: string; currentStepNo: number | null; stepCount: number } | null }
export interface AssemblyLineDraft { itemId: number | null; warehouseId: number | null; locationId: number | null;
  quantity: string; tracking?: TrackingDraft[] }
export interface AssemblyDraft { businessDate: string; remark: string; finished: AssemblyLineDraft; components: AssemblyLineDraft[] }

const decimal6 = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
function validLine(line: AssemblyLineDraft): boolean {
  return !!line.itemId && !!line.warehouseId && decimal6.test(line.quantity) && Number(line.quantity) > 0;
}
export function validAssemblyDraft(draft: AssemblyDraft): boolean {
  return !!draft.businessDate && draft.remark.length <= 1000 && validLine(draft.finished)
    && draft.components.length > 0 && draft.components.length <= 999
    && draft.components.every(line => validLine(line) && line.itemId !== draft.finished.itemId);
}

@Injectable({ providedIn: 'root' })
export class AssemblyService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.ASSEMBLY.NOT_FOUND', 'INV.ASSEMBLY.NOT_DRAFT',
      'INV.ASSEMBLY.APPROVAL_STATE_CHANGED', 'INV.ASSEMBLY.COST_UNKNOWN',
      'INV.ASSEMBLY.COST_PRECISION', 'INV.ASSEMBLY.COST_OVERFLOW',
      'INV.STOCK.INSUFFICIENT', 'INV.TRACKING.REQUIRED', 'INV.TRACKING.MODE_MISMATCH',
      'INV.LOT.INSUFFICIENT', 'INV.SERIAL.IN_STOCK', 'INV.SERIAL.NOT_AVAILABLE',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED', ...INVENTORY_REFERENCE_ERROR_CODES]);
  }
  list(query: { page: number; pageSize: number; keyword?: string; status?: string; finishedItemId?: number;
    from?: string; to?: string; sort?: string }): Observable<PagedResult<AssemblySummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim(); if (value) params = params.set(key, value);
    }
    if (query.finishedItemId) params = params.set('finishedItemId', query.finishedItemId);
    return this.http.get<PagedResult<AssemblySummary>>(API, { params });
  }
  get(id: number): Observable<AssemblyDetail> { return this.http.get<AssemblyDetail>(`${API}/${id}`); }
  savedReferences(id: number): Observable<InventorySavedReferences> {
    return this.http.get<InventorySavedReferences>(`${API}/${id}/saved-references`);
  }
  create(draft: AssemblyDraft): Observable<AssemblyDetail> { return this.http.post<AssemblyDetail>(API, this.body(draft)); }
  update(id: number, version: number, draft: AssemblyDraft): Observable<AssemblyDetail> {
    return this.http.put<AssemblyDetail>(`${API}/${id}`, { ...this.body(draft), version });
  }
  submit(id: number, version: number): Observable<AssemblyDetail> {
    return this.http.post<AssemblyDetail>(`${API}/${id}/submit`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  private body(draft: AssemblyDraft) {
    const line = (item: AssemblyLineDraft, lineNo: number) => ({ lineNo, itemId: item.itemId,
      warehouseId: item.warehouseId, locationId: item.locationId, quantity: item.quantity,
      tracking: trackingBody(item.tracking) });
    return { businessDate: draft.businessDate, remark: draft.remark.trim() || null,
      finished: line(draft.finished, 1), components: draft.components.map((item, index) => line(item, index + 2)) };
  }
}
