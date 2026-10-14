import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export interface ValuationItemOption { id: number; code: string; name: string }

export interface InventoryValuation {
  itemId: number;
  cutoffPostedAt: string | null;
  onHand: number;
  value: number | null;
  averageUnitCost: number | null;
  unpricedQuantity: number;
  status: 'Valued' | 'Unpriced';
  movementCount: number;
  provisional: boolean;
}

@Injectable({ providedIn: 'root' })
export class ValuationService {
  private readonly http = inject(HttpClient);
  items(page: number, pageSize: number): Observable<PagedResult<ValuationItemOption>> {
    return this.http.get<PagedResult<ValuationItemOption>>('/api/v1/inv/valuations/items',
      { params: new HttpParams().set('page', page).set('pageSize', pageSize) });
  }
  item(itemId: number, cutoffPostedAt?: string): Observable<InventoryValuation> {
    const params = cutoffPostedAt ? new HttpParams().set('cutoffPostedAt', cutoffPostedAt) : new HttpParams();
    return this.http.get<InventoryValuation>(`/api/v1/inv/valuations/items/${itemId}`, { params });
  }
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['INV.VALUATION.INVALID_SCOPE', 'INV.VALUATION.NEGATIVE_POOL',
      'INV.VALUATION.TRANSFER_UNPAIRED', 'INV.VALUATION.OVERFLOW', 'MD.ITEM.NOT_FOUND', 'COMMON.PERMISSION.DENIED']);
  }
}
