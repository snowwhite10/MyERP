import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { ITEM_API } from './item.service';
import { SortedMasterApi } from './process.service';

export const ITEM_ATTRIBUTE_API = '/api/v1/md/item-attributes';
/** 與後端相同的限制（ADR-045）：每物料最多 50 個屬性，值去除前後空白後 1–200 字。 */
export const MAX_ATTRIBUTES_PER_ITEM = 50;
export const MAX_ATTRIBUTE_VALUE_LENGTH = 200;

export interface ItemAttribute { id: number; code: string; name: string; sortOrder: number;
  itemCount: number; isActive: boolean; version: number }
export interface ItemAttributeValue { attributeId: number; attributeCode: string; attributeName: string;
  sortOrder: number; isAttributeActive: boolean; value: string }
export interface ItemAttributeValueInput { attributeId: number; value: string }

/** 物料屬性定義（介面 8.16，REQ_F4118.45.1–4）。 */
@Injectable({ providedIn: 'root' })
export class ItemAttributeService extends SortedMasterApi<ItemAttribute> {
  constructor() { super(ITEM_ATTRIBUTE_API, 'MD.ITEM_ATTRIBUTE', ['DUPLICATE_CODE', 'IN_USE', 'NOT_FOUND']); }
  create(draft: { code: string; name: string; sortOrder: number }): Observable<ItemAttribute> {
    return this.post({ code: draft.code.trim().toUpperCase(), name: draft.name.trim(), sortOrder: draft.sortOrder });
  }
  update(id: number, body: { name: string; sortOrder: number; isActive: boolean; version: number }): Observable<ItemAttribute> {
    return this.put(id, { ...body, name: body.name.trim() });
  }
}

/** 物料屬性值（REQ_F4118.45.5–6）：整批取代，空陣列清除。 */
@Injectable({ providedIn: 'root' })
export class ItemAttributeValueService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['MD.ITEM.NOT_FOUND', 'MD.ITEM_ATTRIBUTE.NOT_FOUND', 'COMMON.CONCURRENCY.CONFLICT',
      'COMMON.PERMISSION.DENIED', 'COMMON.REQUEST.VALIDATION_FAILED']);
  }
  list(itemId: number): Observable<ItemAttributeValue[]> {
    return this.http.get<ItemAttributeValue[]>(`${ITEM_API}/${itemId}/attributes`);
  }
  replace(itemId: number, values: readonly ItemAttributeValueInput[]): Observable<ItemAttributeValue[]> {
    return this.http.put<ItemAttributeValue[]>(`${ITEM_API}/${itemId}/attributes`,
      { attributes: values.map(x => ({ attributeId: x.attributeId, value: x.value.trim() })) });
  }
}
