import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesOrderService } from './order.service';

describe('SD-ORDER-UI-001 saved order reads', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends named status, scoped filters and stable paging without a mutation', () => {
    const service = TestBed.inject(SalesOrderService);
    service.list({ page: 2, pageSize: 20, keyword: ' SO1 ', from: '2028-02-01',
      to: '2028-02-29', status: 'Approved', sort: '-businessDate' }).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/orders');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('SO1');
    expect(request.request.params.get('status')).toBe('Approved');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('sort')).toBe('-businessDate');
    request.flush({ items: [], totalCount: 0, page: 2, pageSize: 20 });
  });

  it('reads only the saved detail for its id', () => {
    TestBed.inject(SalesOrderService).get(7).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/orders/7');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 7, lines: [] });
  });
});
