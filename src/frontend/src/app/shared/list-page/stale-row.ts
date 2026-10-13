import { HttpErrorResponse } from '@angular/common/http';

/** 樂觀鎖版本衝突的錯誤碼（409）；同為 409 的「使用中」「已在審批中」不算過期。 */
export const CONCURRENCY_CONFLICT = 'COMMON.CONCURRENCY.CONFLICT';

/**
 * 儲存或刪除回 404（資料已被刪除）或 409 版本衝突表示畫面上的列已過期：
 * 呼叫端應重新查詢列表，讓使用者重試時帶最新版本，而不是一直 409（MD-F06）。
 */
export function isStaleRowError(error: unknown): boolean {
  return isMissingRowError(error) || (error instanceof HttpErrorResponse && error.status === 409
    && (error.error as { errorCode?: unknown } | null)?.errorCode === CONCURRENCY_CONFLICT);
}

/** 404：資料已被刪除，編輯中的表單應放棄。 */
export function isMissingRowError(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404;
}
