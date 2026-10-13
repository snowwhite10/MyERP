import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export interface StockFilters {
  page: number; pageSize: number; itemId?: number; warehouseId?: number; locationId?: number;
  from?: string; to?: string; direction?: 'In' | 'Out';
}
export interface BalanceRow { itemId: number; warehouseId: number; locationId: number | null; quantity: number }
export interface MovementRow extends BalanceRow {
  id: number; businessDate: string; postedAt: string; sourceType: string; sourceId: number;
  sourceLineNo: number; direction: 'In' | 'Out'; itemCode: string; warehouseCode: string;
  locationCode: string | null; unitCost: number | null;
}

/** Exported text is treated as data by spreadsheet applications; numbers (including negatives) stay numeric. */
export function stockCsvCell(value: string | number | null): string {
  let content = String(value ?? '');
  if (typeof value !== 'number' && /^\s*[=+\-@]/.test(content)) content = "'" + content;
  return '"' + content.replaceAll('"', '""') + '"';
}

@Injectable({ providedIn: 'root' })
export class StockQueryService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, ['COMMON.PERMISSION.DENIED']); }
  balances(filters: StockFilters): Observable<PagedResult<BalanceRow>> {
    return this.http.get<PagedResult<BalanceRow>>('/api/v1/inv/balances', { params: this.params(filters) });
  }
  movements(filters: StockFilters): Observable<PagedResult<MovementRow>> {
    return this.http.get<PagedResult<MovementRow>>('/api/v1/inv/movements', { params: this.params(filters, true) });
  }
  private params(filters: StockFilters, movement = false): HttpParams {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    for (const key of ['itemId', 'warehouseId', 'locationId'] as const) {
      if (filters[key]) params = params.set(key, filters[key]);
    }
    if (movement) for (const key of ['from', 'to', 'direction'] as const) {
      if (filters[key]) params = params.set(key, filters[key]!);
    }
    return params;
  }
}
