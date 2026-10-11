import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { CurrencyService } from './currency.service';

describe('MD-CUR-SVC currency service', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('normalizes currency bodies and passes list queries', () => {
    const api = TestBed.inject(CurrencyService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 50, keyword: ' usd ', sort: '-code' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/md/currencies');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('keyword')).toBe('usd');
    expect(list.request.params.get('sort')).toBe('-code');
    list.flush({ items: [], totalCount: 0 });

    api.create({ code: ' usd ', name: ' US Dollar ', symbol: '  ', decimalPlaces: 2, isBase: false }).subscribe();
    expect(http.expectOne((r) => r.method === 'POST').request.body)
      .toEqual({ code: 'USD', name: 'US Dollar', symbol: null, decimalPlaces: 2, isBase: false });
    api.update(3, { name: 'Dollar', symbol: ' $ ', decimalPlaces: 3, isBase: true, isActive: true, version: 4 }).subscribe();
    expect(http.expectOne('/api/v1/md/currencies/3').request.body)
      .toEqual({ name: 'Dollar', symbol: '$', decimalPlaces: 3, isBase: true, isActive: true, version: 4 });
    api.remove(3, 5).subscribe();
    expect(http.expectOne((r) => r.method === 'DELETE').request.params.get('version')).toBe('5');
  });

  it('calls nested rate endpoints with the date filter and maps known error codes', () => {
    const api = TestBed.inject(CurrencyService);
    const http = TestBed.inject(HttpTestingController);
    api.listRates(3, { page: 1, pageSize: 10, sort: '-effectiveFrom', on: '2026-02-15' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/md/currencies/3/rates');
    expect(list.request.params.get('on')).toBe('2026-02-15');
    expect(list.request.params.get('sort')).toBe('-effectiveFrom');
    list.flush({ items: [], totalCount: 0 });
    api.listRates(3, { page: 1, pageSize: 10, on: null }).subscribe();
    expect(http.expectOne((r) => r.url === '/api/v1/md/currencies/3/rates').request.params.has('on')).toBe(false);
    api.createRate(3, { effectiveFrom: '2026-01-01', effectiveTo: null, rate: 32.15 }).subscribe();
    expect(http.expectOne((r) => r.method === 'POST').request.body).toEqual({ effectiveFrom: '2026-01-01', effectiveTo: null, rate: 32.15 });
    api.updateRate(3, 8, { effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', rate: 31.5, version: 2 }).subscribe();
    expect(http.expectOne('/api/v1/md/currencies/3/rates/8').request.body)
      .toEqual({ effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', rate: 31.5, version: 2 });
    api.removeRate(3, 8, 3).subscribe();
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.url).toBe('/api/v1/md/currencies/3/rates/8');
    expect(del.request.params.get('version')).toBe('3');

    const error = (code: string, status: number) => new HttpErrorResponse({ status, error: { errorCode: code } });
    expect(api.errorKey(error('MD.CURRENCY.BASE_LOCKED', 409))).toBe('errors.MD.CURRENCY.BASE_LOCKED');
    expect(api.errorKey(error('MD.EXCHANGE_RATE.OVERLAP', 409))).toBe('errors.MD.EXCHANGE_RATE.OVERLAP');
    expect(api.errorKey(error('MD.EXCHANGE_RATE.NO_BASE_CURRENCY', 422))).toBe('errors.MD.EXCHANGE_RATE.NO_BASE_CURRENCY');
    expect(api.errorKey(error('MD.UNKNOWN', 400))).not.toBe('errors.MD.UNKNOWN');
  });
});
