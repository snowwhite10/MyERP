import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TaxService } from './tax.service';

describe('MD-TAX-SVC tax service', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('normalizes tax code bodies and passes list queries', () => {
    const api = TestBed.inject(TaxService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 50, keyword: ' vat ', sort: '-code' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/md/tax-codes');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('keyword')).toBe('vat');
    expect(list.request.params.get('sort')).toBe('-code');
    list.flush({ items: [], totalCount: 0 });

    api.create({ code: ' vat5 ', name: ' 應稅 5% ', taxType: 'Both' }).subscribe();
    expect(http.expectOne((r) => r.method === 'POST').request.body).toEqual({ code: 'VAT5', name: '應稅 5%', taxType: 'Both' });
    api.update(3, { name: ' Zero ', taxType: 'Output', isActive: false, version: 4 }).subscribe();
    expect(http.expectOne('/api/v1/md/tax-codes/3').request.body)
      .toEqual({ name: 'Zero', taxType: 'Output', isActive: false, version: 4 });
    api.remove(3, 5).subscribe();
    expect(http.expectOne((r) => r.method === 'DELETE').request.params.get('version')).toBe('5');
  });

  it('calls nested rate endpoints, calculates and maps known error codes', () => {
    const api = TestBed.inject(TaxService);
    const http = TestBed.inject(HttpTestingController);
    api.listRates(3, { page: 1, pageSize: 10, sort: '-effectiveFrom', on: '2026-02-15' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/md/tax-codes/3/rates');
    expect(list.request.params.get('on')).toBe('2026-02-15');
    list.flush({ items: [], totalCount: 0 });
    api.listRates(3, { page: 1, pageSize: 10, on: null }).subscribe();
    expect(http.expectOne((r) => r.url === '/api/v1/md/tax-codes/3/rates').request.params.has('on')).toBe(false);
    api.createRate(3, { effectiveFrom: '2026-01-01', effectiveTo: null, ratePercent: 5 }).subscribe();
    expect(http.expectOne((r) => r.method === 'POST').request.body).toEqual({ effectiveFrom: '2026-01-01', effectiveTo: null, ratePercent: 5 });
    api.updateRate(3, 8, { effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', ratePercent: 5.5, version: 2 }).subscribe();
    expect(http.expectOne('/api/v1/md/tax-codes/3/rates/8').request.body)
      .toEqual({ effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', ratePercent: 5.5, version: 2 });
    api.removeRate(3, 8, 3).subscribe();
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.url).toBe('/api/v1/md/tax-codes/3/rates/8');
    expect(del.request.params.get('version')).toBe('3');

    api.calculate({ taxCode: ' vat5 ', direction: 'Output', date: '2026-02-01', amount: 105, priceIncludesTax: true,
      currencyCode: ' twd ' }).subscribe();
    const calc = http.expectOne('/api/v1/md/tax-rates/calculate');
    expect(calc.request.body).toEqual({ taxCode: 'VAT5', direction: 'Output', date: '2026-02-01', amount: 105,
      priceIncludesTax: true, currencyCode: 'TWD' });

    const error = (code: string, status: number) => new HttpErrorResponse({ status, error: { errorCode: code } });
    expect(api.errorKey(error('MD.TAX_CODE.IN_USE', 409))).toBe('errors.MD.TAX_CODE.IN_USE');
    expect(api.errorKey(error('MD.TAX_RATE.OVERLAP', 409))).toBe('errors.MD.TAX_RATE.OVERLAP');
    expect(api.errorKey(error('MD.TAX_RATE.DIRECTION_MISMATCH', 422))).toBe('errors.MD.TAX_RATE.DIRECTION_MISMATCH');
    expect(api.errorKey(error('MD.UNKNOWN', 400))).not.toBe('errors.MD.UNKNOWN');
  });
});
