import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';

export interface ImportFieldError { field: string; code: string }
export interface CustomerImportRow { rowNumber: number; code: string | null; name: string | null;
  status: 'Create' | 'Skip' | 'Error'; errors: ImportFieldError[] }
export interface CustomerImportResult { rows: CustomerImportRow[]; createdCount: number; skippedCount: number; errorCount: number }

@Injectable({ providedIn: 'root' })
export class CustomerImportService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/v1/md/customers/import';
  private readonly codes = ['COMMON.IMPORT.TOO_LARGE', 'COMMON.IMPORT.EMPTY_FILE', 'COMMON.IMPORT.UNSUPPORTED_FORMAT',
    'COMMON.IMPORT.INVALID_ENCODING', 'COMMON.IMPORT.INVALID_FILE', 'COMMON.IMPORT.TOO_MANY_ROWS',
    'COMMON.IMPORT.DUPLICATE_COLUMN', 'COMMON.IMPORT.MISSING_COLUMN', 'MD.CUSTOMER_IMPORT.HAS_ERRORS',
    'MD.CUSTOMER.DUPLICATE_CODE', 'COMMON.PERMISSION.DENIED'];

  errorKey(error: unknown): string { return apiErrorKey(error, this.codes); }
  template(format: 'xlsx' | 'csv'): Observable<Blob> {
    return this.http.get(`${this.url}/template`, { params: { format }, responseType: 'blob' });
  }
  preview(file: File): Observable<CustomerImportResult> { return this.http.post<CustomerImportResult>(`${this.url}/preview`, this.body(file)); }
  commit(file: File): Observable<CustomerImportResult> { return this.http.post<CustomerImportResult>(this.url, this.body(file)); }
  private body(file: File): FormData { const body = new FormData(); body.append('file', file); return body; }
}
