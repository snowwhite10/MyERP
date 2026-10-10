import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { DataScope } from '../../../core/auth/permission.service';
import { PagedResult } from '../organization/organization.service';

export const ROLE_API = '/api/v1/sys/roles';
export const PERMISSION_CATALOG_API = '/api/v1/sys/permissions';

/** 資料範圍由窄到寬（下拉選單順序）。 */
export const DATA_SCOPES: readonly DataScope[] = ['Self', 'Department', 'Branch', 'Company'];

/** 資料範圍的 i18n key（`sys.role.scope.self` 等；i18n key 段落一律 camelCase）。 */
export function dataScopeKey(scope: DataScope): string {
  return `sys.role.scope.${scope.charAt(0).toLowerCase()}${scope.slice(1)}`;
}

/** 角色（api.md「角色與權限」）；列表與單筆都包含權限代碼。 */
export interface Role {
  id: number;
  code: string;
  name: string;
  description: string | null;
  dataScope: DataScope;
  isFullAccess: boolean;
  isActive: boolean;
  version: number;
  permissions: string[];
}

export interface RoleQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  /** `code`、`name`，可加 `-` 降序 */
  sort?: string;
}

export interface RoleInput {
  code: string;
  name: string;
  description: string | null;
  dataScope: DataScope;
  isFullAccess: boolean;
  permissions: string[];
}

export interface RoleUpdate extends Omit<RoleInput, 'code'> {
  isActive: boolean;
  version: number;
}

/** 權限目錄項目。 */
export interface PermissionEntry {
  code: string;
  module: string;
}

/** 依模組分組的權限（模組依代碼排序，組內依代碼排序）。 */
export interface PermissionGroup {
  module: string;
  codes: string[];
}

/** 角色代碼格式與後端相同：1–50 字元 ASCII 英文、數字、`-`、`_`（保存為大寫）。 */
export const ROLE_CODE_PATTERN = /^[A-Za-z0-9_-]{1,50}$/;
export const ROLE_NAME_MAX_LENGTH = 200;
export const ROLE_DESCRIPTION_MAX_LENGTH = 500;

/** 頁面會顯示的角色錯誤代碼（其他錯誤顯示通用訊息）。 */
export const ROLE_ERROR_CODES = [
  'SYS.ROLE.DUPLICATE_CODE',
  'SYS.ROLE.IN_USE',
  'SYS.ROLE.NOT_FOUND',
  'COMMON.CONCURRENCY.CONFLICT',
  'COMMON.PERMISSION.DENIED',
  'COMMON.REQUEST.VALIDATION_FAILED',
] as const;

/** 將 API 錯誤轉成 i18n key。 */
export function roleErrorKey(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'errors.http.network';
    const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
    if (typeof code === 'string' && (ROLE_ERROR_CODES as readonly string[]).includes(code)) {
      return `errors.${code}`;
    }
    if (error.status === 400) return 'errors.COMMON.REQUEST.VALIDATION_FAILED';
  }
  return 'errors.http.unknown';
}

/** 權限目錄依模組分組。 */
export function groupPermissions(entries: readonly PermissionEntry[]): PermissionGroup[] {
  const groups = new Map<string, string[]>();
  for (const entry of entries) {
    groups.set(entry.module, [...(groups.get(entry.module) ?? []), entry.code]);
  }
  return [...groups.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([module, codes]) => ({ module, codes: [...codes].sort() }));
}

/** 角色 API（角色全系統共用，不分公司；權限仍在目前公司內檢查）。 */
@Injectable({ providedIn: 'root' })
export class RoleService {
  private readonly http = inject(HttpClient);

  list(query: RoleQuery): Observable<PagedResult<Role>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<Role>>(ROLE_API, { params });
  }

  permissions(): Observable<PermissionEntry[]> {
    return this.http.get<PermissionEntry[]>(PERMISSION_CATALOG_API);
  }

  create(input: RoleInput): Observable<Role> {
    return this.http.post<Role>(ROLE_API, input);
  }

  update(id: number, input: RoleUpdate): Observable<Role> {
    return this.http.put<Role>(`${ROLE_API}/${id}`, input);
  }

  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${ROLE_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
}
