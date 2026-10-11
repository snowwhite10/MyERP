import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';

export const PARAMETERS_API = '/api/v1/sys/parameters';
export type ParameterType = 'Boolean' | 'WholeNumber' | 'Number' | 'Text' | 'Option';

/** 後端 `ParameterDetail`：值一律為正規化字串；`version` 0 表示未修改過（使用預設值）。 */
export interface ParameterDetail {
  key: string;
  module: string;
  group: string;
  type: ParameterType;
  value: string;
  defaultValue: string;
  isDefault: boolean;
  minimum: number | null;
  maximum: number | null;
  maxLength: number;
  options: string[];
  version: number;
}
export interface ParameterValueUpdate { key: string; value: string; version: number; }

export const PARAMETER_ERRORS = ['COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED', 'COMMON.REQUEST.VALIDATION_FAILED'] as const;
export const parameterErrorKey = (error: unknown): string => apiErrorKey(error, PARAMETER_ERRORS);

const WHOLE = /^-?\d+$/;
const DECIMAL = /^-?\d+(\.\d+)?$/;

/** 用戶端檢查（伺服器仍會正規化並作最終驗證）；回傳錯誤 i18n key，合法回傳 null。 */
export function parameterValueError(parameter: ParameterDetail, value: string): string | null {
  const text = value.trim();
  switch (parameter.type) {
    case 'Boolean':
      return text === 'true' || text === 'false' ? null : 'sys.parameters.errors.invalid';
    case 'WholeNumber':
    case 'Number': {
      if (!(parameter.type === 'WholeNumber' ? WHOLE : DECIMAL).test(text)) return 'sys.parameters.errors.number';
      // 範圍只用於提示，十進位精度由伺服器以 decimal 判斷
      const number = Number(text);
      if ((parameter.minimum !== null && number < parameter.minimum) || (parameter.maximum !== null && number > parameter.maximum)) {
        return 'sys.parameters.errors.range';
      }
      return null;
    }
    case 'Text':
      return text.length > parameter.maxLength ?'sys.parameters.errors.length' : null;
    case 'Option':
      return parameter.options.includes(text) ? null : 'sys.parameters.errors.invalid';
  }
}

/** 400 回應的 `items[i].key`／`items[i].value` 轉成參數鍵（依送出的項目順序）。 */
export function parameterFieldErrors(error: unknown, sent: readonly ParameterValueUpdate[]): string[] {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) return [];
  const errors = (error.error as { errors?: Record<string, unknown> } | null)?.errors ?? {};
  return Object.keys(errors).flatMap(field => {
    const match = /^items\[(\d+)\]\./i.exec(field);
    const item = match ? sent[Number(match[1])] : undefined;
    return item ? [item.key] : [];
  });
}

@Injectable({ providedIn: 'root' })
export class ParameterService {
  private readonly http = inject(HttpClient);
  list(module?: string): Observable<ParameterDetail[]> {
    const params = module ? new HttpParams().set('module', module) : undefined;
    return this.http.get<ParameterDetail[]>(PARAMETERS_API, { params });
  }
  save(items: readonly ParameterValueUpdate[]): Observable<ParameterDetail[]> {
    return this.http.put<ParameterDetail[]>(PARAMETERS_API, { items });
  }
}
