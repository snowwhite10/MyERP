import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const WAREHOUSE_API = '/api/v1/md/warehouses';
export interface Warehouse {
  id: number; code: string; name: string; address: string | null; contactName: string | null; phone: string | null;
  isActive: boolean; version: number;
}
export interface WarehouseDraft { name: string; address: string | null; contactName: string | null; phone: string | null }
/** 倉庫內的儲位，只有一層（ADR-036）。 */
export interface StorageLocation { id: number; warehouseId: number; code: string; name: string; isActive: boolean; version: number }
export interface ListQuery { page: number; pageSize: number; keyword?: string; sort?: string }

@Injectable({ providedIn: 'root' })
export class WarehouseService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.WAREHOUSE.DUPLICATE_CODE', 'MD.WAREHOUSE.NOT_FOUND', 'MD.WAREHOUSE.HAS_LOCATIONS',
      'MD.STORAGE_LOCATION.DUPLICATE_CODE', 'MD.STORAGE_LOCATION.NOT_FOUND', 'MD.STORAGE_LOCATION.WAREHOUSE_INACTIVE',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: ListQuery): Observable<PagedResult<Warehouse>> {
    return this.http.get<PagedResult<Warehouse>>(WAREHOUSE_API, { params: this.params(query) });
  }
  create(body: WarehouseDraft & { code: string }): Observable<Warehouse> {
    return this.http.post<Warehouse>(WAREHOUSE_API, { ...this.clean(body), code: body.code.trim().toUpperCase() });
  }
  update(id: number, body: WarehouseDraft & { isActive: boolean; version: number }): Observable<Warehouse> {
    return this.http.put<Warehouse>(`${WAREHOUSE_API}/${id}`, { ...this.clean(body), isActive: body.isActive, version: body.version });
  }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${WAREHOUSE_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  listLocations(warehouseId: number, query: ListQuery): Observable<PagedResult<StorageLocation>> {
    return this.http.get<PagedResult<StorageLocation>>(`${WAREHOUSE_API}/${warehouseId}/locations`, { params: this.params(query) });
  }
  createLocation(warehouseId: number, body: { code: string; name: string }): Observable<StorageLocation> {
    return this.http.post<StorageLocation>(`${WAREHOUSE_API}/${warehouseId}/locations`,
      { code: body.code.trim().toUpperCase(), name: body.name.trim() });
  }
  updateLocation(warehouseId: number, id: number, body: { name: string; isActive: boolean; version: number }): Observable<StorageLocation> {
    return this.http.put<StorageLocation>(`${WAREHOUSE_API}/${warehouseId}/locations/${id}`,
      { name: body.name.trim(), isActive: body.isActive, version: body.version });
  }
  removeLocation(warehouseId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${WAREHOUSE_API}/${warehouseId}/locations/${id}`, { params: new HttpParams().set('version', version) });
  }
  private params(query: ListQuery): HttpParams {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return params;
  }
  private clean(body: WarehouseDraft): WarehouseDraft {
    return { name: body.name.trim(), address: body.address?.trim() || null,
      contactName: body.contactName?.trim() || null, phone: body.phone?.trim() || null };
  }
}
