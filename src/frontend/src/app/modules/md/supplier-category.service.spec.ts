import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SupplierCategoryService } from './supplier-category.service';

describe('MD supplier category API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('sends paging, normalized create data, versioned update and delete', async () => {
    const api = TestBed.inject(SupplierCategoryService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list({ page: 1, pageSize: 20, keyword: '  ', sort: 'name' }));
    const listed = http.expectOne((req) => req.url === '/api/v1/md/supplier-categories');
    expect(listed.request.params.has('keyword')).toBe(false);
    expect(listed.request.params.get('sort')).toBe('name');
    listed.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await list;

    const created = firstValueFrom(api.create({ code: ' raw-01 ', name: ' Raw ' }));
    const post = http.expectOne('/api/v1/md/supplier-categories');
    expect(post.request.body).toEqual({ code: 'RAW-01', name: 'Raw' });
    post.flush({ id: 5, code: 'RAW-01', name: 'Raw', isActive: true, version: 1 });
    await created;

    const updated = firstValueFrom(api.update(5, { name: ' Service ', isActive: false, version: 1 }));
    const put = http.expectOne('/api/v1/md/supplier-categories/5');
    expect(put.request.body).toEqual({ name: 'Service', isActive: false, version: 1 });
    put.flush({ id: 5, code: 'RAW-01', name: 'Service', isActive: false, version: 2 });
    await updated;

    const removed = firstValueFrom(api.remove(5, 2));
    http.expectOne('/api/v1/md/supplier-categories/5?version=2').flush(null);
    await removed;
    http.verify();
  });

  it('maps only supplier category error codes', () => {
    const api = TestBed.inject(SupplierCategoryService);
    const problem = (errorCode: string) => new HttpErrorResponse({ status: 409, error: { errorCode } });
    expect(api.errorKey(problem('MD.SUPPLIER_CATEGORY.DUPLICATE_CODE'))).toBe('errors.MD.SUPPLIER_CATEGORY.DUPLICATE_CODE');
    expect(api.errorKey(problem('MD.CUSTOMER_CATEGORY.DUPLICATE_CODE'))).toBe('errors.http.unknown');
  });
});
