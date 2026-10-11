import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const ITEM_API = '/api/v1/md/items';
export interface Item {
  id: number; code: string; name: string; specification: string | null;
  baseUomId: number; brandId: number | null; categoryId: number | null;
  isActive: boolean; version: number;
}
export interface ItemDraft {
  name: string; specification: string | null; baseUomId: number;
  brandId: number | null; categoryId: number | null;
}

@Injectable({ providedIn: 'root' })
export class ItemService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.ITEM.DUPLICATE_CODE', 'MD.ITEM.NOT_FOUND',
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  list(query: { page: number; pageSize: number; keyword?: string; sort?: string }): Observable<PagedResult<Item>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<Item>>(ITEM_API, { params });
  }
  create(body: ItemDraft & { code: string }): Observable<Item> {
    return this.http.post<Item>(ITEM_API, { ...this.clean(body), code: body.code.trim().toUpperCase() });
  }
  update(id: number, body: ItemDraft & { isActive: boolean; version: number }): Observable<Item> {
    return this.http.put<Item>(`${ITEM_API}/${id}`, { ...this.clean(body), isActive: body.isActive, version: body.version });
  }
  private clean(body: ItemDraft): ItemDraft {
    return { name: body.name.trim(), specification: body.specification?.trim() || null,
      baseUomId: body.baseUomId, brandId: body.brandId, categoryId: body.categoryId };
  }
}
