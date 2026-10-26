import { ParamMap, ActivatedRoute, Router } from '@angular/router';

/** 樹頁一次載入完整資料；網址只保存已提交搜尋，不接受分頁與排序。 */
export function treeKeywordFromUrl(params: ParamMap): string {
  return (params.get('keyword') ?? '').trim();
}

export function syncTreeKeywordUrl(route: ActivatedRoute | null, router: Router | null, keyword: string, replaceUrl = false): void {
  if (!route || !router) return;
  const current = route.snapshot.queryParamMap;
  const normalized = keyword || null;
  if (current.get('keyword') === normalized && !['page', 'pageSize', 'sort'].some(key => current.has(key))) return;
  void router.navigate([], { relativeTo: route, queryParams: { keyword: normalized, page: null, pageSize: null, sort: null },
    queryParamsHandling: 'merge', replaceUrl });
}
