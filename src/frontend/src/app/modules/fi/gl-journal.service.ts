import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { parseJsonWithDecimalText } from '../../shared/decimal-text';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/gl/journals';

/** Sort fields accepted by `GET /api/v1/gl/journals` (one field only). */
export const GL_JOURNAL_SORTS = ['businessDate', 'id'] as const;
export const DEFAULT_GL_JOURNAL_SORT = '-businessDate';
/** Server limits of the saved source key and account code filters (GlReadService). */
export const GL_JOURNAL_LIMITS = { sourceModule: 20, sourceType: 30, accountCode: 50 } as const;

/** Money fields kept as exact decimal text (base currency `decimal(19,4)`, BR-FI-026). */
const AMOUNT_KEYS = ['totalDebit', 'totalCredit', 'debit', 'credit'] as const;

/** Immutable Posted journal header with its saved source key (BR-FI-024/025). Amounts are exact decimal text. */
export interface GlJournal {
  id: number;
  periodId: number;
  businessDate: string;
  baseCurrency: string;
  status: 'Posted';
  totalDebit: string;
  totalCredit: string;
  sourceModule: string;
  sourceType: string;
  sourceId: number;
  sourceVersion: number;
  createdAt: string;
  createdBy: number;
}

/** Saved account snapshot and amounts of one line (BR-FI-027); exactly one side is nonzero. */
export interface GlJournalLine {
  sequence: number;
  accountId: number;
  accountCode: string;
  accountName: string;
  accountVersion: number;
  mappingVersion: number;
  debit: string;
  credit: string;
}

export interface GlJournalDetail { header: GlJournal; lines: GlJournalLine[]; }

export interface GlJournalQuery {
  page: number;
  pageSize: number;
  periodId?: number | null;
  /** Inclusive business date range. */
  from?: string | null;
  to?: string | null;
  sourceModule?: string | null;
  sourceType?: string | null;
  sourceId?: number | null;
  accountCode?: string | null;
  sort?: string;
}

const KNOWN_CODES = ['FI.GL_JOURNAL.NOT_FOUND', 'COMMON.PERMISSION.DENIED'];

/** Read-only Posted GL journal API (REQ_F4116.10.3, interface 6.22); the server scopes rows to the current company. */
@Injectable({ providedIn: 'root' })
export class GlJournalService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string {
    return apiErrorKey(error, KNOWN_CODES);
  }

  list(query: GlJournalQuery): Observable<PagedResult<GlJournal>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.periodId) params = params.set('periodId', query.periodId);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.sourceModule) params = params.set('sourceModule', query.sourceModule);
    if (query.sourceType) params = params.set('sourceType', query.sourceType);
    if (query.sourceId) params = params.set('sourceId', query.sourceId);
    if (query.accountCode) params = params.set('accountCode', query.accountCode);
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get(API, { params, responseType: 'text' }).pipe(jsonTextErrors(),
      map(text => parseJsonWithDecimalText<PagedResult<GlJournal>>(text, AMOUNT_KEYS)));
  }

  get(id: number): Observable<GlJournalDetail> {
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(jsonTextErrors(),
      map(text => parseJsonWithDecimalText<GlJournalDetail>(text, AMOUNT_KEYS)));
  }
}
