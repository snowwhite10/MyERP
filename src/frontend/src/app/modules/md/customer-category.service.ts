import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const CUSTOMER_CATEGORY_API = '/api/v1/md/customer-categories';
export const CATEGORY_ERROR_CODES = [
  'MD.CUSTOMER_CATEGORY.DUPLICATE_CODE',
  'MD.CUSTOMER_CATEGORY.NOT_FOUND',
  'COMMON.CONCURRENCY.CONFLICT',
  'COMMON.PERMISSION.DENIED',
] as const;

export interface CustomerCategory {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
  version: number;
}

export interface CategoryQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  sort?: string;
}

export function categoryErrorKey(error: unknown): string {
  return apiErrorKey(error, CATEGORY_ERROR_CODES);
}

@Injectable({ providedIn: 'root' })
export class CustomerCategoryService {
  private readonly http = inject(HttpClient);

  list(query: CategoryQuery): Observable<PagedResult<CustomerCategory>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<CustomerCategory>>(CUSTOMER_CATEGORY_API, { params });
  }

  create(body: { code: string; name: string }): Observable<CustomerCategory> {
    return this.http.post<CustomerCategory>(CUSTOMER_CATEGORY_API,
      { code: body.code.trim().toUpperCase(), name: body.name.trim() });
  }

  update(id: number, body: { name: string; isActive: boolean; version: number }): Observable<CustomerCategory> {
    return this.http.put<CustomerCategory>(`${CUSTOMER_CATEGORY_API}/${id}`, { ...body, name: body.name.trim() });
  }

  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${CUSTOMER_CATEGORY_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
}
