import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CompanyContextService } from '../company/company-context.service';
import { LanguageService } from '../i18n/language.service';

/** 後端 API 路徑前綴（api-conventions.md）。只有 API 請求加標頭，語言檔等靜態資源不加。 */
export const API_PATH_PREFIX = '/api/';

export const COMPANY_HEADER = 'X-Company-Id';

export function isApiRequest(url: string): boolean {
  const path = url.replace(/^https?:\/\/[^/]+/i, '');
  return path.startsWith(API_PATH_PREFIX) || path.startsWith(API_PATH_PREFIX.slice(1));
}

/** 為 API 請求加上 Accept-Language（目前語言）與 X-Company-Id（目前公司）。 */
export const apiHeadersInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url)) {
    return next(req);
  }
  const language = inject(LanguageService).current();
  const company = inject(CompanyContextService).current();

  const headers: Record<string, string> = { 'Accept-Language': language };
  if (company && !req.headers.has(COMPANY_HEADER)) {
    headers[COMPANY_HEADER] = String(company.id);
  }
  return next(req.clone({ setHeaders: headers }));
};
