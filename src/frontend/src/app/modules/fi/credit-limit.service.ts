import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey, jsonTextErrors } from '../../core/http/api-error';
import { formatDecimalText } from '../../shared/decimal-text';
import { SelectDialogQuery } from '../../shared/dialogs/select-dialog';
import { PagedResult } from '../../shared/list-page/table-query';
import { Customer, CustomerService } from '../md/customer.service';

const API = '/api/v1/ar/credit-limits';

export type CreditLimitStatus = 'Active' | 'Deactivated';
export const CREDIT_LIMIT_STATUSES: readonly CreditLimitStatus[] = ['Active', 'Deactivated'];
export const CREDIT_LIMIT_SORTS = ['effectiveFrom', 'effectiveTo', 'customerId', 'amount'] as const;
export const DEFAULT_CREDIT_LIMIT_SORT = '-effectiveFrom';
/** decimal(19,4)：0 至 999,999,999,999,999.9999，最多 4 位小數（BR-FI-002、FI-CREDIT-004）。 */
export const CREDIT_AMOUNT_PATTERN = /^(0|[1-9]\d{0,14})(\.\d{1,4})?$/;

export interface CreditLimitSummary {
  id: number;
  customerId: number;
  baseCurrency: string;
  effectiveFrom: string;
  effectiveTo: string;
  /** JSON 數字；顯示時保留伺服器回傳的位數，不做浮點運算。 */
  amount: number;
  /** 伺服器 JSON 的原始十進位文字（不經 double），顯示與編輯一律用此值。 */
  amountText: string;
  status: CreditLimitStatus;
  version: number;
  /** MD 客戶代碼／名稱（伺服器經 MD 契約帶出，含已停用／已刪除客戶）；MD 查無時為 null（ADR-077）。 */
  customerCode?: string | null;
  customerName?: string | null;
}

export interface CreditLimitDetail extends CreditLimitSummary {
  createdAt: string;
  createdBy: number;
  updatedAt: string | null;
  updatedBy: number | null;
}

export interface CreditLimitQuery {
  page: number;
  pageSize: number;
  customerId?: number | null;
  status?: CreditLimitStatus | null;
  from?: string | null;
  to?: string | null;
  sort?: string;
}

/** 表單草稿；金額以文字送出，後端以 decimal 讀取，避免 JavaScript 浮點遺失位數。 */
export interface CreditLimitDraft {
  customerId: number;
  effectiveFrom: string;
  effectiveTo: string;
  amount: string;
}

const KNOWN_CODES = ['FI.CREDIT.LIMIT_NOT_FOUND', 'FI.CREDIT.LIMIT_OVERLAP', 'FI.CREDIT.LIMIT_NOT_ACTIVE',
  'FI.CREDIT.BASE_CURRENCY_MISSING', 'FI.CREDIT.CURRENCY_MISMATCH', 'FI.CREDIT.LOCK_TIMEOUT',
  'MD.CUSTOMER.NOT_FOUND', 'MD.CUSTOMER.NOT_APPROVED', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];

/** FI 信用額度 API（REQ_F4116.7.1，介面 6.17–6.19）；公司與本位幣由伺服器決定。 */
@Injectable({ providedIn: 'root' })
export class CreditLimitService {
  private readonly http = inject(HttpClient);
  private readonly customers = inject(CustomerService);

  errorKey(error: unknown): string {
    return apiErrorKey(error, KNOWN_CODES, 'fi.creditLimit.validationFailed');
  }

  /** 400／422 的欄位錯誤（只保留表單認得的欄位）。 */
  fieldErrors(error: unknown): Partial<Record<keyof CreditLimitDraft, true>> {
    if (!(error instanceof HttpErrorResponse) || error.status !== 400) return {};
    const raw = (error.error as { errors?: unknown } | null)?.errors;
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
    const fields: Partial<Record<keyof CreditLimitDraft, true>> = {};
    for (const key of Object.keys(raw)) {
      const field = key.replace(/^\$\./, '');
      const name = (['customerId', 'effectiveFrom', 'effectiveTo', 'amount'] as const)
        .find(candidate => candidate.toLowerCase() === field.toLowerCase());
      if (name) fields[name] = true;
    }
    return fields;
  }

  list(query: CreditLimitQuery): Observable<PagedResult<CreditLimitSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.customerId) params = params.set('customerId', query.customerId);
    if (query.status) params = params.set('status', query.status);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get(API, { params, responseType: 'text' }).pipe(jsonTextErrors(), map(text => {
      const page = JSON.parse(text) as PagedResult<CreditLimitSummary>;
      const amounts = rawAmounts(text);
      return { ...page, items: page.items.map((item, index) => withText(item, amounts[index])) };
    }));
  }

  get(id: number): Observable<CreditLimitDetail> {
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(jsonTextErrors(), map(detail));
  }

  create(draft: CreditLimitDraft): Observable<CreditLimitDetail> {
    return this.http.post(API, body(draft), { responseType: 'text' }).pipe(jsonTextErrors(), map(detail));
  }

  update(id: number, version: number, draft: CreditLimitDraft): Observable<CreditLimitDetail> {
    return this.http.put(`${API}/${id}`, { version, ...body(draft) }, { responseType: 'text' }).pipe(jsonTextErrors(), map(detail));
  }

  deactivate(id: number, version: number): Observable<CreditLimitDetail> {
    return this.http.post(`${API}/${id}/deactivate`, { version }, { responseType: 'text' }).pipe(jsonTextErrors(), map(detail));
  }

  /** MD 客戶選擇器資料（需 MD.CUSTOMER.VIEW；核准與啟用由後端寫入時再檢查）。 */
  customerOptions(query: SelectDialogQuery): Observable<PagedResult<Customer>> {
    return this.customers.list({ page: query.page, pageSize: query.pageSize, keyword: query.keyword, sort: 'code' });
  }
}

function body(draft: CreditLimitDraft): CreditLimitDraft {
  return { customerId: draft.customerId, effectiveFrom: draft.effectiveFrom, effectiveTo: draft.effectiveTo,
    amount: draft.amount.trim() };
}

/** JSON 中每個 `"amount":` 的原始數字文字（依出現次序）；decimal(19,4) 可能超出 double 精度。 */
function rawAmounts(text: string): string[] {
  return [...text.matchAll(/"amount"\s*:\s*(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g)].map(match => match[1]);
}

function withText<T extends CreditLimitSummary>(item: T, raw: string | undefined): T {
  const exact = raw && /^-?\d+(\.\d+)?$/.test(raw) ? raw : String(item.amount);
  return { ...item, amountText: exact };
}

function detail(text: string): CreditLimitDetail {
  return withText(JSON.parse(text) as CreditLimitDetail, rawAmounts(text)[0]);
}

/** 以伺服器回傳的原始十進位文字加上千分位，不經浮點運算（例如 `1234567.5` → `1,234,567.5`）。 */
export function formatCreditAmount(amountText: string, locale?: string): string {
  return formatDecimalText(amountText, locale);
}
