import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { priceRuleDeactivateFailureKey, SalesPriceRule, SalesPriceRuleService } from './price-rule.service';

const ruleJson = '{"id":5,"itemId":7,"customerId":null,"currencyCode":"TWD","validFrom":"2028-01-01",' +
  '"validTo":"2028-12-31","unitPrice":1234567890123.123456,"discountCeiling":12.5000,"isActive":true,"version":3,' +
  '"createdAt":"2028-01-01T00:00:00","updatedAt":null,"itemCode":"I1","itemName":"Item","customerCode":null,"customerName":null}';

describe('SD-PRULE-UI-001 price rule API contract (WU-SD-005b1a)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends only the applied filters and keeps exact price digits', () => {
    let page: { items: SalesPriceRule[] } | undefined;
    TestBed.inject(SalesPriceRuleService).list({ page: 2, pageSize: 50, sort: 'id', itemId: 7,
      defaultOnly: true, currencyCode: ' USD ', asOf: '2028-02-29', active: false }).subscribe(value => page = value);
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/price-rules');
    expect(request.request.method).toBe('GET');
    const params = request.request.params;
    expect(params.keys().sort()).toEqual(['active', 'asOf', 'currencyCode', 'defaultOnly', 'itemId', 'page', 'pageSize', 'sort']);
    expect(params.get('currencyCode')).toBe('USD');
    expect(params.get('active')).toBe('false');
    expect(params.get('defaultOnly')).toBe('true');
    request.flush(`{"items":[${ruleJson}],"page":2,"pageSize":50,"totalCount":51}`);
    expect(page!.items[0].unitPrice).toBe('1234567890123.123456');
    expect(page!.items[0].discountCeiling).toBe('12.5000');
  });

  it('never sends defaultOnly together with a customer and omits empty filters', () => {
    TestBed.inject(SalesPriceRuleService).list({ page: 1, pageSize: 20, customerId: 9, defaultOnly: true,
      currencyCode: '  ' }).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/sal/price-rules');
    expect(request.request.params.keys().sort()).toEqual(['customerId', 'page', 'pageSize']);
    request.flush('{"items":[],"page":1,"pageSize":20,"totalCount":0}');
  });

  it('SD-PRULE-FORM-008 creates and updates with exact decimal tokens and the version last read (WU-SD-005b1b2)', () => {
    const service = TestBed.inject(SalesPriceRuleService);
    const http = TestBed.inject(HttpTestingController);
    const draft = { itemId: 7, scope: 'default' as const, customerId: null, currencyCode: 'TWD', validFrom: '2028-01-01',
      validTo: '2028-12-31', unitPrice: '1234567890123.123456', discountCeiling: '12.5' };
    let created: SalesPriceRule | undefined;
    service.create(draft).subscribe(value => created = value);
    const post = http.expectOne(r => r.url === '/api/v1/sal/price-rules' && r.method === 'POST');
    expect(post.request.body).toBe('{"itemId":7,"customerId":null,"currencyCode":"TWD","validFrom":"2028-01-01",' +
      '"validTo":"2028-12-31","unitPrice":1234567890123.123456,"discountCeiling":12.5}');
    expect(post.request.headers.get('Content-Type')).toBe('application/json');
    post.flush(ruleJson);
    expect(created!.unitPrice).toBe('1234567890123.123456');
    service.update(5, 3, draft).subscribe();
    const put = http.expectOne(r => r.url === '/api/v1/sal/price-rules/5' && r.method === 'PUT');
    expect(put.request.body).toMatch(/"discountCeiling":12\.5,"version":3}$/);
    put.flush(ruleJson);
    expect(() => service.update(0, 3, draft)).toThrow();
    expect(() => service.update(5, 0, draft)).toThrow();
    expect(() => service.create({ ...draft, itemId: null })).toThrow();
  });

  it('deactivates with the version last read and rejects invalid identities before sending', () => {
    const service = TestBed.inject(SalesPriceRuleService);
    let saved: SalesPriceRule | undefined;
    service.deactivate(5, 3).subscribe(value => saved = value);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/price-rules/5/deactivate');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe('{"version":3}');
    request.flush(ruleJson.replace('"isActive":true', '"isActive":false'));
    expect(saved!.isActive).toBe(false);
    expect(() => service.deactivate(0, 1)).toThrow();
    expect(() => service.deactivate(5, 0)).toThrow();
    expect(() => service.get(-1)).toThrow();
  });

  it('names each deactivate refusal and leaves generic failures to the page', () => {
    const failure = (status: number, errorCode?: string) =>
      new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
    expect(priceRuleDeactivateFailureKey(failure(404, 'SAL.PRICE_RULE.NOT_FOUND'))).toBe('sd.priceRule.deactivateUnavailable');
    expect(priceRuleDeactivateFailureKey(failure(422, 'SAL.PRICING.RULE_INACTIVE'))).toBe('sd.priceRule.deactivateAlreadyInactive');
    expect(priceRuleDeactivateFailureKey(failure(409, 'SAL.PRICING.RULE_LOCK_TIMEOUT'))).toBe('sd.priceRule.deactivateBusy');
    expect(priceRuleDeactivateFailureKey(failure(409, 'COMMON.CONCURRENCY.CONFLICT'))).toBe('sd.priceRule.deactivateConflict');
    expect(priceRuleDeactivateFailureKey(failure(403))).toBeNull();
    expect(priceRuleDeactivateFailureKey(failure(500))).toBeNull();
    expect(priceRuleDeactivateFailureKey(new Error('x'))).toBeNull();
  });
  it('SD-PRULE-UI-013 pricing option lists send page, size and trimmed keyword only (ADR-162)', () => {
    const service = TestBed.inject(SalesPriceRuleService);
    const http = TestBed.inject(HttpTestingController);
    let items: { items: { code: string }[] } | undefined;
    service.itemOptions({ page: 2, pageSize: 10, keyword: '  bolt ' }).subscribe(value => items = value);
    const item = http.expectOne(r => r.url === '/api/v1/sal/price-rules/item-options');
    expect(item.request.params.keys().sort()).toEqual(['keyword', 'page', 'pageSize']);
    expect(item.request.params.get('keyword')).toBe('bolt');
    item.flush({ items: [{ id: 9, code: 'ITEM-9', name: 'Bolt' }], page: 2, pageSize: 10, totalCount: 11 });
    expect(items!.items[0].code).toBe('ITEM-9');
    service.customerOptions({ page: 1, pageSize: 10, keyword: '   ' }).subscribe();
    const customer = http.expectOne(r => r.url === '/api/v1/sal/price-rules/customer-options');
    expect(customer.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    customer.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    let currencies: { code: string; decimalPlaces: number }[] | undefined;
    service.currencyOptions().subscribe(value => currencies = value);
    http.expectOne('/api/v1/sal/price-rules/currency-options')
      .flush([{ code: 'TWD', name: 'Taiwan dollar', decimalPlaces: 0 }, { code: 'USD', name: 'US dollar', decimalPlaces: 2 }]);
    expect(currencies!.map(c => `${c.code}:${c.decimalPlaces}`)).toEqual(['TWD:0', 'USD:2']);
  });
});
