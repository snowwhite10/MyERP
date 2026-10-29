import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesQuotationService } from './quotation.service';

describe('SD-QUOTE-046 read client contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends only the supported server filters and trims keyword', () => {
    const service = TestBed.inject(SalesQuotationService);
    const http = TestBed.inject(HttpTestingController);
    service.list({ page: 2, pageSize: 20, keyword: '  Q-1  ', from: '2028-02-01',
      to: '2028-02-29', status: 'Approved', customerId: 12, sort: '-businessDate' }).subscribe();
    const request = http.expectOne(r => r.url === '/api/v1/sal/quotations');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys().sort()).toEqual(
      ['page', 'pageSize', 'keyword', 'from', 'to', 'status', 'customerId', 'sort'].sort());
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('keyword')).toBe('Q-1');
    expect(request.request.params.get('from')).toBe('2028-02-01');
    expect(request.request.params.get('to')).toBe('2028-02-29');
    expect(request.request.params.get('status')).toBe('Approved');
    expect(request.request.params.get('customerId')).toBe('12');
    expect(request.request.params.get('sort')).toBe('-businessDate');
    request.flush({ items: [], totalCount: 0, page: 2, pageSize: 20 });
    service.list({ page: 1, pageSize: 10, keyword: '  ' }).subscribe();
    const empty = http.expectOne(r => r.url === '/api/v1/sal/quotations');
    expect(empty.request.params.keys().sort()).toEqual(['page', 'pageSize'].sort());
    expect(empty.request.params.has('keyword')).toBe(false);
    empty.flush({ items: [], totalCount: 0, page: 1, pageSize: 10 });
  });

  it('reads saved detail by id without emitting a mutation', () => {
    const service = TestBed.inject(SalesQuotationService);
    const http = TestBed.inject(HttpTestingController);
    service.get(7).subscribe();
    const request = http.expectOne('/api/v1/sal/quotations/7');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 7, lines: [], approvalHistory: [] });
  });
});
