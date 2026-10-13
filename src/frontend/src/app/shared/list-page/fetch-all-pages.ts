import { EMPTY, expand, map, Observable, reduce } from 'rxjs';
import { PagedResult } from './table-query';

/** 下拉選項每次請求的筆數（後端 PagedRequest 上限 200）。 */
export const OPTION_PAGE_SIZE = 200;

/**
 * 依序載入全部分頁並合併成一個陣列，供下拉選項使用，避免超過一頁後無法選用或辨識既有參照（MD-F02／F03）。
 * 已載入筆數達到 totalCount，或某頁回傳空白（期間有資料被刪除）時停止。
 */
export function fetchAllPages<T>(load: (page: number) => Observable<PagedResult<T>>): Observable<T[]> {
  let page = 1;
  let loaded = 0;
  return load(page).pipe(
    expand((result) => {
      loaded += result.items.length;
      return loaded < result.totalCount && result.items.length > 0 ? load(++page) : EMPTY;
    }),
    map((result) => result.items),
    reduce((all, items) => [...all, ...items], [] as T[]),
  );
}
