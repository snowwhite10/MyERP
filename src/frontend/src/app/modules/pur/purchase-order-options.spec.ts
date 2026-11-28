import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PurchaseOrderDraft, PurchaseOrderService, purchaseOrderBody } from './purchase-order.service';

const API = '/api/v1/pur/purchase-orders';
const draft = (): PurchaseOrderDraft => ({ supplierId: 5, businessDate: '2028-02-29', currencyCode: ' usd ', remark: '  ',
  lines: [{ itemId: 7, requisitionLineId: 44, quantity: ' 9999999999999.999999 ', unitPrice: '0.000001', taxRateId: 3 },
    { itemId: 8, requisitionLineId: null, quantity: '1', unitPrice: '12.5', taxRateId: 4 }] });

describe('PUR-PO-OPT form client (WU-PUR-006f2a)', () => {
  let service: PurchaseOrderService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PurchaseOrderService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('PUR-PO-OPT-005 body numbers lines in screen order, sends exact decimal text and the header currency, never amounts', () => {
    expect(purchaseOrderBody(draft())).toEqual({ supplierId: 5, businessDate: '2028-02-29', remark: null, lines: [
      { lineNo: 1, itemId: 7, requisitionLineId: 44, quantity: '9999999999999.999999', unitPrice: '0.000001', currencyCode: 'USD',
        taxRateId: 3 },
      { lineNo: 2, itemId: 8, requisitionLineId: null, quantity: '1', unitPrice: '12.5', currencyCode: 'USD', taxRateId: 4 }] });
  });

  it('PUR-PO-OPT-001 option reads use the ORDER endpoints with paging, trimmed keyword and the optional item filter', () => {
    const results: unknown[] = [];
    service.supplierOptions({ page: 2, pageSize: 10, keyword: ' acme ' }).subscribe(x => results.push(x));
    service.itemOptions({ page: 1, pageSize: 20, keyword: '' }).subscribe(x => results.push(x));
    service.requisitionOptions({ page: 1, pageSize: 20, keyword: 'PR-' }, 7).subscribe(x => results.push(x));
    service.requisitionOptions({ page: 1, pageSize: 20, keyword: '' }, null).subscribe(x => results.push(x));
    const supplier = http.expectOne(r => r.url === `${API}/supplier-options`);
    expect(supplier.request.params.toString()).toBe('page=2&pageSize=10&keyword=acme');
    supplier.flush('{"items":[],"totalCount":0,"page":2,"pageSize":10}');
    const item = http.expectOne(r => r.url === `${API}/item-options`);
    expect(item.request.params.has('keyword')).toBe(false);
    item.flush('{"items":[],"totalCount":0,"page":1,"pageSize":20}');
    const [filtered, all] = http.match(r => r.url === `${API}/requisition-options`);
    expect(filtered.request.params.toString()).toBe('page=1&pageSize=20&keyword=PR-&itemId=7');
    expect(all.request.params.has('itemId')).toBe(false);
    filtered.flush('{"items":[{"id":44,"requisitionId":4,"documentNo":"PR-4","lineNo":2,"itemId":7,"requiredDate":"2028-03-02",'
      + '"quantity":5.123456,"orderedQuantity":2.000000,"remainingQuantity":3.123456}],"totalCount":1,"page":1,"pageSize":20}');
    all.flush('{"items":[],"totalCount":0,"page":1,"pageSize":20}');
    expect((results[2] as { items: unknown[] }).items[0]).toEqual({ id: 44, requisitionId: 4, documentNo: 'PR-4', lineNo: 2,
      itemId: 7, requiredDate: '2028-03-02', quantity: '5.123456', orderedQuantity: '2.000000', remainingQuantity: '3.123456' });
  });

  it('PUR-PO-OPT-002 create context is read for the business date with tax rates kept as exact text', () => {
    let context: unknown;
    service.createContext('2028-02-29').subscribe(x => context = x);
    const request = http.expectOne(r => r.url === `${API}/create-context`);
    expect(request.request.params.get('businessDate')).toBe('2028-02-29');
    request.flush('{"businessDate":"2028-02-29","currencies":[{"code":"TWD","name":"NT"}],'
      + '"taxRates":[{"id":3,"code":"IN","name":"Input","ratePercent":5.00}]}');
    expect(context).toEqual({ businessDate: '2028-02-29', currencies: [{ code: 'TWD', name: 'NT' }],
      taxRates: [{ id: 3, code: 'IN', name: 'Input', ratePercent: '5.00' }] });
  });

  it('PUR-PO-OPT-005 create posts the body once and returns saved amounts as exact text; 422 keeps the MD reason', () => {
    let saved: { documentNo: string; baseGross: string } | undefined;
    service.create(draft()).subscribe(x => saved = x);
    const post = http.expectOne(API);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual(purchaseOrderBody(draft()));
    post.flush('{"id":9,"documentNo":"PO-9","status":"Draft","version":1,"supplierId":5,"businessDate":"2028-02-29",'
      + '"currency":"USD","baseCurrencyCode":"TWD","exchangeRate":31.123456,"baseNet":10.10,"baseTax":0.51,"baseGross":10.61,'
      + '"remark":null,"lines":[]}', { status: 201, statusText: 'Created' });
    expect(saved?.documentNo).toBe('PO-9');
    expect(saved?.baseGross).toBe('10.61');

    let failure: unknown;
    service.create(draft()).subscribe({ error: e => failure = e });
    http.expectOne(API).flush('{"errorCode":"MD.SUPPLIER.NOT_APPROVED","traceId":"t-1"}',
      { status: 422, statusText: 'Unprocessable' });
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect(service.errorKey(failure)).toBe('errors.MD.SUPPLIER.NOT_APPROVED');
  });
});
