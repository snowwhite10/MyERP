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

const KNOWN_CODES = ['FI.GL_PERIOD.NOT_FOUND', 'FI.GL_PERIOD.OVERLAP', 'FI.GL_PERIOD.EXCLUDES_POSTED',
  'FI.GL_PERIOD.CONTAINS_POSTED', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];

/** Inclusive dates the user may set; state changes only through close/reopen (BR-FI-023). */
export interface GlPeriodDates { startDate: string; endDate: string; }

/** Write response: `changed=false` means the request was valid but nothing was saved. */
export interface GlPeriodWriteResult { period: GlPeriod; changed: boolean; }

/** GL accounting period API (REQ_F4116.10.1, interfaces 6.20–6.21); the server scopes rows to the current company. */
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

  /** Creates an Open period; company and state come from the server. */
  create(dates: GlPeriodDates): Observable<GlPeriodWriteResult> {
    return this.http.post<GlPeriodWriteResult>(API, { startDate: dates.startDate, endDate: dates.endDate });
  }

  /** Date edit only; a status field would be rejected with 400. */
  update(id: number, version: number, dates: GlPeriodDates): Observable<GlPeriodWriteResult> {
    return this.http.put<GlPeriodWriteResult>(`${API}/${id}`, { version, startDate: dates.startDate, endDate: dates.endDate });
  }

  close(id: number, version: number): Observable<GlPeriodWriteResult> {
    return this.http.post<GlPeriodWriteResult>(`${API}/${id}/close`, { version });
  }

  reopen(id: number, version: number): Observable<GlPeriodWriteResult> {
    return this.http.post<GlPeriodWriteResult>(`${API}/${id}/reopen`, { version });
  }

  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`, { params: new HttpParams().set('version', version) });
  }
}
