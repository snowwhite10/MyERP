import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';

export const ITEM_CATEGORY_API = '/api/v1/md/item-categories';

/** 物料分類樹最多層數，與後端 `ItemCategory.MaxDepth` 一致。 */
export const ITEM_CATEGORY_MAX_DEPTH = 5;

export interface ItemCategory {
  id: number;
  code: string;
  name: string;
  /** null 為最上層。 */
  parentId: number | null;
  isActive: boolean;
  version: number;
}

/** 依樹狀順序展開的一列；`level` 由 0 起算。 */
export interface ItemCategoryRow extends ItemCategory {
  level: number;
  /** 本節點往下的層數（葉節點為 1）。 */
  height: number;
}

const KNOWN_CODES = ['MD.ITEM_CATEGORY.DUPLICATE_CODE', 'MD.ITEM_CATEGORY.NOT_FOUND', 'MD.ITEM_CATEGORY.INVALID_PARENT',
  'MD.ITEM_CATEGORY.CYCLE', 'MD.ITEM_CATEGORY.TOO_DEEP', 'MD.ITEM_CATEGORY.HAS_CHILDREN',
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];

/** 物料分類 API：GET 一次回傳公司全部節點，由前端組樹。 */
@Injectable({ providedIn: 'root' })
export class ItemCategoryService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string {
    return apiErrorKey(error, KNOWN_CODES);
  }

  list(): Observable<ItemCategory[]> {
    return this.http.get<ItemCategory[]>(ITEM_CATEGORY_API);
  }

  create(body: { code: string; name: string; parentId: number | null }): Observable<ItemCategory> {
    return this.http.post<ItemCategory>(ITEM_CATEGORY_API,
      { code: body.code.trim().toUpperCase(), name: body.name.trim(), parentId: body.parentId });
  }

  update(id: number, body: { name: string; isActive: boolean; parentId: number | null; version: number }): Observable<ItemCategory> {
    return this.http.put<ItemCategory>(`${ITEM_CATEGORY_API}/${id}`, { ...body, name: body.name.trim() });
  }

  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${ITEM_CATEGORY_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
}

/** 把節點按「上層 → 子層（同層按代碼）」展開；上層不在清單內的節點視為最上層。 */
export function flattenCategoryTree(items: readonly ItemCategory[]): ItemCategoryRow[] {
  const ids = new Set(items.map((item) => item.id));
  const children = new Map<number | null, ItemCategory[]>();
  for (const item of [...items].sort((a, b) => (a.code < b.code ? -1 : a.code > b.code ? 1 : 0))) {
    const parent = item.parentId !== null && ids.has(item.parentId) ? item.parentId : null;
    children.set(parent, [...(children.get(parent) ?? []), item]);
  }
  const rows: ItemCategoryRow[] = [];
  const visit = (item: ItemCategory, level: number, seen: Set<number>): number => {
    const row: ItemCategoryRow = { ...item, level, height: 1 };
    rows.push(row);
    seen.add(item.id);
    for (const child of children.get(item.id) ?? []) {
      if (!seen.has(child.id)) row.height = Math.max(row.height, visit(child, level + 1, seen) + 1);
    }
    return row.height;
  };
  const seen = new Set<number>();
  for (const root of children.get(null) ?? []) visit(root, 0, seen);
  return rows;
}

/** 關鍵字（代碼或名稱，不分大小寫）命中的節點及其所有上層；空白關鍵字回傳全部。 */
export function filterCategoryRows(rows: readonly ItemCategoryRow[], keyword: string): ItemCategoryRow[] {
  const text = keyword.trim().toLowerCase();
  if (!text) return [...rows];
  const byId = new Map(rows.map((row) => [row.id, row]));
  const keep = new Set<number>();
  for (const row of rows) {
    if (!row.code.toLowerCase().includes(text) && !row.name.toLowerCase().includes(text)) continue;
    for (let node: ItemCategoryRow | undefined = row; node && !keep.has(node.id);
      node = node.parentId === null ? undefined : byId.get(node.parentId)) keep.add(node.id);
  }
  return rows.filter((row) => keep.has(row.id));
}

/** 節點自身及所有子孫的 Id。 */
export function categorySubtreeIds(rows: readonly ItemCategoryRow[], id: number): Set<number> {
  const result = new Set([id]);
  for (const row of rows) if (row.parentId !== null && result.has(row.parentId)) result.add(row.id);
  return result;
}

/**
 * 可選的上層：啟用中（或目前的上層）、不是自己或子孫、掛上後整棵子樹不超過最多層數。
 * `rows` 必須是 `flattenCategoryTree` 的完整結果（上層在子層之前）。
 */
export function parentOptions(rows: readonly ItemCategoryRow[], editing: ItemCategoryRow | null): ItemCategoryRow[] {
  const excluded = editing ? categorySubtreeIds(rows, editing.id) : new Set<number>();
  const height = editing?.height ?? 1;
  return rows.filter((row) => !excluded.has(row.id) && (row.isActive || row.id === editing?.parentId)
    && row.level + 1 + height <= ITEM_CATEGORY_MAX_DEPTH);
}
