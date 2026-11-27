import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OtherPaymentService } from './other-payment.service';

describe('FI-OP-UI list read contract', () => {
  let service: OtherPaymentService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(OtherPaymentService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('FI-OP-UI-001 sends company-scoped page filters and keeps a 19,4 amount as exact text', () => {
    let amount = '';
    service.list({ page: 2, pageSize: 20, keyword: ' OP ', status: 'Draft', from: '2026-11-01',
      to: '2026-11-30', sort: '-businessDate' }).subscribe(result => amount = result.items[0].amount);
    const req = http.expectOne(r => r.url === '/api/v1/ap/other-payments');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('keyword')).toBe('OP');
    expect(req.request.params.get('status')).toBe('Draft');
    expect(req.request.params.get('from')).toBe('2026-11-01');
    expect(req.request.params.get('to')).toBe('2026-11-30');
    expect(req.request.params.get('sort')).toBe('-businessDate');
    req.flush('{"items":[{"id":1,"amount":999999999999999.9999}],"totalCount":1,"page":2,"pageSize":20}');
    expect(amount).toBe('999999999999999.9999');
  });

  it('FI-OP-UI-002 reads category mappings independently of payment list permission', () => {
    let configured: boolean | undefined;
    service.maps().subscribe(result => configured = result.items[0].configured);
    http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [{ expenseCategoryId: 7, configured: false,
      expenseAccount: null }] });
    expect(configured).toBe(false);
  });

  it('FI-OP-UI-007 creates, replaces, submits, withdraws and deletes with held versions', () => {
    service.create({ payeeDescription: 'Power', expenseCategoryId: 3, bankAccountId: 9,
      bankReference: 'WD-1', businessDate: '2026-11-27', amount: '12.3400', remark: null }).subscribe();
    const create = http.expectOne('/api/v1/ap/other-payments');
    expect(create.request.method).toBe('POST');
    expect((create.request.body as { amount: string }).amount).toBe('12.3400');
    create.flush({ id: 7 });
    service.detail(7).subscribe(result => expect(result.amount).toBe('999999999999999.9999'));
    http.expectOne('/api/v1/ap/other-payments/7').flush('{"id":7,"amount":999999999999999.9999}');
    service.update(7, 2, { payeeDescription: 'Power', expenseCategoryId: 3, bankAccountId: 9,
      bankReference: 'WD-1', businessDate: '2026-11-27', amount: '12.3400', remark: null }).subscribe();
    const update = http.expectOne('/api/v1/ap/other-payments/7');
    expect(update.request.method).toBe('PUT');
    expect((update.request.body as { version: number }).version).toBe(2);
    update.flush({ id: 7 });
    service.submit(7, 3).subscribe();
    const submit = http.expectOne('/api/v1/ap/other-payments/7/submit');
    expect(submit.request.body).toEqual({ version: 3 }); submit.flush({ status: 'Pending' });
    service.withdraw(7, 4).subscribe();
    http.expectOne('/api/v1/ap/other-payments/7/withdraw').flush({ status: 'Draft' });
    service.remove(7, 5).subscribe();
    const remove = http.expectOne(r => r.url === '/api/v1/ap/other-payments/7' && r.method === 'DELETE');
    expect(remove.request.params.get('version')).toBe('5'); remove.flush(null);
  });

  it('FI-OP-UI-013 saves one GL category mapping with version and exact changed flag', () => {
    service.saveMap(3, 0, '6101').subscribe(result => expect(result.changed).toBe(false));
    const req = http.expectOne('/api/v1/gl/other-payment-account-map/3');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ version: 0, expenseAccountCode: '6101' });
    req.flush({ changed: false, row: { expenseCategoryId: 3, version: 0 } });
  });
});
