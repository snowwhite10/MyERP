import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';

export const COMPANY_IMAGES_API = '/api/v1/sys/company-images';
export type CompanyImageKind = 'Logo' | 'Stamp';
export const COMPANY_IMAGE_KINDS: readonly CompanyImageKind[] = ['Logo', 'Stamp'];

import { IMAGE_TYPES } from '../../../shared/images/image-data-url';
export { IMAGE_TYPES, toDataUrl } from '../../../shared/images/image-data-url';

/** 與後端相同的限制（ADR-028）；用戶端只作即時提示，伺服器以檔案內容作最終檢查。 */
export const IMAGE_MAX_BYTES = 1024 * 1024;

/** 後端 `CompanyImageDetail`（不含內容）。 */
export interface CompanyImageDetail {
  kind: CompanyImageKind;
  contentType: string;
  fileName: string;
  sizeBytes: number;
  width: number;
  height: number;
  version: number;
  updatedAt: string;
}

export const COMPANY_IMAGE_ERRORS = [
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED', 'COMMON.REQUEST.VALIDATION_FAILED', 'SYS.COMPANY_IMAGE.NOT_FOUND',
  'SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE', 'SYS.COMPANY_IMAGE.TOO_LARGE', 'SYS.COMPANY_IMAGE.INVALID_DIMENSIONS',
] as const;
export const companyImageErrorKey = (error: unknown): string => apiErrorKey(error, COMPANY_IMAGE_ERRORS);

/** 選檔後的用戶端檢查：回傳錯誤 i18n key，合法回傳 null。 */
export function imageFileError(file: File): string | null {
  if (!IMAGE_TYPES.includes(file.type)) return 'errors.SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE';
  if (file.size === 0) return 'errors.COMMON.REQUEST.VALIDATION_FAILED';
  return file.size > IMAGE_MAX_BYTES ? 'errors.SYS.COMPANY_IMAGE.TOO_LARGE' : null;
}

const path = (kind: CompanyImageKind): string => `${COMPANY_IMAGES_API}/${kind.toLowerCase()}`;

@Injectable({ providedIn: 'root' })
export class CompanyImageService {
  private readonly http = inject(HttpClient);
  list(): Observable<CompanyImageDetail[]> { return this.http.get<CompanyImageDetail[]>(COMPANY_IMAGES_API); }
  content(kind: CompanyImageKind): Observable<Blob> { return this.http.get(`${path(kind)}/content`, { responseType: 'blob' }); }
  upload(kind: CompanyImageKind, file: File, version: number): Observable<CompanyImageDetail> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('version', String(version));
    return this.http.put<CompanyImageDetail>(path(kind), form);
  }
  remove(kind: CompanyImageKind, version: number): Observable<void> {
    return this.http.delete<void>(path(kind), { params: new HttpParams().set('version', version) });
  }
}
