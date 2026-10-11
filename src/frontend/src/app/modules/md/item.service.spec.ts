import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ItemService } from './item.service';

describe('MD-ITEM-UI API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('searches and sorts, normalizes create, and sends version on update', () => {
    const api = TestBed.inject(ItemService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 20, keyword: '  bolt ', sort: '-name' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/md/items');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('pageSize')).toBe('20');
    expect(list.request.params.get('keyword')).toBe('bolt');
    expect(list.request.params.get('sort')).toBe('-name');
    list.flush({ items: [], totalCount: 0 });
    api.create({ code: ' bolt ', name: ' Bolt ', specification: ' M8 ', baseUomId: 1,
      brandId: null, categoryId: 2 }).subscribe();
    const create = http.expectOne('/api/v1/md/items');
    expect(create.request.body).toEqual({ code: 'BOLT', name: 'Bolt', specification: 'M8', baseUomId: 1,
      brandId: null, categoryId: 2 });
    create.flush({});
    api.update(3, { name: ' Bolt ', specification: '', baseUomId: 1, brandId: null,
      categoryId: null, isActive: false, version: 4 }).subscribe();
    const update = http.expectOne('/api/v1/md/items/3');
    expect(update.request.body).toMatchObject({ name: 'Bolt', specification: null, version: 4 });
    update.flush({});
  });
});
