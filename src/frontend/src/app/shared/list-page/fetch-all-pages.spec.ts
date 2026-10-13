import { Observable, of, throwError } from 'rxjs';
import { fetchAllPages, OPTION_PAGE_SIZE } from './fetch-all-pages';
import { PagedResult } from './table-query';

function pages(totalCount: number, pageSize: number, items: (page: number) => number[]) {
  const requested: number[] = [];
  const load = (page: number): Observable<PagedResult<number>> => {
    requested.push(page);
    return of({ items: items(page), page, pageSize, totalCount });
  };
  return { requested, load };
}

describe('MD-F03 fetchAllPages', () => {
  it('requests following pages until every item is loaded and concatenates them in order', () => {
    const source = pages(5, 2, (page) => [page * 10, page * 10 + 1].slice(0, page === 3 ? 1 : 2));
    let result: number[] = [];
    fetchAllPages(source.load).subscribe((items) => (result = items));
    expect(source.requested).toEqual([1, 2, 3]);
    expect(result).toEqual([10, 11, 20, 21, 30]);
  });

  it('stops after the first page when it already holds every item', () => {
    const source = pages(2, OPTION_PAGE_SIZE, () => [1, 2]);
    let result: number[] = [];
    fetchAllPages(source.load).subscribe((items) => (result = items));
    expect(source.requested).toEqual([1]);
    expect(result).toEqual([1, 2]);
  });

  it('stops on an empty page even when the total says more rows exist (rows deleted meanwhile)', () => {
    const source = pages(10, 2, (page) => (page === 1 ? [1, 2] : []));
    let result: number[] = [];
    fetchAllPages(source.load).subscribe((items) => (result = items));
    expect(source.requested).toEqual([1, 2]);
    expect(result).toEqual([1, 2]);
  });

  it('propagates errors so the caller can clear its options', () => {
    let failed = false;
    fetchAllPages(() => throwError(() => new Error('boom'))).subscribe({ error: () => (failed = true) });
    expect(failed).toBe(true);
  });
});
