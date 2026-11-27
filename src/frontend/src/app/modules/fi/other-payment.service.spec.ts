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
});
