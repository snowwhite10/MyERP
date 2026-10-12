import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const PROCESS_CATEGORY_API = '/api/v1/md/process-categories';
export const PROCESS_API = '/api/v1/md/processes';
export const MAX_SORT_ORDER = 9999;
/** 標準工價：≥ 0、最多 9 位整數與 6 位小數（15 位有效數字，JSON 數字可無損往返，同 ADR-034）。 */
export const RATE_PATTERN = /^\d{1,9}(\.\d{1,6})?$/;

export interface ProcessCategory { id: number; code: string; name: string; sortOrder: number;
  processCount: number; isActive: boolean; version: number }
export interface Process { id: number; code: string; name: string; categoryId: number | null; uomId: number | null;
  standardRate: number; sortOrder: number; isActive: boolean; version: number }
export interface ProcessCategoryDraft { code: string; name: string; sortOrder: number }
/** 表單草稿；`standardRate` 保留輸入文字，送出前才轉數字。 */
export interface ProcessDraft { code: string; name: string; categoryId: number | null; uomId: number | null;
  standardRate: string; sortOrder: number }
export interface ProcessQuery { page: number; pageSize: number; keyword?: string; sort?: string; categoryId?: number | null }

export function normalizeProcessDraft(draft: ProcessDraft) {
  return { code: draft.code.trim().toUpperCase(), name: draft.name.trim(), categoryId: draft.categoryId,
    uomId: draft.uomId, standardRate: Number(draft.standardRate.trim()), sortOrder: draft.sortOrder };
}

abstract class ProcessApi<T> {
  private readonly http = inject(HttpClient);
  private readonly knownCodes: readonly string[];
  protected constructor(protected readonly url: string, prefix: string, codes: readonly string[]) {
    this.knownCodes = codes.map(x => `${prefix}.${x}`).concat(['COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  errorKey(error: unknown): string { return apiErrorKey(error, this.knownCodes); }
  list(query: ProcessQuery): Observable<PagedResult<T>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    if (query.categoryId != null) params = params.set('categoryId', query.categoryId);
    return this.http.get<PagedResult<T>>(this.url, { params });
  }
  protected post(body: object): Observable<T> { return this.http.post<T>(this.url, body); }
  protected put(id: number, body: object): Observable<T> { return this.http.put<T>(`${this.url}/${id}`, body); }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`, { params: new HttpParams().set('version', version) });
  }
}

@Injectable({ providedIn: 'root' })
export class ProcessCategoryService extends ProcessApi<ProcessCategory> {
  constructor() { super(PROCESS_CATEGORY_API, 'MD.PROCESS_CATEGORY', ['DUPLICATE_CODE', 'HAS_PROCESSES', 'NOT_FOUND']); }
  create(draft: ProcessCategoryDraft): Observable<ProcessCategory> {
    return this.post({ code: draft.code.trim().toUpperCase(), name: draft.name.trim(), sortOrder: draft.sortOrder });
  }
  update(id: number, body: { name: string; sortOrder: number; isActive: boolean; version: number }): Observable<ProcessCategory> {
    return this.put(id, { ...body, name: body.name.trim() });
  }
}

@Injectable({ providedIn: 'root' })
export class ProcessService extends ProcessApi<Process> {
  constructor() {
    super(PROCESS_API, 'MD.PROCESS', ['DUPLICATE_CODE', 'IN_USE', 'NOT_FOUND', 'INVALID_CATEGORY', 'INVALID_UOM', 'INACTIVE']);
  }
  create(draft: ProcessDraft): Observable<Process> { return this.post(normalizeProcessDraft(draft)); }
  update(id: number, draft: ProcessDraft & { isActive: boolean; version: number }): Observable<Process> {
    const { code: _code, ...rest } = normalizeProcessDraft(draft);
    return this.put(id, { ...rest, isActive: draft.isActive, version: draft.version });
  }
}
