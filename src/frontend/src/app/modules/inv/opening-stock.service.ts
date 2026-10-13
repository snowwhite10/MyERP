import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { TrackingDraft, TrackingResponse, trackingBody } from './tracking';

const API = '/api/v1/inv/opening-stocks';
export interface OpeningLine { id: number; lineNo: number; itemId: number; warehouseId: number; locationId: number | null; quantity: number; unitCost: number; amount: number; tracking: TrackingResponse[] }
export interface OpeningSummary { id: number; requestKey: string; openingDate: string; remark: string | null; lineCount: number; totalQuantity: number; totalAmount: number; postedAt: string; createdAt: string }
export interface OpeningDetail { id: number; requestKey: string; openingDate: string; remark: string | null; totalAmount: number; postedAt: string; createdAt: string; replayed: boolean; lines: OpeningLine[] }
export interface OpeningLineError { field: string; code: string }
export interface OpeningPreview { lines: { lineNo: number; amount: number; errors: OpeningLineError[] }[]; errorCount: number; totalQuantity: number; totalAmount: number }
export interface OpeningLineDraft { itemId: number | null; warehouseId: number | null; locationId: number | null; quantity: string; unitCost: string; tracking?: TrackingDraft[] }
export interface OpeningDraft { requestKey: string; openingDate: string; remark: string; lines: OpeningLineDraft[] }
export interface OpeningFilters { page: number; pageSize: number; from?: string; to?: string; itemId?: number; warehouseId?: number; keyword?: string }

/** 逐行錯誤碼（MD.* 與 INV.OPENING_STOCK.*）轉成 errors.* 三語 key；未知碼退回通用驗證訊息。 */
export const OPENING_LINE_CODES = ['MD.ITEM.NOT_FOUND', 'MD.ITEM.INACTIVE', 'MD.WAREHOUSE.NOT_FOUND', 'MD.WAREHOUSE.INACTIVE',
  'MD.STORAGE_LOCATION.NOT_FOUND', 'MD.STORAGE_LOCATION.INACTIVE', 'INV.OPENING_STOCK.DUPLICATE_IN_BATCH',
  'INV.OPENING_STOCK.DUPLICATE', 'INV.OPENING_STOCK.AFTER_MOVEMENT'] as const;
// Tracking mode is checked during preview, so these codes must remain visible per line.
export const OPENING_TRACKING_CODES = ['INV.TRACKING.REQUIRED', 'INV.TRACKING.MODE_MISMATCH', 'INV.LOT.ATTRIBUTE_MISMATCH',
  'INV.SERIAL.IN_STOCK'] as const;
export function openingLineErrorKey(code: string): string {
  return ([...OPENING_LINE_CODES, ...OPENING_TRACKING_CODES] as readonly string[]).includes(code) ? `errors.${code}` : 'errors.COMMON.REQUEST.VALIDATION_FAILED';
}

const decimal6 = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
export function validOpeningLine(line: OpeningLineDraft): boolean {
  return !!line.itemId && !!line.warehouseId && decimal6.test(line.quantity.trim()) && Number(line.quantity) > 0 && decimal6.test(line.unitCost.trim());
}

/** 每批一個請求鍵；提交失敗重試沿用同一鍵，伺服器才能辨認重送（ADR-059）。 */
export function newRequestKey(): string { return crypto.randomUUID(); }

@Injectable({ providedIn: 'root' })
export class OpeningStockService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.OPENING_STOCK.NOT_FOUND', 'INV.OPENING_STOCK.REQUEST_CONFLICT',
      'INV.OPENING_STOCK.HAS_ERRORS', 'COMMON.PERMISSION.DENIED']);
  }
  list(filters: OpeningFilters): Observable<PagedResult<OpeningSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    for (const key of ['from', 'to', 'itemId', 'warehouseId', 'keyword'] as const) {
      const value = filters[key];
      if (typeof value === 'number' ? value > 0 : value?.trim()) params = params.set(key, typeof value === 'string' ? value.trim() : value!);
    }
    return this.http.get<PagedResult<OpeningSummary>>(API, { params });
  }
  get(id: number): Observable<OpeningDetail> { return this.http.get<OpeningDetail>(`${API}/${id}`); }
  preview(draft: OpeningDraft): Observable<OpeningPreview> { return this.http.post<OpeningPreview>(`${API}/preview`, this.body(draft)); }
  /** 201 為新批次；200 為相同請求重送（replayed = true），兩者都視為成功。 */
  submit(draft: OpeningDraft): Observable<OpeningDetail> {
    return this.http.post<OpeningDetail>(API, this.body(draft), { observe: 'response' })
      .pipe(map((response: HttpResponse<OpeningDetail>) => ({ ...response.body!, replayed: response.status === 200 || !!response.body?.replayed })));
  }
  private body(draft: OpeningDraft) {
    return { requestKey: draft.requestKey, openingDate: draft.openingDate, remark: draft.remark.trim() || null,
      lines: draft.lines.map((line, index) => ({ lineNo: index + 1, itemId: line.itemId, warehouseId: line.warehouseId,
        locationId: line.locationId, quantity: line.quantity.trim(), unitCost: line.unitCost.trim(),
        ...(line.tracking ? { tracking: trackingBody(line.tracking) } : {}) })) };
  }
}
