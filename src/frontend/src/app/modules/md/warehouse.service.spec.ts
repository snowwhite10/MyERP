import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { WarehouseService } from './warehouse.service';

describe('MD-WH-SVC warehouse service', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('normalizes warehouse bodies and passes list queries', () => {
    const api = TestBed.inject(WarehouseService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 50, keyword: ' main ', sort: '-name' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/md/warehouses');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('keyword')).toBe('main');
    expect(list.request.params.get('sort')).toBe('-name');
    list.flush({ items: [], totalCount: 0 });

    api.create({ code: ' wh-1 ', name: ' Main ', address: '  ', contactName: ' Amy ', phone: '' }).subscribe();
    expect(http.expectOne((r) => r.method === 'POST').request.body)
      .toEqual({ code: 'WH-1', name: 'Main', address: null, contactName: 'Amy', phone: null });
    api.update(3, { name: 'Main', address: ' Taipei ', contactName: null, phone: '02', isActive: false, version: 4 }).subscribe();
    expect(http.expectOne('/api/v1/md/warehouses/3').request.body)
      .toEqual({ name: 'Main', address: 'Taipei', contactName: null, phone: '02', isActive: false, version: 4 });
    api.remove(3, 5).subscribe();
    expect(http.expectOne((r) => r.method === 'DELETE').request.params.get('version')).toBe('5');
  });

  it('calls nested location endpoints and maps known error codes', () => {
    const api = TestBed.inject(WarehouseService);
    const http = TestBed.inject(HttpTestingController);
    api.listLocations(3, { page: 1, pageSize: 10, keyword: '', sort: 'code' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/md/warehouses/3/locations');
    expect(list.request.params.has('keyword')).toBe(false);
    list.flush({ items: [], totalCount: 0 });
    api.createLocation(3, { code: ' a-01 ', name: ' Shelf ' }).subscribe();
    expect(http.expectOne((r) => r.method === 'POST').request.body).toEqual({ code: 'A-01', name: 'Shelf' });
    api.updateLocation(3, 8, { name: ' Shelf 2 ', isActive: false, version: 2 }).subscribe();
    expect(http.expectOne('/api/v1/md/warehouses/3/locations/8').request.body).toEqual({ name: 'Shelf 2', isActive: false, version: 2 });
    api.removeLocation(3, 8, 3).subscribe();
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.url).toBe('/api/v1/md/warehouses/3/locations/8');
    expect(del.request.params.get('version')).toBe('3');

    const error = (code: string, status: number) => new HttpErrorResponse({ status, error: { errorCode: code } });
    expect(api.errorKey(error('MD.WAREHOUSE.HAS_LOCATIONS', 409))).toBe('errors.MD.WAREHOUSE.HAS_LOCATIONS');
    expect(api.errorKey(error('MD.STORAGE_LOCATION.WAREHOUSE_INACTIVE', 422))).toBe('errors.MD.STORAGE_LOCATION.WAREHOUSE_INACTIVE');
    expect(api.errorKey(error('MD.UNKNOWN', 400))).not.toBe('errors.MD.UNKNOWN');
  });
});
