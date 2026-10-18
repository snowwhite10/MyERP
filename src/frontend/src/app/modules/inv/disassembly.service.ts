import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { InventorySavedReferences, INVENTORY_REFERENCE_ERROR_CODES } from './selection-options.service';
import { TrackingDraft, TrackingResponse, trackingBody } from './tracking';

const API = '/api/v1/inv/disassemblies';
export type DisassemblyStatus = 'Draft' | 'PendingApproval' | 'Rejected' | 'Posted';
export interface DisassemblySummary { id: number; documentNo: string; businessDate: string; status: DisassemblyStatus;
  finishedItemId: number; finishedItemCode: string | null; finishedItemName: string | null;
  finishedQuantity: number; componentCount: number; version: number }
export interface DisassemblyLine { id: number; lineNo: number; role: 'Finished' | 'Component'; itemId: number;
  warehouseId: number; locationId: number | null; quantity: number; allocationWeight: number | null; postedUnitCost: number | null; tracking: TrackingResponse[] }
export interface DisassemblyDetail { id: number; documentNo: string; businessDate: string; status: DisassemblyStatus;
  remark: string | null; postedAt: string | null; createdAt: string; version: number; lines: DisassemblyLine[];
  approval: { instanceId: number; status: string; currentStepNo: number | null; stepCount: number } | null;
  approvalHistory?: { instanceId: number; status: 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'; submittedAt: string;
    steps: { stepNo: number; name: string; status: string; actedBy: number | null;
      actedAt: string | null; comment: string | null }[] }[] }
export interface DisassemblyLineDraft { itemId: number | null; warehouseId: number | null; locationId: number | null;
  quantity: string; allocationWeight?: string; tracking?: TrackingDraft[] }
export interface DisassemblyDraft { businessDate: string; remark: string; finished: DisassemblyLineDraft; components: DisassemblyLineDraft[] }

const decimal6 = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
function validBusinessDecimal(value: string): boolean {
  if (!decimal6.test(value)) return false;
  const [integer, fraction = ''] = value.split('.');
  return integer.length <= 13 && (integer !== '0' || /[1-9]/.test(fraction));
}
function validLine(line: DisassemblyLineDraft): boolean {
  return !!line.itemId && !!line.warehouseId && validBusinessDecimal(line.quantity);
}
export function validDisassemblyDraft(draft: DisassemblyDraft): boolean {
  return !!draft.businessDate && draft.remark.length <= 1000 && validLine(draft.finished)
    && draft.components.length > 0 && draft.components.length <= 999
    && draft.components.every(line => validLine(line) && line.itemId !== draft.finished.itemId
      && !!line.allocationWeight && validBusinessDecimal(line.allocationWeight));
}

@Injectable({ providedIn: 'root' })
export class DisassemblyService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.DISASSEMBLY.NOT_FOUND', 'INV.DISASSEMBLY.NOT_DRAFT',
      'INV.DISASSEMBLY.APPROVAL_STATE_CHANGED', 'INV.DISASSEMBLY.COST_UNKNOWN',
      'INV.DISASSEMBLY.COST_PRECISION', 'INV.DISASSEMBLY.COST_OVERFLOW',
      'INV.STOCK.INSUFFICIENT', 'INV.TRACKING.REQUIRED', 'INV.TRACKING.MODE_MISMATCH',
      'INV.LOT.INSUFFICIENT', 'INV.SERIAL.IN_STOCK', 'INV.SERIAL.NOT_AVAILABLE',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED', ...INVENTORY_REFERENCE_ERROR_CODES]);
  }
  list(query: { page: number; pageSize: number; keyword?: string; status?: string; finishedItemId?: number;
    from?: string; to?: string; sort?: string }): Observable<PagedResult<DisassemblySummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to', 'sort'] as const) {
      const value = query[key]?.trim(); if (value) params = params.set(key, value);
    }
    if (query.finishedItemId) params = params.set('finishedItemId', query.finishedItemId);
    return this.http.get<PagedResult<DisassemblySummary>>(API, { params });
  }
  get(id: number): Observable<DisassemblyDetail> { return this.http.get<DisassemblyDetail>(`${API}/${id}`); }
  savedReferences(id: number): Observable<InventorySavedReferences> {
    return this.http.get<InventorySavedReferences>(`${API}/${id}/saved-references`);
  }
  create(draft: DisassemblyDraft): Observable<DisassemblyDetail> { return this.http.post<DisassemblyDetail>(API, this.body(draft)); }
  update(id: number, version: number, draft: DisassemblyDraft): Observable<DisassemblyDetail> {
    return this.http.put<DisassemblyDetail>(`${API}/${id}`, { ...this.body(draft), version });
  }
  submit(id: number, version: number): Observable<DisassemblyDetail> {
    return this.http.post<DisassemblyDetail>(`${API}/${id}/submit`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  private body(draft: DisassemblyDraft) {
    const line = (item: DisassemblyLineDraft, lineNo: number) => ({ lineNo, itemId: item.itemId,
      warehouseId: item.warehouseId, locationId: item.locationId, quantity: item.quantity,
      allocationWeight: item.allocationWeight ?? null,
      tracking: trackingBody(item.tracking) });
    return { businessDate: draft.businessDate, remark: draft.remark.trim() || null,
      finished: line(draft.finished, 1), components: draft.components.map((item, index) => line(item, index + 2)) };
  }
}
