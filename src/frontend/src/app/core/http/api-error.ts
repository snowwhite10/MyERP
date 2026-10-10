import { HttpErrorResponse } from '@angular/common/http';

export const NETWORK_ERROR_KEY = 'errors.http.network';
export const UNKNOWN_ERROR_KEY = 'errors.http.unknown';
export const VALIDATION_ERROR_KEY = 'errors.COMMON.REQUEST.VALIDATION_FAILED';
export const RATE_LIMITED_ERROR_KEY = 'errors.COMMON.REQUEST.RATE_LIMITED';

/**
 * 將 API 錯誤轉成 i18n key（api-conventions.md 第 4 節）：連不到伺服器 → network；429 限流 → RATE_LIMITED；
 * 頁面認得的 errorCode → `errors.<code>`；其他 400 → `validationKey`；其餘 → unknown。
 */
export function apiErrorKey(error: unknown, knownCodes: readonly string[], validationKey = VALIDATION_ERROR_KEY): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return NETWORK_ERROR_KEY;
    if (error.status === 429) return RATE_LIMITED_ERROR_KEY;
    const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
    if (typeof code === 'string' && knownCodes.includes(code)) {
      return `errors.${code}`;
    }
    if (error.status === 400) return validationKey;
  }
  return UNKNOWN_ERROR_KEY;
}
