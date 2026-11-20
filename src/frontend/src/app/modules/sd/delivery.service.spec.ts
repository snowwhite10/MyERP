import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesDeliveryDetail, SalesDeliveryService } from './delivery.service';

describe('SD-DEL-UI-001 saved delivery reads', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends trimmed filters, named status, source order and paging as one GET', () => {
    let total = '';
    TestBed.inject(SalesDeliveryService).list({ page: 2, pageSize: 50, keyword: ' DH1 ', from: '2028-02-01',
      to: '2028-02-29', status: 'Posted', orderId: 9, sort: 'documentNo' }).subscribe(page => total = page.items[0].totalQuantity);
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/deliveries');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('DH1');
    expect(request.request.params.get('status')).toBe('Posted');
    expect(request.request.params.get('orderId')).toBe('9');
    expect(request.request.params.has('customerId')).toBe(false);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('sort')).toBe('documentNo');
    request.flush('{"items":[{"id":1,"totalQuantity":9007199254740.123456}],"totalCount":1,"page":2,"pageSize":50}');
    expect(total).toBe('9007199254740.123456');
  });

  it('reads one saved detail keeping exact line and tracking quantities', () => {
    let saved: SalesDeliveryDetail | undefined;
    TestBed.inject(SalesDeliveryService).get(7).subscribe(value => saved = value);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/deliveries/7');
    expect(request.request.method).toBe('GET');
    request.flush('{"id":7,"status":"Posted","remark":"note \\"quantity\\":42","lines":[{"id":3,"quantity":1.000001,' +
      '"invMovementId":55,"tracking":[{"seqNo":1,"lotNo":"L1","serialNo":null,"quantity":0.000001}]}],"approvalHistory":[]}');
    expect(saved?.lines[0].quantity).toBe('1.000001');
    expect(saved?.lines[0].tracking[0].quantity).toBe('0.000001');
    expect(saved?.lines[0].invMovementId).toBe(55);
    expect(saved?.remark).toBe('note "quantity":42');
  });

  it('rejects an invalid id before any request and surfaces JSON ProblemDetails', () => {
    const service = TestBed.inject(SalesDeliveryService);
    expect(() => service.get(0)).toThrow();
    let failure: unknown;
    service.get(8).subscribe({ error: error => failure = error });
    TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/deliveries/8')
      .flush('{"errorCode":"SAL.DELIVERY.NOT_FOUND"}', { status: 404, statusText: 'Not Found' });
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect(((failure as HttpErrorResponse).error as { errorCode: string }).errorCode).toBe('SAL.DELIVERY.NOT_FOUND');
  });
});

describe('SD-DEL-ED-001 delivery draft replace and delete requests', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  const draft = { orderId: 9, businessDate: '2028-02-29', lines: [{ lineNo: 1, orderLineId: 21, warehouseId: 4, locationId: null,
    quantity: '9999999999999.999999', tracking: [{ lotNo: 'L-1', serialNo: null, quantity: '0.5' }] }] };

  it('PUTs {version, draft} with exact decimal tokens and saved tracking, reading the reply as exact text', () => {
    let quantity = '';
    TestBed.inject(SalesDeliveryService).replace(61, 3, draft).subscribe(value => quantity = value.lines[0].quantity);
    const request = TestBed.inject(HttpTestingController).expectOne(`${'/api/v1/sal/deliveries'}/61`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toBe('{"version":3,"draft":{"orderId":9,"businessDate":"2028-02-29","lines":[{"lineNo":1,' +
      '"orderLineId":21,"warehouseId":4,"locationId":null,"quantity":9999999999999.999999,' +
      '"tracking":[{"lotNo":"L-1","serialNo":null,"quantity":0.5}]}]}}');
    expect(request.request.headers.get('Content-Type')).toBe('application/json');
    request.flush('{"id":61,"version":4,"lines":[{"quantity":9999999999999.999999}]}');
    expect(quantity).toBe('9999999999999.999999');
  });

  it('DELETEs with the version as a query parameter', () => {
    let done = false;
    TestBed.inject(SalesDeliveryService).remove(61, 4).subscribe({ complete: () => done = true });
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/deliveries/61');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.params.get('version')).toBe('4');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
  });

  it('refuses an invalid id or version before any request', () => {
    const service = TestBed.inject(SalesDeliveryService);
    expect(() => service.replace(0, 1, draft)).toThrow();
    expect(() => service.replace(61, 0, draft)).toThrow();
    expect(() => service.remove(61, 1.5)).toThrow();
    expect(() => service.remove(-1, 1)).toThrow();
  });

  it('SD-DEL-ACT-001 POSTs submit and withdraw with only the loaded version and reads the exact reply', () => {
    const service = TestBed.inject(SalesDeliveryService);
    const http = TestBed.inject(HttpTestingController);
    let submitted = '', withdrawn = '';
    service.submit(61, 4).subscribe(value => submitted = `${value.status}:${value.lines[0].quantity}`);
    const submit = http.expectOne('/api/v1/sal/deliveries/61/submit');
    expect(submit.request.method).toBe('POST');
    expect(submit.request.body).toBe('{"version":4}');
    expect(submit.request.headers.get('Content-Type')).toBe('application/json');
    submit.flush('{"id":61,"status":"Pending","version":5,"lines":[{"quantity":9999999999999.999999}]}');
    expect(submitted).toBe('Pending:9999999999999.999999');
    service.withdraw(61, 5).subscribe(value => withdrawn = value.status);
    const withdraw = http.expectOne('/api/v1/sal/deliveries/61/withdraw');
    expect(withdraw.request.method).toBe('POST');
    expect(withdraw.request.body).toBe('{"version":5}');
    withdraw.flush('{"id":61,"status":"Draft","version":6,"lines":[]}');
    expect(withdrawn).toBe('Draft');
  });

  it('SD-DEL-ACT-002 refuses an invalid submit or withdraw identity before any request', () => {
    const service = TestBed.inject(SalesDeliveryService);
    expect(() => service.submit(0, 1)).toThrow();
    expect(() => service.submit(61, 0)).toThrow();
    expect(() => service.withdraw(61, 2.5)).toThrow();
    expect(() => service.withdraw(-3, 1)).toThrow();
  });
});
