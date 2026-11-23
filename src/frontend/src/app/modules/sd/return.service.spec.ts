import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { newReturnRequestKey, returnDraftJson, SalesReturnCreateResult, SalesReturnDetail, SalesReturnDraftRequest,
  SalesReturnService, SalesReturnSourceOrder, SalesReturnSummary } from './return.service';

const API = '/api/v1/sal/returns';
const DRAFT: SalesReturnDraftRequest = {
  requestKey: 'k-1', businessDate: '2028-03-01', remark: '破損 "A"',
  lines: [
    { lineNo: 1, deliveryLineId: 11, warehouseId: 51, locationId: null, unbilledQuantity: '0.500000',
      billed: [{ invoiceLineId: 21, quantity: '1.000001' }] },
    { lineNo: 2, deliveryLineId: 12, warehouseId: 52, locationId: 9, unbilledQuantity: '9007199254740993', billed: [] }
  ]
};

describe('SD-RETURN-SVC sales return HTTP client (WU-SD-008b1)', () => {
  let http: HttpTestingController;
  let service: SalesReturnService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SalesReturnService);
  });
  afterEach(() => http.verify());

  it('SD-RETURN-SVC-001 lists with trimmed filters, status, order, customer, sort and paging; keeps exact amounts', () => {
    let first: SalesReturnSummary | undefined;
    service.list({ page: 2, pageSize: 50, keyword: ' SR1 ', from: '2028-02-01', to: '2028-02-29', orderId: 9,
      customerId: 3, status: 'Rejected', sort: '-documentNo' }).subscribe(page => first = page.items[0]);
    const request = http.expectOne(r => r.url === API);
    expect(request.request.method).toBe('GET');
    const params = request.request.params;
    expect([params.get('keyword'), params.get('from'), params.get('to'), params.get('orderId'),
      params.get('customerId'), params.get('status'), params.get('sort'), params.get('page'), params.get('pageSize')])
      .toEqual(['SR1', '2028-02-01', '2028-02-29', '9', '3', 'Rejected', '-documentNo', '2', '50']);
    request.flush('{"items":[{"id":1,"status":"Rejected","originalGross":900719925474099.1234,"baseGross":0.0001}],' +
      '"totalCount":1,"page":2,"pageSize":50}');
    expect([first?.originalGross, first?.baseGross, first?.status]).toEqual(['900719925474099.1234', '0.0001', 'Rejected']);

    service.list({ page: 1, pageSize: 20, keyword: '  ', from: '' }).subscribe();
    const empty = http.expectOne(r => r.url === API);
    for (const key of ['keyword', 'from', 'to', 'orderId', 'customerId', 'status', 'sort'])
      expect(empty.request.params.has(key)).toBe(false);
    empty.flush('{"items":[],"totalCount":0,"page":1,"pageSize":20}');
  });

  it('SD-RETURN-SVC-002 reads a return keeping rate, quantities, line and allocation amounts as text', () => {
    let saved: SalesReturnDetail | undefined;
    service.get(7).subscribe(value => saved = value);
    http.expectOne(`${API}/7`).flush('{"id":7,"status":"Posted","exchangeRate":31.123456,"originalNet":33.3300,' +
      '"originalGross":35.0000,"baseGross":1085,"fiCreditNoteId":9001,"version":3,"lines":[{"id":3,"quantity":1.500000,' +
      '"unbilledQuantity":0.500000,"billedQuantity":1.000000,"originalNet":33.3300,"invMovementId":800003,' +
      '"allocations":[{"id":5,"quantity":1.000000,"originalGross":35.0000,"baseTax":52}]}]}');
    expect([saved?.exchangeRate, saved?.originalNet, saved?.baseGross, saved?.fiCreditNoteId])
      .toEqual(['31.123456', '33.3300', '1085', 9001]);
    const line = saved!.lines[0];
    expect([line.quantity, line.unbilledQuantity, line.billedQuantity, line.originalNet, line.invMovementId])
      .toEqual(['1.500000', '0.500000', '1.000000', '33.3300', 800003]);
    expect([line.allocations[0].quantity, line.allocations[0].originalGross, line.allocations[0].baseTax])
      .toEqual(['1.000000', '35.0000', '52']);
  });

  it('SD-RETURN-SVC-003 reads source orders and one source with exact returnable, unbilled and creditable balances', () => {
    service.sourceOrders({ page: 1, pageSize: 10, keyword: ' SO1 ', customerId: 21, sort: 'documentNo' }).subscribe();
    const list = http.expectOne(r => r.url === `${API}/source-orders`);
    expect([list.request.params.get('keyword'), list.request.params.get('customerId'), list.request.params.get('sort')])
      .toEqual(['SO1', '21', 'documentNo']);
    list.flush({ items: [], totalCount: 0, page: 1, pageSize: 10 });
    service.sourceOrders({ page: 1, pageSize: 10, keyword: ' ' }).subscribe();
    const bare = http.expectOne(r => r.url === `${API}/source-orders`);
    expect(['keyword', 'customerId', 'sort'].some(key => bare.request.params.has(key))).toBe(false);
    bare.flush({ items: [], totalCount: 0, page: 1, pageSize: 10 });

    let source: SalesReturnSourceOrder | undefined;
    service.sourceOrder(5).subscribe(value => source = value);
    http.expectOne(`${API}/source-orders/5`).flush('{"id":5,"exchangeRate":31.000000,"lines":[{"deliveryLineId":11,' +
      '"warehouseId":51,"locationId":null,"trackingUnsupported":true,"postedQuantity":2.000000,"returnedQuantity":0.500000,' +
      '"returnableQuantity":1.500000,"invoicedQuantity":1.000000,"unbilledRemainingQuantity":0.500000,"invoiceLines":' +
      '[{"invoiceLineId":21,"quantity":1.000000,"creditedQuantity":0.000001,"remainingQuantity":0.999999}]}]}');
    const line = source!.lines[0];
    expect([source?.exchangeRate, line.postedQuantity, line.returnedQuantity, line.returnableQuantity,
      line.invoicedQuantity, line.unbilledRemainingQuantity, line.trackingUnsupported, line.locationId])
      .toEqual(['31.000000', '2.000000', '0.500000', '1.500000', '1.000000', '0.500000', true, null]);
    expect([line.invoiceLines[0].quantity, line.invoiceLines[0].creditedQuantity, line.invoiceLines[0].remainingQuantity])
      .toEqual(['1.000000', '0.000001', '0.999999']);
  });

  it('SD-RETURN-SVC-004 creates with exact number literals and tells a new Draft (201) from a replay (200)', () => {
    const results: SalesReturnCreateResult[] = [];
    service.create(DRAFT).subscribe(value => results.push(value));
    const created = http.expectOne(API);
    expect(created.request.method).toBe('POST');
    expect(created.request.headers.get('Content-Type')).toBe('application/json');
    expect(created.request.body).toBe(returnDraftJson(DRAFT));
    expect(created.request.body).toContain('"unbilledQuantity":9007199254740993,');
    expect(created.request.body).toContain('{"invoiceLineId":21,"quantity":1.000001}');
    expect(JSON.parse(created.request.body as string)).toEqual({ requestKey: 'k-1', businessDate: '2028-03-01',
      remark: '破損 "A"', lines: [
        { lineNo: 1, deliveryLineId: 11, warehouseId: 51, locationId: null, unbilledQuantity: 0.5,
          billed: [{ invoiceLineId: 21, quantity: 1.000001 }] },
        { lineNo: 2, deliveryLineId: 12, warehouseId: 52, locationId: 9, unbilledQuantity: 9007199254740992,
          billed: [] }] });
    created.flush('{"id":8,"originalGross":35.0000}', { status: 201, statusText: 'Created' });
    service.create(DRAFT).subscribe(value => results.push(value));
    http.expectOne(API).flush('{"id":8,"originalGross":35.0000}', { status: 200, statusText: 'OK' });
    expect(results.map(x => [x.saved.id, x.saved.originalGross, x.replayed])).toEqual([[8, '35.0000', false],
      [8, '35.0000', true]]);
  });

  it('SD-RETURN-SVC-005 replaces with version and draft, deletes with version, submits with version', () => {
    let replaced: SalesReturnDetail | undefined, submitted: SalesReturnDetail | undefined, deleted = false;
    service.replace(8, 3, DRAFT).subscribe(value => replaced = value);
    const put = http.expectOne(`${API}/8`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toBe(`{"version":3,"draft":${returnDraftJson(DRAFT)}}`);
    put.flush('{"id":8,"status":"Draft","version":5,"baseGross":1085.0000}');
    expect([replaced?.version, replaced?.baseGross]).toEqual([5, '1085.0000']);

    service.delete(8, 5).subscribe(() => deleted = true);
    const del = http.expectOne(r => r.url === `${API}/8`);
    expect([del.request.method, del.request.params.get('version')]).toEqual(['DELETE', '5']);
    del.flush(null, { status: 204, statusText: 'No Content' });
    expect(deleted).toBe(true);

    service.submit(9, 1).subscribe(value => submitted = value);
    const submit = http.expectOne(`${API}/9/submit`);
    expect([submit.request.method, submit.request.body]).toEqual(['POST', '{"version":1}']);
    submit.flush('{"id":9,"status":"Posted","fiCreditNoteId":77,"originalGross":0.0001}');
    expect([submitted?.status, submitted?.fiCreditNoteId, submitted?.originalGross]).toEqual(['Posted', 77, '0.0001']);
  });

  it('SD-RETURN-SVC-006 refuses invalid ids and quantity text before any request and surfaces JSON ProblemDetails', () => {
    expect(() => service.get(0)).toThrow();
    expect(() => service.sourceOrder(1.5)).toThrow();
    expect(() => service.submit(1, 0)).toThrow();
    expect(() => service.delete(-1, 1)).toThrow();
    expect(() => returnDraftJson({ ...DRAFT, lines: [{ ...DRAFT.lines[0], unbilledQuantity: '1e3' }] })).toThrow();
    expect(() => returnDraftJson({ ...DRAFT, lines: [{ ...DRAFT.lines[0], billed: [{ invoiceLineId: 21,
      quantity: '-1' }] }] })).toThrow();
    expect(() => returnDraftJson({ ...DRAFT, lines: [{ ...DRAFT.lines[0], locationId: 0 }] })).toThrow();

    let failure: unknown;
    service.submit(8, 2).subscribe({ error: error => failure = error });
    http.expectOne(`${API}/8/submit`).flush('{"errorCode":"SAL.RETURN.RETURN_EXCEEDED","lineNo":2,"remaining":0.5}',
      { status: 422, statusText: 'Unprocessable Entity' });
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect((failure as HttpErrorResponse).error).toEqual({ errorCode: 'SAL.RETURN.RETURN_EXCEEDED', lineNo: 2,
      remaining: 0.5 });
    expect(newReturnRequestKey()).toMatch(/^[A-Za-z0-9_-]{1,64}$/);
  });
});
