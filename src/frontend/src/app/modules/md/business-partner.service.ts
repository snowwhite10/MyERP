import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, InjectionToken, Type } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { MasterCategoryService } from './master-category.service';

/** 往來對象種類；決定子資源與匯入的 API 路徑、權限、文字及 data-testid。 */
export type PartnerKind = 'customer' | 'supplier';

/** 公司內以代碼識別、可選同公司分類的往來對象（客戶、供應商）。 */
export interface BusinessPartner {
  id: number;
  code: string;
  name: string;
  categoryId: number | null;
  /** 單據帶出的預設付款條件與結算方式（ADR-047）。 */
  defaultPaymentTermId?: number | null;
  defaultSettlementMethodId?: number | null;
  isActive: boolean;
  version: number;
  reviewStatus?: PartnerReviewStatus;
}

/** 新增與修改共用的可選欄位；修改時缺少的預設值會被後端清除。 */
export interface PartnerDraft {
  name: string;
  categoryId: number | null;
  defaultPaymentTermId?: number | null;
  defaultSettlementMethodId?: number | null;
}

/** 往來對象審核狀態（ADR-046）；核准前不可用於交易。 */
export type PartnerReviewStatus = 'Pending' | 'Approved' | 'Rejected';

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
    this.knownCodes = [...['DUPLICATE_CODE', 'NOT_FOUND', 'NOT_APPROVED', 'APPROVAL_PENDING', 'ALREADY_APPROVED',
      'IN_USE', 'APPROVAL_STATE_CHANGED'].map((code) => `${errorPrefix}.${code}`),
    'SYS.APPROVAL.ALREADY_PENDING', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];
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

  create(body: PartnerDraft & { code: string }): Observable<BusinessPartner> {
    return this.http.post<BusinessPartner>(this.baseUrl, {
      ...body, code: body.code.trim().toUpperCase(), name: body.name.trim(),
    });
  }

  update(id: number, body: PartnerDraft & { isActive: boolean; version: number }): Observable<BusinessPartner> {
    return this.http.put<BusinessPartner>(`${this.baseUrl}/${id}`, { ...body, name: body.name.trim() });
  }

  /** 送交 SYS 審批；無適用步驟時後端立即核准。 */
  submit(id: number, version: number): Observable<{ status: PartnerReviewStatus }> {
    return this.http.post<{ status: PartnerReviewStatus }>(`${this.baseUrl}/${id}/submit`, { version });
  }

  /** 以版本號軟刪除；待審 422、已被單據引用 409。 */
  delete(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, { params: new HttpParams().set('version', version) });
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
  /** 將已提交列表查詢同步到分享網址。 */
  urlQuery?: boolean;
  /** 列表每列提供聯絡人對話框；客戶另有送貨地址（ADR-043）。 */
  contacts?: PartnerKind;
  /** 匯入按鈕及對話框（需 `{permission}.IMPORT`，ADR-044）。 */
  imports?: PartnerKind;
}

export const PARTNER_PAGE = new InjectionToken<PartnerPage>('PARTNER_PAGE');
