import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, InjectionToken, Type } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { MasterCategoryService } from './master-category.service';

/** 公司內以代碼識別、可選同公司分類的往來對象（客戶、供應商）。 */
export interface BusinessPartner {
  id: number;
  code: string;
  name: string;
  categoryId: number | null;
  isActive: boolean;
  version: number;
}

export interface PartnerQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  sort?: string;
}

/** 往來對象 API 共用實作；子類只提供路徑與可辨識的錯誤碼前綴（如 `MD.SUPPLIER`）。 */
export abstract class BusinessPartnerService {
  private readonly http = inject(HttpClient);
  private readonly knownCodes: readonly string[];

  protected constructor(private readonly baseUrl: string, errorPrefix: string) {
    this.knownCodes = [`${errorPrefix}.DUPLICATE_CODE`, `${errorPrefix}.NOT_FOUND`,
      'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];
  }

  errorKey(error: unknown): string {
    return apiErrorKey(error, this.knownCodes);
  }

  list(query: PartnerQuery): Observable<PagedResult<BusinessPartner>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<BusinessPartner>>(this.baseUrl, { params });
  }

  create(body: { code: string; name: string; categoryId: number | null }): Observable<BusinessPartner> {
    return this.http.post<BusinessPartner>(this.baseUrl, {
      code: body.code.trim().toUpperCase(), name: body.name.trim(), categoryId: body.categoryId,
    });
  }

  update(id: number, body: { name: string; categoryId: number | null; isActive: boolean; version: number }): Observable<BusinessPartner> {
    return this.http.put<BusinessPartner>(`${this.baseUrl}/${id}`, { ...body, name: body.name.trim() });
  }
}

/** 往來對象維護頁設定，由路由 providers 提供給共用的 `PartnerList`。 */
export interface PartnerPage {
  api: Type<BusinessPartnerService>;
  categoryApi: Type<MasterCategoryService>;
  /** 權限前綴，例如 `MD.SUPPLIER`（頁面再加上 `.CREATE` 等動作）。 */
  permission: string;
  /** 分類查看權限；沒有時不載入分類。 */
  categoryPermission: string;
  /** i18n 前綴，例如 `md.supplier`。 */
  i18n: string;
  /** data-testid 前綴，例如 `md-supplier`。 */
  testId: string;
}

export const PARTNER_PAGE = new InjectionToken<PartnerPage>('PARTNER_PAGE');
