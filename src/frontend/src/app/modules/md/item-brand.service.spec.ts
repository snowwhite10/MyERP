import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ItemBrandService } from './item-brand.service';

describe('MD item brand API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('sends paging, normalized create data, versioned update and delete', async () => {
    const api = TestBed.inject(ItemBrandService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list({ page: 2, pageSize: 50, keyword: ' acme ', sort: '-code' }));
    const listed = http.expectOne((req) => req.url === '/api/v1/md/item-brands');
    expect(listed.request.params.get('keyword')).toBe('acme');
    expect(listed.request.params.get('page')).toBe('2');
    listed.flush({ items: [], page: 2, pageSize: 50, totalCount: 0 });
    await list;

    const created = firstValueFrom(api.create({ code: ' acme ', name: ' Acme ' }));
    const post = http.expectOne('/api/v1/md/item-brands');
    expect(post.request.body).toEqual({ code: 'ACME', name: 'Acme' });
    post.flush({ id: 7, code: 'ACME', name: 'Acme', isActive: true, version: 1 });
    await created;

    const updated = firstValueFrom(api.update(7, { name: ' Acme Ltd ', isActive: false, version: 1 }));
    const put = http.expectOne('/api/v1/md/item-brands/7');
    expect(put.request.body).toEqual({ name: 'Acme Ltd', isActive: false, version: 1 });
    put.flush({ id: 7, code: 'ACME', name: 'Acme Ltd', isActive: false, version: 2 });
    await updated;

    const removed = firstValueFrom(api.remove(7, 2));
    http.expectOne('/api/v1/md/item-brands/7?version=2').flush(null);
    await removed;
    http.verify();
  });

  it('maps only item brand error codes', () => {
    const api = TestBed.inject(ItemBrandService);
    const problem = (errorCode: string) => new HttpErrorResponse({ status: 409, error: { errorCode } });
    expect(api.errorKey(problem('MD.ITEM_BRAND.DUPLICATE_CODE'))).toBe('errors.MD.ITEM_BRAND.DUPLICATE_CODE');
    expect(api.errorKey(problem('MD.ITEM_CATEGORY.DUPLICATE_CODE'))).toBe('errors.http.unknown');
  });
});
