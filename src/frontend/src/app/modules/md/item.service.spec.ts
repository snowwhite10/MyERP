import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
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

  it('MD-ITEM-CONV-UI calls item conversion endpoints with JSON factors and raw quantities', () => {
    const api = TestBed.inject(ItemService);
    const http = TestBed.inject(HttpTestingController);
    const base = '/api/v1/md/items/3/uom-conversions';
    api.listConversions(3).subscribe();
    http.expectOne(base).flush([]);
    api.createConversion(3, { uomId: 2, factor: ' 123456789.123456 ' }).subscribe();
    const create = http.expectOne((r) => r.url === base && r.method === 'POST');
    expect(create.request.body).toEqual({ uomId: 2, factor: 123456789.123456 });
    expect(JSON.parse(JSON.stringify(create.request.body)).factor.toString()).toBe('123456789.123456');
    create.flush({});
    api.updateConversion(3, 9, { factor: '12', version: 4 }).subscribe();
    expect(http.expectOne(`${base}/9`).request.body).toEqual({ factor: 12, version: 4 });
    api.removeConversion(3, 9, 5).subscribe();
    expect(http.expectOne((r) => r.url === `${base}/9` && r.method === 'DELETE').request.params.get('version')).toBe('5');
    api.convert(3, 2, 1, ' -0.000001 ').subscribe();
    const convert = http.expectOne((r) => r.url === `${base}/convert`);
    expect(convert.request.params.get('quantity')).toBe('-0.000001');
    expect(convert.request.params.get('toUomId')).toBe('1');
    expect(api.errorKey(new HttpErrorResponse({ status: 409, error: { errorCode: 'MD.ITEM.BASE_UOM_LOCKED' } })))
      .toBe('errors.MD.ITEM.BASE_UOM_LOCKED');
  });
});
