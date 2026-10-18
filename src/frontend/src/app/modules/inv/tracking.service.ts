import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/inv/tracking';
export interface LotRow { id: number; itemId: number; lotNo: string; manufactureDate: string | null; expiryDate: string | null; onHand: number }
export interface SerialRow { id: number; itemId: number; serialNo: string; inStock: boolean }
export interface TrackedLocation { warehouseId: number; locationId: number | null; quantity: number }
export interface TrackedMovement { movementId: number; businessDate: string; postedAt: string; sourceType: string; sourceId: number;
  sourceLineNo: number; direction: 'In' | 'Out'; warehouseId: number; locationId: number | null; quantity: number }
export interface TrackingTrace<T> { header: T; locations: TrackedLocation[]; history: TrackedMovement[]; historyTruncated: boolean }
export interface TrackingFilters { page: number; pageSize: number; itemId?: number; keyword?: string; inStockOnly?: boolean }

@Injectable({ providedIn: 'root' })
export class TrackingService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, ['INV.LOT.NOT_FOUND', 'INV.SERIAL.NOT_FOUND', 'COMMON.PERMISSION.DENIED']); }
  private params(filters: TrackingFilters): HttpParams {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize)
      .set('inStockOnly', !!filters.inStockOnly);
    if (filters.itemId) params = params.set('itemId', filters.itemId);
    if (filters.keyword?.trim()) params = params.set('keyword', filters.keyword.trim());
    return params;
  }
  lots(filters: TrackingFilters): Observable<PagedResult<LotRow>> {
    return this.http.get<PagedResult<LotRow>>(`${API}/lots`, { params: this.params(filters) });
  }
  serials(filters: TrackingFilters): Observable<PagedResult<SerialRow>> {
    return this.http.get<PagedResult<SerialRow>>(`${API}/serials`, { params: this.params(filters) });
  }
  lot(id: number): Observable<TrackingTrace<LotRow>> { return this.http.get<TrackingTrace<LotRow>>(`${API}/lots/${id}`); }
  serial(id: number): Observable<TrackingTrace<SerialRow>> { return this.http.get<TrackingTrace<SerialRow>>(`${API}/serials/${id}`); }
  export(kind: 'lots' | 'serials', id: number): Observable<HttpResponse<Blob>> {
    return this.http.get(`${API}/${kind}/${id}/export`, { observe: 'response', responseType: 'blob' });
  }
}
