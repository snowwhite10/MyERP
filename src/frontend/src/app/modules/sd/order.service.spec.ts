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

  it('reads editable line decimals as text before JSON parsing rounds them', () => {
    const service = TestBed.inject(SalesOrderService);
    let quantity = '';
    service.getEditable(7).subscribe(saved => quantity = saved.lines[0].quantity);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/orders/7');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('text');
    request.flush('{"id":7,"version":2,"lines":[{"quantity":9007199254740.123456,"unitPrice":0.000001,"discountRate":0.1234}]}');
    expect(quantity).toBe('9007199254740.123456');
    expect(() => service.getEditable(0)).toThrow();
  });

  it('does not rewrite property-like text inside a saved remark', () => {
    let remark = '';
    TestBed.inject(SalesOrderService).getEditable(7).subscribe(saved => remark = saved.remark ?? '');
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/orders/7');
    request.flush('{"id":7,"remark":"note \\"quantity\\":42","lines":[{"quantity":1.000001}]}');
    expect(remark).toBe('note "quantity":42');
  });

  it('replaces one saved order with its version and exact decimal tokens', () => {
    const service = TestBed.inject(SalesOrderService);
    service.replace(7, 2, draft()).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/orders/7');
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('Content-Type')).toBe('application/json');
    expect(request.request.body).toContain('"version":2');
    expect(request.request.body).toContain('"quantity":1.000001');
    expect(request.request.body).toContain('"unitPrice":0.000001');
    request.flush({ id: 7, version: 3, documentNo: 'SO-7' });
    expect(() => service.replace(0, 2, draft())).toThrow();
    expect(() => service.replace(7, 0, draft())).toThrow();
  });
});

describe('SD-ORDER-EXPORT-UI-001 export request', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends applied filters and sort without paging and reads the full blob response', () => {
    let status = 0;
    TestBed.inject(SalesOrderService).export({ keyword: ' SO1 ', from: '2028-02-29', to: '2028-03-01',
      status: 'Approved', sort: 'documentNo' }).subscribe(r => status = r.status);
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/orders/export');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.get('keyword')).toBe('SO1');
    expect(request.request.params.get('from')).toBe('2028-02-29');
    expect(request.request.params.get('status')).toBe('Approved');
    expect(request.request.params.get('sort')).toBe('documentNo');
    expect(request.request.params.has('page')).toBe(false);
    expect(request.request.params.has('pageSize')).toBe(false);
    request.flush(new Blob(['h\r\n']), { headers: { 'X-Export-Row-Count': '0' } });
    expect(status).toBe(200);
  });

  it('converts a Blob ProblemDetails error into JSON', async () => {
    const failure = new Promise<unknown>(resolve => TestBed.inject(SalesOrderService).export({})
      .subscribe({ error: (e: { error?: { errorCode?: string } }) => resolve(e.error?.errorCode) }));
    TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/orders/export')
      .flush(new Blob([JSON.stringify({ errorCode: 'SAL.ORDER.EXPORT_LIMIT' })]), { status: 422, statusText: 'Unprocessable' });
    expect(await failure).toBe('SAL.ORDER.EXPORT_LIMIT');
  });
});
