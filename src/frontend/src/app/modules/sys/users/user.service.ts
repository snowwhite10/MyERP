import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../organization/organization.service';

export const USER_API = '/api/v1/sys/users';

/** 用戶摘要（api.md「用戶身分」）；不含雜湊、失敗次數與鎖定期限。 */
export interface UserSummary {
  id: number;
  username: string;
  isActive: boolean;
  isLocked: boolean;
  /** UTC+8 ISO 時間 */
  passwordChangedAt: string;
  isPasswordExpired: boolean;
  version: number;
}

export interface UserQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  /** `username` 或 `-username` */
  sort?: string;
}

/** 帳號格式與後端相同：3–100 字元，英文字母、數字、點、底線、連字號。 */
export const USERNAME_PATTERN = /^[A-Za-z0-9._-]{3,100}$/;
/** 密碼長度上限（政策上限 128，其餘規則由後端依設定檢查）。 */
export const PASSWORD_MAX_LENGTH = 128;

/** 頁面會顯示的用戶錯誤代碼（其他錯誤顯示通用訊息）。 */
export const USER_ERROR_CODES = [
  'SYS.USER.DUPLICATE',
  'SYS.USER.SELF_DEACTIVATION',
  'SYS.USER.NOT_FOUND',
  'COMMON.CONCURRENCY.CONFLICT',
  'COMMON.PERMISSION.DENIED',
] as const;

/**
 * 將 API 錯誤轉成 i18n key。`passwordInvolved` 為新增／重設密碼：400 多半是密碼不符政策，顯示政策說明。
 */
export function userErrorKey(error: unknown, passwordInvolved = false): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'errors.http.network';
    const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
    if (typeof code === 'string' && (USER_ERROR_CODES as readonly string[]).includes(code)) {
      return `errors.${code}`;
    }
    if (error.status === 400) return passwordInvolved ? 'sys.user.passwordRejected' : 'errors.COMMON.REQUEST.VALIDATION_FAILED';
  }
  return 'errors.http.unknown';
}

/** 用戶 API；X-Company-Id 由攔截器帶入，只會看到目前公司的用戶（ADR-016）。 */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);

  list(query: UserQuery): Observable<PagedResult<UserSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<UserSummary>>(USER_API, { params });
  }

  create(username: string, password: string): Observable<UserSummary> {
    return this.http.post<UserSummary>(USER_API, { username, password });
  }

  setActive(id: number, isActive: boolean, version: number): Observable<UserSummary> {
    return this.http.put<UserSummary>(`${USER_API}/${id}`, { isActive, version });
  }

  resetPassword(id: number, password: string): Observable<void> {
    return this.http.post<void>(`${USER_API}/${id}/reset-password`, { password });
  }

  unlock(id: number): Observable<void> {
    return this.http.post<void>(`${USER_API}/${id}/unlock`, null);
  }
}
