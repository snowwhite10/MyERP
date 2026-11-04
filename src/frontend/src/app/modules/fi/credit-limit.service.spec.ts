import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CREDIT_AMOUNT_PATTERN, CreditLimitService, formatCreditAmount } from './credit-limit.service';

const API = '/api/v1/ar/credit-limits';

describe('FI-CREDIT credit limit service', () => {
  let api: CreditLimitService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CreditLimitService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('FI-CREDIT-019 sends only supported filters and keeps raw decimal text of every row', () => {
    let rows: string[] = [];
    api.list({ page: 2, pageSize: 50, customerId: 7, status: 'Active', from: '2026-01-01', to: null, sort: 'amount' })
      .subscribe(result => rows = result.items.map(item => item.amountText));
    const request = http.expectOne(r => r.url === API);
    expect(request.request.params.keys().sort()).toEqual(['customerId', 'from', 'page', 'pageSize', 'sort', 'status']);
    expect(request.request.params.get('customerId')).toBe('7');
    expect(request.request.params.has('keyword')).toBe(false);
    request.flush('{"items":[{"id":1,"customerId":7,"baseCurrency":"TWD","effectiveFrom":"2026-01-01","effectiveTo":"2026-12-31",'
      + '"amount":999999999999999.9999,"status":"Active","version":1},{"id":2,"customerId":7,"baseCurrency":"TWD",'
      + '"effectiveFrom":"2025-01-01","effectiveTo":"2025-12-31","amount":0,"status":"Deactivated","version":3}],'
      + '"totalCount":2,"page":2,"pageSize":50}');
    expect(rows).toEqual(['999999999999999.9999', '0']);
  });

  it('FI-CREDIT-004 sends amount as text and version on update; deactivate sends version only', () => {
    const draft = { customerId: 7, effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', amount: ' 123456789012.3456 ' };
    let saved = '';
    api.create(draft).subscribe(result => saved = result.amountText);
    const post = http.expectOne(API);
    expect(post.request.body).toEqual({ ...draft, amount: '123456789012.3456' });
    post.flush('{"id":5,"customerId":7,"baseCurrency":"TWD","amount":123456789012.3456,"status":"Active","version":1}');
    expect(saved).toBe('123456789012.3456');

    api.update(5, 1, draft).subscribe();
    const put = http.expectOne(`${API}/5`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ version: 1, ...draft, amount: '123456789012.3456' });
    put.flush('{"id":5,"amount":1,"version":2}');

    api.deactivate(5, 2).subscribe();
    const deactivate = http.expectOne(`${API}/5/deactivate`);
    expect(deactivate.request.body).toEqual({ version: 2 });
    deactivate.flush('{"id":5,"amount":1,"status":"Deactivated","version":3}');
  });

  it('FI-CREDIT-017 restores JSON problem bodies so named errors, fields and trace IDs survive text mode', () => {
    let key = ''; let fields = {};
    api.create({ customerId: 7, effectiveFrom: '2026-01-01', effectiveTo: '2025-01-01', amount: '-1' }).subscribe({
      error: (error: unknown) => { key = api.errorKey(error); fields = api.fieldErrors(error); } });
    http.expectOne(API).flush('{"errorCode":"COMMON.REQUEST.VALIDATION_FAILED","errors":{"effectiveTo":["x"],"Amount":["y"],"other":["z"]}}',
      { status: 400, statusText: 'Bad Request' });
    expect(key).toBe('fi.creditLimit.validationFailed');
    expect(fields).toEqual({ effectiveTo: true, amount: true });

    api.update(5, 1, { customerId: 7, effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', amount: '1' }).subscribe({
      error: (error: unknown) => key = api.errorKey(error) });
    http.expectOne(`${API}/5`).flush('{"errorCode":"FI.CREDIT.LIMIT_OVERLAP","traceId":"t"}', { status: 409, statusText: 'Conflict' });
    expect(key).toBe('errors.FI.CREDIT.LIMIT_OVERLAP');

    api.get(5).subscribe({ error: (error: unknown) => key = api.errorKey(error) });
    http.expectOne(`${API}/5`).flush('not json', { status: 502, statusText: 'Bad Gateway' });
    expect(key).toBe('errors.http.unknown');
  });

  it('FI-CREDIT-004 validates amount text and formats without floating point', () => {
    for (const ok of ['0', '0.5', '50000.25', '999999999999999.9999']) expect(CREDIT_AMOUNT_PATTERN.test(ok)).toBe(true);
    for (const bad of ['', '-1', '01', '1.23456', '1000000000000000', '1,000', '1e3', ' 1']) expect(CREDIT_AMOUNT_PATTERN.test(bad)).toBe(false);
    expect(formatCreditAmount('999999999999999.9999', 'en')).toBe('999,999,999,999,999.9999');
    expect(formatCreditAmount('50000.2500', 'en')).toBe('50,000.25');
    expect(formatCreditAmount('0', 'en')).toBe('0');
  });
});
