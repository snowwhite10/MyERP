import { NzTableQueryParams } from 'ng-zorro-antd/table';

/** 伺服器端分頁結果（api-conventions 第 5 節）。 */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/** 標準列表頁的每頁筆數選項（ui-conventions 第 5 節）。 */
export const PAGE_SIZE_OPTIONS = [10, 20, 50, 100, 200] as const;

/**
 * `nz-table` 的排序轉成 API 的 `sort` 參數：升冪 `column`、降冪 `-column`；
 * 只接受白名單欄位，沒有排序時用 `fallback`。
 */
export function tableSort(params: NzTableQueryParams, sortable: readonly string[], fallback: string): string {
  const sorted = params.sort.find((s) => s.value !== null && sortable.includes(s.key));
  return sorted ? `${sorted.value === 'descend' ? '-' : ''}${sorted.key}` : fallback;
}

/** API 的 `sort` 參數轉回欄位的 `nzSortOrder`。 */
export function sortOrderOf(sort: string, column: string): 'ascend' | 'descend' | null {
  if (sort === column) return 'ascend';
  return sort === `-${column}` ? 'descend' : null;
}

/** 列表目前的分頁與排序。 */
export interface TableState {
  page: number;
  pageSize: number;
  sort: string;
}

/** 由 `nzQueryParams` 算出新的分頁與排序；和目前相同時回傳 null（不必重新查詢）。 */
export function nextTableState(
  params: NzTableQueryParams,
  current: TableState,
  sortable: readonly string[],
  fallback: string,
): TableState | null {
  const sort = tableSort(params, sortable, fallback);
  if (params.pageIndex === current.page && params.pageSize === current.pageSize && sort === current.sort) return null;
  return { page: params.pageIndex, pageSize: params.pageSize, sort };
}
