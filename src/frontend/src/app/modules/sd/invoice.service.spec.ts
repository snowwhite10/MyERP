import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { invoiceMoney, SalesInvoiceDetail, SalesInvoiceService, SalesInvoiceSummary } from './invoice.service';

describe('SD-INV-UI-001 saved invoice reads', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends trimmed filters, customer, order, sort and paging as one GET and keeps exact amounts', () => {
    let first: SalesInvoiceSummary | undefined;
    TestBed.inject(SalesInvoiceService).list({ page: 2, pageSize: 50, keyword: ' SI1 ', from: '2028-02-01',
      to: '2028-02-29', customerId: 3, orderId: 9, sort: 'documentNo' }).subscribe(page => first = page.items[0]);
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/invoices');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('SI1');
    expect(request.request.params.get('from')).toBe('2028-02-01');
    expect(request.request.params.get('to')).toBe('2028-02-29');
    expect(request.request.params.get('customerId')).toBe('3');
    expect(request.request.params.get('orderId')).toBe('9');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.get('sort')).toBe('documentNo');
    request.flush('{"items":[{"id":1,"originalGross":900719925474099.1234,"baseGross":0.0001,"fiJournalId":77}],' +
      '"totalCount":1,"page":2,"pageSize":50}');
    expect(first?.originalGross).toBe('900719925474099.1234');
    expect(first?.baseGross).toBe('0.0001');
    expect(first?.fiJournalId).toBe(77);
  });

  it('omits empty filters', () => {
    TestBed.inject(SalesInvoiceService).list({ page: 1, pageSize: 20, keyword: '  ', from: '' }).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/invoices');
    for (const key of ['keyword', 'from', 'to', 'customerId', 'orderId', 'sort'])
      expect(request.request.params.has(key)).toBe(false);
    request.flush('{"items":[],"totalCount":0,"page":1,"pageSize":20}');
  });

  it('reads one saved invoice keeping exact rate, tax percent, quantities and amounts as text', () => {
    let saved: SalesInvoiceDetail | undefined;
    TestBed.inject(SalesInvoiceService).get(7).subscribe(value => saved = value);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/invoices/7');
    expect(request.request.method).toBe('GET');
    request.flush('{"id":7,"documentNo":"SI1","exchangeRate":31.123456,"originalNet":100.10,"originalTax":5.0050,' +
      '"originalGross":105.1050,"baseNet":3115,"baseTax":156,"baseGross":3271,"fiJournalId":88,"lines":[{"id":3,' +
      '"quantity":1.000001,"taxRatePercent":5.0000,"originalNet":100.10,"originalTax":5.0050,"originalGross":105.1050,' +
      '"baseNet":3115,"baseTax":156,"baseGross":3271}]}');
    expect(saved?.exchangeRate).toBe('31.123456');
    expect(saved?.originalTax).toBe('5.0050');
    expect(saved?.baseGross).toBe('3271');
    expect(saved?.lines[0].quantity).toBe('1.000001');
    expect(saved?.lines[0].taxRatePercent).toBe('5.0000');
    expect(saved?.fiJournalId).toBe(88);
  });

  it('rejects an invalid id before any request and surfaces JSON ProblemDetails', () => {
    const service = TestBed.inject(SalesInvoiceService);
    expect(() => service.get(0)).toThrow();
    expect(() => service.get(1.5)).toThrow();
    let failure: unknown;
    service.get(8).subscribe({ error: error => failure = error });
    TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/invoices/8')
      .flush('{"errorCode":"SAL.INVOICE.NOT_FOUND"}', { status: 404, statusText: 'Not Found' });
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect(((failure as HttpErrorResponse).error as { errorCode: string }).errorCode).toBe('SAL.INVOICE.NOT_FOUND');
  });

  it('formats money text with grouping and at least two decimals, never rounding saved digits', () => {
    expect(invoiceMoney('1234567.5')).toBe('1,234,567.50');
    expect(invoiceMoney('3271')).toBe('3,271.00');
    expect(invoiceMoney('105.1050')).toBe('105.105');
    expect(invoiceMoney('900719925474099.1234')).toBe('900,719,925,474,099.1234');
    expect(invoiceMoney('-0.5')).toBe('-0.50');
    expect(invoiceMoney('n/a')).toBe('n/a');
  });
});
