import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/gl/periods';

export type GlPeriodStatus = 'Open' | 'Closed';
export const GL_PERIOD_STATUSES: readonly GlPeriodStatus[] = ['Open', 'Closed'];
/** Sort fields accepted by `GET /api/v1/gl/periods` (one field only). */
export const GL_PERIOD_SORTS = ['startDate', 'endDate'] as const;
export const DEFAULT_GL_PERIOD_SORT = '-startDate';

/** Saved accounting period (BR-FI-023); dates are inclusive business dates `yyyy-MM-dd`. */
export interface GlPeriod {
  id: number;
  startDate: string;
  endDate: string;
  status: GlPeriodStatus;
  version: number;
  createdAt: string;
  createdBy: number;
  updatedAt: string | null;
  updatedBy: number | null;
}

export interface GlPeriodQuery {
  page: number;
  pageSize: number;
  status?: GlPeriodStatus | null;
  /** Window start: lists periods ending on or after this date. */
  from?: string | null;
  /** Window end: lists periods starting on or before this date. */
  to?: string | null;
  sort?: string;
}

const KNOWN_CODES = ['FI.GL_PERIOD.NOT_FOUND', 'COMMON.PERMISSION.DENIED'];

/** GL accounting period read API (REQ_F4116.10.1, interface 6.20); the server scopes rows to the current company. */
@Injectable({ providedIn: 'root' })
export class GlPeriodService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string {
    return apiErrorKey(error, KNOWN_CODES);
  }

  list(query: GlPeriodQuery): Observable<PagedResult<GlPeriod>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.status) params = params.set('status', query.status);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<GlPeriod>>(API, { params });
  }
}
