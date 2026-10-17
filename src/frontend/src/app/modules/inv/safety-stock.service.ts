import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, TimeoutError, timeout } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { INVENTORY_REFERENCE_ERROR_CODES } from './selection-options.service';

const root = '/api/v1/inv/safety-stock-thresholds';
const quantityPattern = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;
export function validSafetyQuantity(value: string): boolean {
  return quantityPattern.test(value) && Number(value) > 0;
}
export interface SafetyStockRow {
  id: number; itemId: number; itemCode: string | null; itemName: string | null; itemActive: boolean;
  warehouseId: number; warehouseCode: string | null; warehouseName: string | null; warehouseActive: boolean;
  minimumQuantity: number; onHand: number; isLow: boolean; shortage: number; version: number;
}
export interface SafetyStockPage {
  items: SafetyStockRow[]; page: number; pageSize: number; totalCount: number; queriedAt: string;
}
export interface SafetyStockFilters {
  page: number; pageSize: number; keyword?: string; warehouseId?: number;
  lowOnly?: boolean; sort?: 'itemCode' | '-itemCode' | 'shortage' | '-shortage';
}
export interface SafetyStockSaved { id: number; itemId: number; warehouseId: number; minimumQuantity: number; version: number }

@Injectable({ providedIn: 'root' })
export class SafetyStockService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    if (error instanceof TimeoutError) return 'inv.safety.timeout';
    return apiErrorKey(error, ['INV.SAFETY_STOCK.DUPLICATE', 'INV.SAFETY_STOCK.NOT_FOUND',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED', ...INVENTORY_REFERENCE_ERROR_CODES]);
  }
  list(filters: SafetyStockFilters): Observable<SafetyStockPage> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize)
      .set('lowOnly', !!filters.lowOnly);
    if (filters.keyword?.trim()) params = params.set('keyword', filters.keyword.trim());
    if (filters.warehouseId) params = params.set('warehouseId', filters.warehouseId);
    if (filters.sort) params = params.set('sort', filters.sort);
    return this.http.get<SafetyStockPage>(root, { params }).pipe(timeout(15000));
  }
  create(itemId: number, warehouseId: number, minimumQuantity: string): Observable<SafetyStockSaved> {
    return this.http.post<SafetyStockSaved>(root, { itemId, warehouseId, minimumQuantity }).pipe(timeout(15000));
  }
  update(id: number, version: number, minimumQuantity: string): Observable<SafetyStockSaved> {
    return this.http.put<SafetyStockSaved>(`${root}/${id}`, { version, minimumQuantity }).pipe(timeout(15000));
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${root}/${id}`, { params: new HttpParams().set('version', version) }).pipe(timeout(15000));
  }
}
