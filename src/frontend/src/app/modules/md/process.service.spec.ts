import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { normalizeProcessDraft, PROCESS_API, PROCESS_CATEGORY_API, ProcessCategoryService, ProcessService, RATE_PATTERN } from './process.service';

describe('MD-PROC-UI-001 process API client', () => {
  it('accepts rates of 0 or more with up to 9 integer and 6 decimal digits', () => {
    for (const ok of ['0', '12.5', '999999999.123456']) expect(RATE_PATTERN.test(ok)).toBe(true);
    for (const bad of ['-1', '1.1234567', '1000000000', '', '1e3', '.5']) expect(RATE_PATTERN.test(bad)).toBe(false);
  });

  it('normalizes code and name and converts the rate text to a number', () => {
    expect(normalizeProcessDraft({ code: ' cut-1 ', name: ' Cutting ', categoryId: 2, uomId: null,
      standardRate: ' 1.25 ', sortOrder: 3 })).toEqual({ code: 'CUT-1', name: 'Cutting', categoryId: 2,
      uomId: null, standardRate: 1.25, sortOrder: 3 });
  });

  it('sends the category filter and does not send immutable codes on update', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const processes = TestBed.inject(ProcessService);
    processes.list({ page: 1, pageSize: 20, keyword: ' cut ', sort: 'sortOrder', categoryId: 7 }).subscribe();
    const list = http.expectOne(r => r.url === PROCESS_API);
    expect(list.request.params.toString()).toBe('page=1&pageSize=20&keyword=cut&sort=sortOrder&categoryId=7');
    list.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    processes.update(3, { code: 'OLD', name: ' Cut ', categoryId: null, uomId: 4, standardRate: '2', sortOrder: 1,
      isActive: false, version: 5 }).subscribe();
    const put = http.expectOne(`${PROCESS_API}/3`);
    expect(put.request.body).toEqual({ name: 'Cut', categoryId: null, uomId: 4, standardRate: 2, sortOrder: 1,
      isActive: false, version: 5 });
    put.flush({});
    const categories = TestBed.inject(ProcessCategoryService);
    categories.create({ code: ' asm ', name: ' Assembly ', sortOrder: 10 }).subscribe();
    const post = http.expectOne(PROCESS_CATEGORY_API);
    expect(post.request.body).toEqual({ code: 'ASM', name: 'Assembly', sortOrder: 10 });
    post.flush({});
    categories.update(8, { name: ' Paint ', sortOrder: 2, isActive: true, version: 1 }).subscribe();
    expect(http.expectOne(`${PROCESS_CATEGORY_API}/8`).request.body).toEqual({ name: 'Paint', sortOrder: 2, isActive: true, version: 1 });
    categories.list({ page: 1, pageSize: 20, categoryId: null }).subscribe();
    expect(http.expectOne(r => r.url === PROCESS_CATEGORY_API && r.method === 'GET').request.params.has('categoryId')).toBe(false);
  });

  it('maps known error codes to translation keys', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const categories = TestBed.inject(ProcessCategoryService);
    const problem = (errorCode: string) => new HttpErrorResponse({ status: 409, error: { errorCode } });
    expect(categories.errorKey(problem('MD.PROCESS_CATEGORY.HAS_PROCESSES'))).toBe('errors.MD.PROCESS_CATEGORY.HAS_PROCESSES');
    expect(TestBed.inject(ProcessService).errorKey(problem('MD.PROCESS.IN_USE'))).toBe('errors.MD.PROCESS.IN_USE');
  });
});
