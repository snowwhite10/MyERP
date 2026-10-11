import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { CategoryQuery } from './master-category.service';

export const UOM_API = '/api/v1/md/uoms';
export const UOM_CONVERSION_API = '/api/v1/md/uom-conversions';

/** 換算係數：正數、最多 9 位整數與 6 位小數（15 位有效數字，JSON 數字可無損往返，ADR-034）。 */
export const FACTOR_PATTERN = /^(?!0+(\.0+)?$)\d{1,9}(\.\d{1,6})?$/;
/** 試算數量：可負數、最多 12 位整數與 6 位小數；以原文字放入查詢字串，不經 JS 浮點運算。 */
export const QUANTITY_PATTERN = /^-?\d{1,12}(\.\d{1,6})?$/;

export interface Uom {
  id: number;
  code: string;
  name: string;
  decimalPlaces: number;
  isActive: boolean;
  version: number;
}

/** 1 個 from 單位 = factor 個 to 單位。 */
export interface UomConversion {
  id: number;
  fromUomId: number;
  fromUomCode: string;
  toUomId: number;
  toUomCode: string;
  factor: number;
  version: number;
}

export interface UomConvertResult {
  fromUomId: number;
  toUomId: number;
  quantity: number;
  result: number;
}

const KNOWN_CODES = ['MD.UOM.DUPLICATE_CODE', 'MD.UOM.NOT_FOUND', 'MD.UOM.IN_USE',
  'MD.UOM_CONVERSION.PATH_EXISTS', 'MD.UOM_CONVERSION.NOT_FOUND', 'MD.UOM_CONVERSION.NO_PATH',
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];

/** 計量單位與單位換算 API。 */
@Injectable({ providedIn: 'root' })
export class UomService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string {
    return apiErrorKey(error, KNOWN_CODES);
  }

  list(query: CategoryQuery): Observable<PagedResult<Uom>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<Uom>>(UOM_API, { params });
  }

  create(body: { code: string; name: string; decimalPlaces: number }): Observable<Uom> {
    return this.http.post<Uom>(UOM_API, { ...body, code: body.code.trim().toUpperCase(), name: body.name.trim() });
  }

  update(id: number, body: { name: string; isActive: boolean; decimalPlaces: number; version: number }): Observable<Uom> {
    return this.http.put<Uom>(`${UOM_API}/${id}`, { ...body, name: body.name.trim() });
  }

  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${UOM_API}/${id}`, { params: new HttpParams().set('version', version) });
  }

  listConversions(): Observable<UomConversion[]> {
    return this.http.get<UomConversion[]>(UOM_CONVERSION_API);
  }

  createConversion(body: { fromUomId: number; toUomId: number; factor: string }): Observable<UomConversion> {
    return this.http.post<UomConversion>(UOM_CONVERSION_API, { ...body, factor: Number(body.factor) });
  }

  updateConversion(id: number, body: { factor: string; version: number }): Observable<UomConversion> {
    return this.http.put<UomConversion>(`${UOM_CONVERSION_API}/${id}`, { ...body, factor: Number(body.factor) });
  }

  removeConversion(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${UOM_CONVERSION_API}/${id}`, { params: new HttpParams().set('version', version) });
  }

  convert(fromUomId: number, toUomId: number, quantity: string): Observable<UomConvertResult> {
    const params = new HttpParams().set('fromUomId', fromUomId).set('toUomId', toUomId).set('quantity', quantity.trim());
    return this.http.get<UomConvertResult>(`${UOM_CONVERSION_API}/convert`, { params });
  }
}
