import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const CUSTOMER_API = '/api/v1/md/customers';
const ERROR_CODES = ['MD.CUSTOMER.DUPLICATE_CODE', 'MD.CUSTOMER.NOT_FOUND',
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'] as const;
export const customerErrorKey = (error: unknown): string => apiErrorKey(error, ERROR_CODES);

export interface Customer {
  id: number;
  code: string;
  name: string;
  categoryId: number | null;
  isActive: boolean;
  version: number;
}

export interface CustomerQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  sort?: string;
}

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);

  list(query: CustomerQuery): Observable<PagedResult<Customer>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<Customer>>(CUSTOMER_API, { params });
  }

  create(body: { code: string; name: string; categoryId: number | null }): Observable<Customer> {
    return this.http.post<Customer>(CUSTOMER_API, {
      code: body.code.trim().toUpperCase(), name: body.name.trim(), categoryId: body.categoryId,
    });
  }

  update(id: number, body: { name: string; categoryId: number | null; isActive: boolean; version: number }): Observable<Customer> {
    return this.http.put<Customer>(`${CUSTOMER_API}/${id}`, { ...body, name: body.name.trim() });
  }
}
