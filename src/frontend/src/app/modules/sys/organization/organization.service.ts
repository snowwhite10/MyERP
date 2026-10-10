import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';

export type OrganizationKind = 'company' | 'branch' | 'department';

/** 下拉選項（分店、上級部門）。 */
export interface OrganizationOption {
  id: number;
  code: string;
  name: string;
}

/** 權限代碼：SYS.COMPANY / SYS.BRANCH / SYS.DEPARTMENT + 動作（api.md「權限」）。 */
export function organizationPermission(kind: OrganizationKind, action: 'VIEW' | 'CREATE' | 'EDIT'): string {
  return `SYS.${kind.toUpperCase()}.${action}`;
}

/** 公司／分店／部門（api.md「SYS 組織 API」）；部門另有分店與上級部門。 */
export interface OrganizationRecord {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
  version: number;
  branchId?: number | null;
  parentDepartmentId?: number | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface OrganizationQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  /** `code`、`-code`、`name`、`-name` */
  sort?: string;
  /** 只對公司有效：含已停用公司 */
  includeInactive?: boolean;
}

export interface OrganizationCreate {
  code: string;
  name: string;
  branchId?: number | null;
  parentDepartmentId?: number | null;
}

export interface OrganizationUpdate {
  name: string;
  isActive: boolean;
  version: number;
  branchId?: number | null;
  parentDepartmentId?: number | null;
}

export const ORGANIZATION_API: Record<OrganizationKind, string> = {
  company: '/api/v1/sys/companies',
  branch: '/api/v1/sys/branches',
  department: '/api/v1/sys/departments',
};

/** 頁面會顯示的組織錯誤代碼（其他錯誤顯示通用訊息）。 */
export const ORGANIZATION_ERROR_CODES = [
  'SYS.COMPANY.DUPLICATE_CODE',
  'SYS.BRANCH.DUPLICATE_CODE',
  'SYS.DEPARTMENT.DUPLICATE_CODE',
  'COMMON.CONCURRENCY.CONFLICT',
  'COMMON.PERMISSION.DENIED',
] as const;

/** 將 API 錯誤轉成 i18n key（`errors.<code>`、驗證失敗、限流、網絡或通用錯誤）。 */
export function organizationErrorKey(error: unknown): string {
  return apiErrorKey(error, ORGANIZATION_ERROR_CODES);
}

/** 組織 API；X-Company-Id 由攔截器帶入，分店／部門因此只會取得目前公司的資料。 */
@Injectable({ providedIn: 'root' })
export class OrganizationService {
  private readonly http = inject(HttpClient);

  list(kind: OrganizationKind, query: OrganizationQuery): Observable<PagedResult<OrganizationRecord>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    if (kind === 'company' && query.includeInactive) params = params.set('includeInactive', true);
    return this.http.get<PagedResult<OrganizationRecord>>(ORGANIZATION_API[kind], { params });
  }

  create(kind: OrganizationKind, body: OrganizationCreate): Observable<OrganizationRecord> {
    return this.http.post<OrganizationRecord>(ORGANIZATION_API[kind], body);
  }

  update(kind: OrganizationKind, id: number, body: OrganizationUpdate): Observable<OrganizationRecord> {
    return this.http.put<OrganizationRecord>(`${ORGANIZATION_API[kind]}/${id}`, body);
  }
}
