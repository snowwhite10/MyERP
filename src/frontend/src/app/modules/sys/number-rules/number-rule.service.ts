import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';
import { PagedResult } from '../../../shared/list-page/table-query';

export const NUMBER_RULE_API = '/api/v1/sys/number-rules';

/** 單號的日期部分（與後端 `NumberDateFormat` 相同，JSON 以字串傳送）。 */
export type NumberDateFormat = 'None' | 'Year' | 'YearMonth' | 'YearMonthDay' | 'ShortYearMonth' | 'ShortYearMonthDay';

/** 流水號重設週期。 */
export type NumberResetPeriod = 'Never' | 'Yearly' | 'Monthly' | 'Daily';

export const DATE_FORMATS: readonly NumberDateFormat[] = [
  'None',
  'Year',
  'YearMonth',
  'YearMonthDay',
  'ShortYearMonth',
  'ShortYearMonthDay',
];
export const RESET_PERIODS: readonly NumberResetPeriod[] = ['Never', 'Yearly', 'Monthly', 'Daily'];

/** 單號規則（api.md「單號規則」）。`sample` 為今天第 1 號；`isInUse` 表示已發出單號。 */
export interface NumberRule {
  id: number;
  documentType: string;
  name: string;
  prefix: string;
  dateFormat: NumberDateFormat;
  sequenceLength: number;
  resetPeriod: NumberResetPeriod;
  isActive: boolean;
  isInUse: boolean;
  sample: string;
  version: number;
}

export interface NumberRuleQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  /** `documentType`、`name`，可加 `-` 降序 */
  sort?: string;
}

export interface NumberRuleInput {
  documentType: string;
  name: string;
  prefix: string;
  dateFormat: NumberDateFormat;
  sequenceLength: number;
  resetPeriod: NumberResetPeriod;
}

export interface NumberRuleUpdate extends Omit<NumberRuleInput, 'documentType'> {
  isActive: boolean;
  version: number;
}

export interface NumberPreview {
  documentType: string;
  documentDate: string;
  nextNumber: string;
}

/** 與後端 `NumberRule` 相同的限制。 */
export const DOCUMENT_TYPE_PATTERN = /^[A-Z][A-Z0-9]*\.[A-Z][A-Z0-9_]*$/;
export const DOCUMENT_TYPE_MAX_LENGTH = 50;
export const PREFIX_PATTERN = /^[A-Z0-9_-]{0,10}$/;
export const PREFIX_MAX_LENGTH = 10;
export const NUMBER_RULE_NAME_MAX_LENGTH = 100;
export const MIN_SEQUENCE_LENGTH = 3;
export const MAX_SEQUENCE_LENGTH = 10;

/** 頁面會顯示的單號規則錯誤代碼（其他錯誤顯示通用訊息）。 */
export const NUMBER_RULE_ERROR_CODES = [
  'SYS.NUMBER_RULE.DUPLICATE_TYPE',
  'SYS.NUMBER_RULE.NOT_FOUND',
  'SYS.NUMBER_RULE.IN_USE',
  'SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED',
  'COMMON.CONCURRENCY.CONFLICT',
  'COMMON.PERMISSION.DENIED',
  'COMMON.REQUEST.VALIDATION_FAILED',
] as const;

/** 將 API 錯誤轉成 i18n key。 */
export function numberRuleErrorKey(error: unknown): string {
  return apiErrorKey(error, NUMBER_RULE_ERROR_CODES);
}

/** 列舉值的 i18n key（`sys.numberRule.dateFormats.yearMonth` 等；i18n key 段落一律 camelCase）。 */
export function dateFormatKey(format: NumberDateFormat): string {
  return `sys.numberRule.dateFormats.${camel(format)}`;
}

export function resetPeriodKey(period: NumberResetPeriod): string {
  return `sys.numberRule.resetPeriods.${camel(period)}`;
}

/**
 * 重設週期是否包含在日期格式內（與後端 `NumberRule.ValidateSettings` 相同）：
 * 每年重設要有年、每月要有月、每日要有日，否則不同週期會產生相同單號。
 */
export function periodCovered(format: NumberDateFormat, period: NumberResetPeriod): boolean {
  switch (period) {
    case 'Yearly':
      return format !== 'None';
    case 'Monthly':
      return format !== 'None' && format !== 'Year';
    case 'Daily':
      return format === 'YearMonthDay' || format === 'ShortYearMonthDay';
    default:
      return true;
  }
}

/** 組合單號樣式（與後端 `NumberRule.Format` 相同）：前綴 + 日期 + 補零流水號。 */
export function formatNumber(
  prefix: string,
  format: NumberDateFormat,
  sequenceLength: number,
  date: Date,
  sequence = 1,
): string {
  const pad = (n: number, width: number) => String(n).padStart(width, '0');
  const yyyy = pad(date.getFullYear(), 4);
  const yy = yyyy.slice(-2);
  const mm = pad(date.getMonth() + 1, 2);
  const dd = pad(date.getDate(), 2);
  const datePart: Record<NumberDateFormat, string> = {
    None: '',
    Year: yyyy,
    YearMonth: yyyy + mm,
    YearMonthDay: yyyy + mm + dd,
    ShortYearMonth: yy + mm,
    ShortYearMonthDay: yy + mm + dd,
  };
  return prefix + datePart[format] + String(sequence).padStart(sequenceLength, '0');
}

function camel(value: string): string {
  return value.charAt(0).toLowerCase() + value.slice(1);
}

/** 單號規則 API（規則屬於目前公司，經 `X-Company-Id` 隔離）。 */
@Injectable({ providedIn: 'root' })
export class NumberRuleService {
  private readonly http = inject(HttpClient);

  list(query: NumberRuleQuery): Observable<PagedResult<NumberRule>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<NumberRule>>(NUMBER_RULE_API, { params });
  }

  /** 指定業務日期（`yyyy-MM-dd`，空白為今天）的下一個單號，不佔用流水號。 */
  preview(id: number, date?: string): Observable<NumberPreview> {
    const params = date ? new HttpParams().set('date', date) : undefined;
    return this.http.get<NumberPreview>(`${NUMBER_RULE_API}/${id}/preview`, { params });
  }

  create(input: NumberRuleInput): Observable<NumberRule> {
    return this.http.post<NumberRule>(NUMBER_RULE_API, input);
  }

  update(id: number, input: NumberRuleUpdate): Observable<NumberRule> {
    return this.http.put<NumberRule>(`${NUMBER_RULE_API}/${id}`, input);
  }
}
