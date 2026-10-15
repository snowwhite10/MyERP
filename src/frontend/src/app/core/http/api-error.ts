import { HttpErrorResponse } from '@angular/common/http';

export const NETWORK_ERROR_KEY = 'errors.http.network';
export const UNKNOWN_ERROR_KEY = 'errors.http.unknown';
export const VALIDATION_ERROR_KEY = 'errors.COMMON.REQUEST.VALIDATION_FAILED';
export const RATE_LIMITED_ERROR_KEY = 'errors.COMMON.REQUEST.RATE_LIMITED';

/** 只向用戶顯示伺服器提供的 5xx 追蹤碼，方便支援人員查詢同一筆請求。 */
export function apiErrorTraceId(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status < 500) return null;
  const value = (error.error as { traceId?: unknown } | null)?.traceId;
  return typeof value === 'string' && value.length <= 200 ? value : null;
}

/** 保留 400／422 的欄位路徑與逐項原因，避免業務拒絕只顯示通用訊息。 */
export function apiValidationDetails(error: unknown): string[] {
  if (!(error instanceof HttpErrorResponse) || (error.status !== 400 && error.status !== 422)) return [];
  const errors = (error.error as { errors?: unknown } | null)?.errors;
  if (!errors || typeof errors !== 'object' || Array.isArray(errors)) return [];
  return Object.entries(errors).slice(0, 20).flatMap(([field, messages]) =>
    Array.isArray(messages) ? messages.filter((message): message is string => typeof message === 'string')
      .slice(0, 3).map(message => `${field.slice(0, 120)}: ${message.slice(0, 500)}`) : []);
}

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
