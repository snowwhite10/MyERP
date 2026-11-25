import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesSourceService } from './sales-source.service';

describe('FI-SRC-UI sales source trace API', () => {
  let api: SalesSourceService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SalesSourceService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('FI-SRC-UI-001/002 reads a company-scoped page with exact decimal text and supported filters', () => {
    let gross = '';
    api.list({ page: 2, pageSize: 10, customerId: 37, sourceType: 'Invoice', from: '2026-01-01',
      to: '2026-12-31', sort: '-id' }).subscribe(result => gross = result.items[0].grossAmount);
    const request = http.expectOne(r => r.url === '/api/v1/ar/sales-sources');
    expect(request.request.params.keys().sort()).toEqual(['customerId', 'from', 'page', 'pageSize', 'sort', 'sourceType', 'to']);
    request.flush('{"items":[{"id":9,"grossAmount":999999999999999.9999}],"totalCount":1,"page":2,"pageSize":10}');
    expect(gross).toBe('999999999999999.9999');
  });

  it('FI-SRC-UI-003 resolves a permanent key and retains allocation decimals', () => {
    let amount = '';
    api.byKey('Return', 28).subscribe(detail => amount = detail.allocations[0].baseGrossAmount);
    const request = http.expectOne('/api/v1/ar/sales-sources/by-key/Return/28');
    request.flush('{"source":{"id":8},"allocations":[{"baseGrossAmount":105.1234}]}');
    expect(amount).toBe('105.1234');
  });

  it('FI-SRC-UI-007 maps missing, validation, permission, timeout and network errors', () => {
    const err = (status: number, code?: string) => new HttpErrorResponse({ status, error: code ? { errorCode: code } : null });
    expect(api.errorKey(err(404, 'FI.SALES_SOURCE.NOT_FOUND'))).toBe('fi.salesSource.notFound');
    expect(api.errorKey(err(400))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    expect(api.errorKey(err(403))).toBe('errors.COMMON.PERMISSION.DENIED');
    expect(api.errorKey(err(0))).toBe('errors.http.network');
    expect(api.errorKey({ name: 'TimeoutError' })).toBe('fi.salesSource.timeout');
  });
});
