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
