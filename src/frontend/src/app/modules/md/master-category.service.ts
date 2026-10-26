import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, InjectionToken, Type } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

/** 公司內以代碼識別的扁平分類（客戶分類、供應商分類）。 */
export interface MasterCategory {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
  version: number;
}

export interface CategoryQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  sort?: string;
}

/** 分類 API 共用實作；子類只提供路徑與可辨識的錯誤碼前綴（如 `MD.SUPPLIER_CATEGORY`）。 */
export abstract class MasterCategoryService {
  private readonly http = inject(HttpClient);
  private readonly knownCodes: readonly string[];

  protected constructor(private readonly baseUrl: string, errorPrefix: string) {
    this.knownCodes = [`${errorPrefix}.DUPLICATE_CODE`, `${errorPrefix}.NOT_FOUND`, `${errorPrefix}.IN_USE`,
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];
  }

  errorKey(error: unknown): string {
    return apiErrorKey(error, this.knownCodes);
  }

  list(query: CategoryQuery): Observable<PagedResult<MasterCategory>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<MasterCategory>>(this.baseUrl, { params });
  }

  create(body: { code: string; name: string }): Observable<MasterCategory> {
    return this.http.post<MasterCategory>(this.baseUrl, { code: body.code.trim().toUpperCase(), name: body.name.trim() });
  }

  update(id: number, body: { name: string; isActive: boolean; version: number }): Observable<MasterCategory> {
    return this.http.put<MasterCategory>(`${this.baseUrl}/${id}`, { ...body, name: body.name.trim() });
  }

  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, { params: new HttpParams().set('version', version) });
  }
}

/** 分類維護頁設定，由路由 providers 提供給共用的 `CategoryList`。 */
export interface CategoryPage {
  api: Type<MasterCategoryService>;
  /** 此路由將已提交的列表查詢同步至網址。 */
  urlQuery?: boolean;
  /** 權限前綴，例如 `MD.SUPPLIER_CATEGORY`（頁面再加上 `.CREATE` 等動作）。 */
  permission: string;
  /** i18n 前綴，例如 `md.supplierCategory`。 */
  i18n: string;
}

export const CATEGORY_PAGE = new InjectionToken<CategoryPage>('CATEGORY_PAGE');
