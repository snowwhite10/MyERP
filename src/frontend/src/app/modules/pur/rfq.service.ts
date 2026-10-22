import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export type RfqStatus = 'Draft' | 'Sent' | 'Closed';
export interface RfqSummary { id: number; documentNo: string; businessDate: string; replyDeadline: string;
  ownerId: number; status: RfqStatus; lineCount: number; invitationCount: number; version: number }
export interface RfqDetail extends Omit<RfqSummary, 'lineCount' | 'invitationCount'> {
  remark: string | null; sentAt: string | null; createdAt: string;
  lines: { id: number; lineNo: number; itemId: number; requisitionLineId: number | null;
    requiredDate: string; quantity: number; remark: string | null }[];
  invitations: { id: number; supplierId: number; supplierCodeSnapshot: string | null;
    supplierNameSnapshot: string | null; sentAt: string | null }[];
}
export interface RfqQuery { page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: RfqStatus; requisitionLineId?: number; sort?: string }

@Injectable({ providedIn: 'root' })
export class RfqService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string { return apiErrorKey(error, ['PUR.RFQ.NOT_FOUND', 'COMMON.PERMISSION.DENIED']); }
  list(query: RfqQuery): Observable<PagedResult<RfqSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.requisitionLineId) params = params.set('requisitionLineId', query.requisitionLineId);
    return this.http.get<PagedResult<RfqSummary>>('/api/v1/pur/rfqs', { params });
  }
  get(id: number): Observable<RfqDetail> { return this.http.get<RfqDetail>(`/api/v1/pur/rfqs/${id}`); }
}
