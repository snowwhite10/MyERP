import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { CustomerCategoryService } from './customer-category.service';

describe('MD customer category API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('sends paging, normalized create data, versioned update and delete', async () => {
    const api = TestBed.inject(CustomerCategoryService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list({ page: 2, pageSize: 10, keyword: ' Retail ', sort: '-name' }));
    const listed = http.expectOne((req) => req.url === '/api/v1/md/customer-categories');
    expect(listed.request.params.get('keyword')).toBe('Retail');
    expect(listed.request.params.get('page')).toBe('2');
    expect(listed.request.params.get('sort')).toBe('-name');
    listed.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
    await list;

    const created = firstValueFrom(api.create({ code: 'retail', name: 'Retail' }));
    const post = http.expectOne('/api/v1/md/customer-categories');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ code: 'RETAIL', name: 'Retail' });
    post.flush({ id: 1, code: 'RETAIL', name: 'Retail', isActive: true, version: 1 });
    await created;

    const updated = firstValueFrom(api.update(1, { name: 'Wholesale', isActive: false, version: 1 }));
    const put = http.expectOne('/api/v1/md/customer-categories/1');
    expect(put.request.body.version).toBe(1);
    put.flush({ id: 1, code: 'RETAIL', name: 'Wholesale', isActive: false, version: 2 });
    await updated;

    const removed = firstValueFrom(api.remove(1, 2));
    const del = http.expectOne('/api/v1/md/customer-categories/1?version=2');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    await removed;
    http.verify();
  });
});
