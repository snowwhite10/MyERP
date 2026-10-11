import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';
import { COMPANY_HEADER } from '../../../core/http/api-headers.interceptor';
import { ORGANIZATION_API, OrganizationOption, OrganizationRecord, PagedResult } from '../organization/organization.service';

export const USER_API = '/api/v1/sys/users';

/** 用戶在一間公司的授權（GET /users/{id}/companies）。 */
export interface UserCompanyAssignment {
  companyId: number;
  companyCode: string;
  companyName: string;
  branchId: number | null;
  departmentId: number | null;
  isDefault: boolean;
  isActive: boolean;
  roleIds: number[];
}

export type UserCompanyAssignmentInput = Pick<UserCompanyAssignment, 'branchId' | 'departmentId' | 'isDefault' | 'isActive' | 'roleIds'>;

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
  'SYS.USER.SELF_ROLE_CHANGE',
  'SYS.USER.NOT_FOUND',
  'SYS.ROLE.BEYOND_AUTHORITY',
  'SYS.COMPANY.NOT_FOUND',
  'COMMON.CONCURRENCY.CONFLICT',
  'COMMON.PERMISSION.DENIED',
] as const;

/**
 * 將 API 錯誤轉成 i18n key。`passwordInvolved` 為新增／重設密碼：400 多半是密碼不符政策，顯示政策說明。
 */
export function userErrorKey(error: unknown, passwordInvolved = false): string {
  return passwordInvolved ? apiErrorKey(error, USER_ERROR_CODES, 'sys.user.passwordRejected') : apiErrorKey(error, USER_ERROR_CODES);
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

  /** 用戶在操作者獲授權公司內的指派（ADR-017）。 */
  assignments(id: number): Observable<UserCompanyAssignment[]> {
    return this.http.get<UserCompanyAssignment[]>(`${USER_API}/${id}/companies`);
  }

  /** 設定用戶在某公司的組織歸屬、預設公司與角色（整組取代）。 */
  assign(id: number, companyId: number, input: UserCompanyAssignmentInput): Observable<void> {
    return this.http.put<void>(`${USER_API}/${id}/companies/${companyId}`, input);
  }

  /**
   * 指定公司的啟用分店或部門（下拉選項，最多 200 筆）。以該公司的 X-Company-Id 查詢，
   * 攔截器不會覆蓋已有的標頭；操作者沒有該公司的 VIEW 權限時後端回 403。
   */
  organizationOptions(kind: 'branch' | 'department', companyId: number): Observable<OrganizationOption[]> {
    const params = new HttpParams().set('page', 1).set('pageSize', 200).set('sort', 'code');
    return this.http
      .get<PagedResult<OrganizationRecord>>(ORGANIZATION_API[kind], { params, headers: { [COMPANY_HEADER]: String(companyId) } })
      .pipe(map((result) => result.items.filter((item) => item.isActive).map(({ id, code, name }) => ({ id, code, name }))));
  }
}
