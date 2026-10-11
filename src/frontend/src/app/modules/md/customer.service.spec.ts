import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { CustomerService } from './customer.service';

describe('MD customer API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('sends company-scoped paging, normalized create data and versioned updates', async () => {
    const api = TestBed.inject(CustomerService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list({ page: 2, pageSize: 10, keyword: ' Acme ', sort: '-name' }));
    const listed = http.expectOne((req) => req.url === '/api/v1/md/customers');
    expect(listed.request.params.get('keyword')).toBe('Acme');
    expect(listed.request.params.get('page')).toBe('2');
    expect(listed.request.params.get('sort')).toBe('-name');
    listed.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
    await list;

    const created = firstValueFrom(api.create({ code: 'acme', name: ' Acme ', categoryId: 3 }));
    const post = http.expectOne('/api/v1/md/customers');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ code: 'ACME', name: 'Acme', categoryId: 3 });
    post.flush({ id: 1, code: 'ACME', name: 'Acme', categoryId: 3, isActive: true, version: 1 });
    await created;

    const updated = firstValueFrom(api.update(1, { name: ' Other ', categoryId: null, isActive: false, version: 1 }));
    const put = http.expectOne('/api/v1/md/customers/1');
    expect(put.request.body).toEqual({ name: 'Other', categoryId: null, isActive: false, version: 1 });
    put.flush({ id: 1, code: 'ACME', name: 'Other', categoryId: null, isActive: false, version: 2 });
    await updated;
    http.verify();
  });
});
