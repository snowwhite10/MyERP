import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesOrderService } from './order.service';
import { OrderDraftForm } from './order-draft-form';

const draft = (): OrderDraftForm => ({ customerId: 7, businessDate: '2028-02-29',
  currencyCode: 'USD', remark: '', lines: [{ lineNo: 1, itemId: 12,
    quantity: '1.000001', unitPrice: '0.000001', discountRate: '0', taxRateId: 4 }] });

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

  it('re-reads a newly created order through the CREATE-scoped receipt endpoint', () => {
    TestBed.inject(SalesOrderService).getCreated(7).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/orders/7/created');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 7, documentNo: 'SO-7', lines: [] });
    expect(() => TestBed.inject(SalesOrderService).getCreated(0)).toThrow();
  });

  it('posts exact manual decimal tokens and converts only a positive quotation id', () => {
    const service = TestBed.inject(SalesOrderService);
    service.create(draft()).subscribe();
    const manual = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/orders');
    expect(manual.request.method).toBe('POST');
    expect(manual.request.body).toContain('"quantity":1.000001');
    expect(manual.request.body).toContain('"unitPrice":0.000001');
    manual.flush({ id: 8, documentNo: 'SO-1' });

    service.convertQuotation(5).subscribe();
    const converted = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/orders/from-quotation/5');
    expect(converted.request.method).toBe('POST');
    converted.flush({ id: 9, documentNo: 'SO-2' });
    expect(() => service.convertQuotation(0)).toThrow();
  });
});
