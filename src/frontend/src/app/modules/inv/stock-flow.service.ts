import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface StockFlowFilters {
  from: string; to: string; itemId?: number; warehouseId?: number; locationId?: number;
  page?: number; pageSize?: number;
}
export interface StockFlowRow {
  itemId: number; warehouseId: number; locationId: number | null;
  openingQuantity: number; inboundQuantity: number; outboundQuantity: number;
  closingQuantity: number; negativeClosing: boolean;
}
export interface StockFlowPage {
  queriedAt: string; totalCount: number; items: StockFlowRow[]; page: number; pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class StockFlowService {
  private readonly http = inject(HttpClient);
  private readonly path = '/api/v1/inv/reports/stock-flow';
  private params(filters: StockFlowFilters): HttpParams {
    let params = new HttpParams().set('from', filters.from).set('to', filters.to);
    for (const key of ['itemId', 'warehouseId', 'locationId', 'page', 'pageSize'] as const) {
      const value = filters[key];
      if (value !== undefined && value !== null) params = params.set(key, value);
    }
    return params;
  }
  query(filters: StockFlowFilters): Observable<StockFlowPage> {
    return this.http.get<StockFlowPage>(this.path, { params: this.params(filters) });
  }
  export(filters: StockFlowFilters): Observable<HttpResponse<Blob>> {
    return this.http.get(this.path + '/export', { params: this.params(filters), observe: 'response', responseType: 'blob' });
  }
}
