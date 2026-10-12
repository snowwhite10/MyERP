import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PartnerKind } from './business-partner.service';

export interface ImportFieldError { field: string; code: string }
export interface PartnerImportRow { rowNumber: number; code: string | null; name: string | null;
  status: 'Create' | 'Skip' | 'Error'; errors: ImportFieldError[] }
export interface PartnerImportResult { rows: PartnerImportRow[]; createdCount: number; skippedCount: number; errorCount: number }

const fileCodes = ['COMMON.IMPORT.TOO_LARGE', 'COMMON.IMPORT.EMPTY_FILE', 'COMMON.IMPORT.UNSUPPORTED_FORMAT',
  'COMMON.IMPORT.INVALID_ENCODING', 'COMMON.IMPORT.INVALID_FILE', 'COMMON.IMPORT.TOO_MANY_ROWS',
  'COMMON.IMPORT.DUPLICATE_COLUMN', 'COMMON.IMPORT.MISSING_COLUMN', 'COMMON.PERMISSION.DENIED'];

/** 客戶／供應商匯入 API（ADR-044）；路徑與錯誤碼前綴依往來對象種類決定。 */
@Injectable({ providedIn: 'root' })
export class PartnerImportService {
  private readonly http = inject(HttpClient);

  errorKey(kind: PartnerKind, error: unknown): string {
    const prefix = `MD.${kind.toUpperCase()}`;
    return apiErrorKey(error, [...fileCodes, `${prefix}_IMPORT.HAS_ERRORS`, `${prefix}.DUPLICATE_CODE`]);
  }
  template(kind: PartnerKind, format: 'xlsx' | 'csv'): Observable<Blob> {
    return this.http.get(`${this.url(kind)}/template`, { params: { format }, responseType: 'blob' });
  }
  preview(kind: PartnerKind, file: File): Observable<PartnerImportResult> {
    return this.http.post<PartnerImportResult>(`${this.url(kind)}/preview`, this.body(file));
  }
  commit(kind: PartnerKind, file: File): Observable<PartnerImportResult> {
    return this.http.post<PartnerImportResult>(this.url(kind), this.body(file));
  }
  private url(kind: PartnerKind): string { return `/api/v1/md/${kind}s/import`; }
  private body(file: File): FormData { const body = new FormData(); body.append('file', file); return body; }
}
