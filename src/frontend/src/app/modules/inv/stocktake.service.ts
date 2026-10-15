import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { INVENTORY_REFERENCE_ERROR_CODES } from './selection-options.service';

const API = '/api/v1/inv/stocktakes';
export type StocktakeStatus = 'Draft' | 'PendingApproval' | 'Rejected' | 'Posted';
export interface StocktakeScope { warehouseIds: number[]; categoryIds: number[]; itemIds: number[] }
export interface StocktakeTracking { id: number; lotNo: string | null; serialNo: string | null; bookQuantity: number; countedQuantity: number | null }
export interface StocktakeLine { id: number; lineNo: number; itemId: number; warehouseId: number; locationId: number | null;
  trackingMode: 'None' | 'Lot' | 'Serial'; bookQuantity: number; countedQuantity: number | null; isAdded: boolean;
  recountRequired: boolean; tracking: StocktakeTracking[] }
export interface StocktakeDetail { id: number; documentNo: string; businessDate: string; status: StocktakeStatus;
  remark: string | null; scope: StocktakeScope; snapshotAt: string; postedAt: string | null; version: number;
  lines: StocktakeLine[]; approval: { instanceId: number; status: string } | null }
export interface StocktakeSummary { id: number; documentNo: string; businessDate: string; status: StocktakeStatus;
  remark: string | null; warehouseCount: number; lineCount: number; countedLineCount: number; version: number }
export interface StocktakeVarianceTracking { lotNo: string | null; serialNo: string | null; bookQuantity: number;
  countedQuantity: number | null; difference: number | null }
export interface StocktakeVarianceLine { lineId: number; lineNo: number; itemId: number; warehouseId: number;
  locationId: number | null; trackingMode: 'None' | 'Lot' | 'Serial'; bookQuantity: number;
  countedQuantity: number | null; difference: number | null; gainQuantity: number; lossQuantity: number;
  type: 'Gain' | 'Loss' | 'Match' | 'Uncounted'; isAdded: boolean; tracking: StocktakeVarianceTracking[] }
export interface StocktakeVariance { id: number; documentNo: string; businessDate: string; status: StocktakeStatus;
  lines: StocktakeVarianceLine[]; gainQuantity: number; lossQuantity: number; gainLineCount: number;
  lossLineCount: number; matchLineCount: number; uncountedLineCount: number }
export interface StocktakeTrackingCount { lotNo: string | null; serialNo: string | null; countedQuantity: string | null }
export interface StocktakeCountLine { lineId?: number; itemId?: number; warehouseId?: number; locationId?: number | null;
  countedQuantity: string | null; tracking: StocktakeTrackingCount[] }
export interface StocktakeCountDraft { businessDate: string; remark: string; lines: StocktakeCountLine[] }

const decimal6 = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
export function validCount(value: string | null): boolean { return value !== null && decimal6.test(value); }
function units(value: string): bigint {
  const [whole, fraction = ''] = value.split('.');
  return BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0'));
}
/** Display only. Business decisions and posting remain on the server. */
export function decimalDifference(counted: string, book: string): string {
  const difference = units(counted) - units(book);
  const sign = difference < 0n ? '-' : '';
  const absolute = difference < 0n ? -difference : difference;
  const fraction = String(absolute % 1_000_000n).padStart(6, '0').replace(/0+$/, '');
  return `${sign}${absolute / 1_000_000n}${fraction ? '.' + fraction : ''}`;
}

@Injectable({ providedIn: 'root' })
export class StocktakeService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.STOCKTAKE.NOT_FOUND', 'INV.STOCKTAKE.NOT_DRAFT',
      'INV.STOCKTAKE.COUNT_MISSING', 'INV.STOCKTAKE.SNAPSHOT_STALE', 'INV.STOCKTAKE.TOO_MANY_LINES',
      'INV.STOCKTAKE.SCOPE_IN_USE', 'INV.STOCKTAKE.LINE_OUT_OF_SCOPE', 'INV.STOCKTAKE.APPROVAL_STATE_CHANGED', 'INV.STOCK.INSUFFICIENT',
      'INV.TRACKING.REQUIRED', 'INV.TRACKING.MODE_MISMATCH', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED',
      'MD.ITEM_CATEGORY.NOT_FOUND', 'MD.ITEM_CATEGORY.INACTIVE', ...INVENTORY_REFERENCE_ERROR_CODES]);
  }
  list(query: { page: number; pageSize: number; keyword?: string; status?: string; from?: string; to?: string }): Observable<PagedResult<StocktakeSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'status', 'from', 'to'] as const) {
      const value = query[key]?.trim(); if (value) params = params.set(key, value);
    }
    return this.http.get<PagedResult<StocktakeSummary>>(API, { params });
  }
  get(id: number): Observable<StocktakeDetail> { return this.http.get<StocktakeDetail>(`${API}/${id}`); }
  variance(id: number, differenceOnly: boolean): Observable<StocktakeVariance> {
    return this.http.get<StocktakeVariance>(`${API}/${id}/variance`,
      { params: new HttpParams().set('differenceOnly', differenceOnly) });
  }
  create(draft: { businessDate: string; remark: string; scope: StocktakeScope }): Observable<StocktakeDetail> {
    return this.http.post<StocktakeDetail>(API, { ...draft, remark: draft.remark.trim() || null });
  }
  update(id: number, version: number, draft: StocktakeCountDraft): Observable<StocktakeDetail> {
    return this.http.put<StocktakeDetail>(`${API}/${id}`, { businessDate: draft.businessDate,
      remark: draft.remark.trim() || null, version, lines: draft.lines.map(line => ({ ...line,
        tracking: line.tracking.map(t => ({ ...t, lotNo: t.lotNo?.trim() || null, serialNo: t.serialNo?.trim() || null })) })) });
  }
  refresh(id: number, version: number): Observable<StocktakeDetail> {
    return this.http.post<StocktakeDetail>(`${API}/${id}/refresh-snapshot`, { version });
  }
  submit(id: number, version: number): Observable<StocktakeDetail> {
    return this.http.post<StocktakeDetail>(`${API}/${id}/submit`, { version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
}
