import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';

export const ITEM_API = '/api/v1/md/items';
export interface Item {
  id: number; code: string; name: string; specification: string | null;
  baseUomId: number; brandId: number | null; categoryId: number | null;
  isActive: boolean; version: number;
}
export interface ItemDraft {
  name: string; specification: string | null; baseUomId: number;
  brandId: number | null; categoryId: number | null;
}

/** 物料專屬換算：1 個 uomCode = factor 個物料基本單位（ADR-035）。 */
export interface ItemUomConversion {
  id: number; itemId: number; uomId: number; uomCode: string; factor: number; version: number;
}
export interface ItemConvertResult { itemId: number; fromUomId: number; toUomId: number; quantity: number; result: number }

@Injectable({ providedIn: 'root' })
export class ItemService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.ITEM.DUPLICATE_CODE', 'MD.ITEM.NOT_FOUND', 'MD.ITEM.BASE_UOM_LOCKED', 'MD.ITEM.IN_USE',
      'MD.ITEM_UOM_CONVERSION.DUPLICATE', 'MD.ITEM_UOM_CONVERSION.NOT_FOUND', 'MD.UOM.NOT_FOUND',
      'MD.UOM_CONVERSION.NO_PATH', 'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']);
  }
  listConversions(itemId: number): Observable<ItemUomConversion[]> {
    return this.http.get<ItemUomConversion[]>(`${ITEM_API}/${itemId}/uom-conversions`);
  }
  /** 係數以 JSON 數字傳遞：15 位有效數字內可無損往返（ADR-034）。 */
  createConversion(itemId: number, body: { uomId: number; factor: string }): Observable<ItemUomConversion> {
    return this.http.post<ItemUomConversion>(`${ITEM_API}/${itemId}/uom-conversions`,
      { uomId: body.uomId, factor: Number(body.factor.trim()) });
  }
  updateConversion(itemId: number, id: number, body: { factor: string; version: number }): Observable<ItemUomConversion> {
    return this.http.put<ItemUomConversion>(`${ITEM_API}/${itemId}/uom-conversions/${id}`,
      { factor: Number(body.factor.trim()), version: body.version });
  }
  removeConversion(itemId: number, id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${ITEM_API}/${itemId}/uom-conversions/${id}`, { params: new HttpParams().set('version', version) });
  }
  /** 數量以原文字放入查詢字串，由後端 decimal 換算。 */
  convert(itemId: number, fromUomId: number, toUomId: number, quantity: string): Observable<ItemConvertResult> {
    const params = new HttpParams().set('fromUomId', fromUomId).set('toUomId', toUomId).set('quantity', quantity.trim());
    return this.http.get<ItemConvertResult>(`${ITEM_API}/${itemId}/uom-conversions/convert`, { params });
  }
  list(query: { page: number; pageSize: number; keyword?: string; sort?: string }): Observable<PagedResult<Item>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<Item>>(ITEM_API, { params });
  }
  create(body: ItemDraft & { code: string }): Observable<Item> {
    return this.http.post<Item>(ITEM_API, { ...this.clean(body), code: body.code.trim().toUpperCase() });
  }
  update(id: number, body: ItemDraft & { isActive: boolean; version: number }): Observable<Item> {
    return this.http.put<Item>(`${ITEM_API}/${id}`, { ...this.clean(body), isActive: body.isActive, version: body.version });
  }
  /** 軟刪除物料（ADR-050）：專屬換算、圖片、屬性一併刪除；已被庫存或單據使用時 409 IN_USE。 */
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${ITEM_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
  private clean(body: ItemDraft): ItemDraft {
    return { name: body.name.trim(), specification: body.specification?.trim() || null,
      baseUomId: body.baseUomId, brandId: body.brandId, categoryId: body.categoryId };
  }
}
