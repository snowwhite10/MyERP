import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { quotationDraftJson, SalesQuotationService } from './quotation.service';
import { QuotationDraftForm, validateQuotationDraft } from './quotation-draft-form';

describe('SD-QUOTE-046 read client contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends only the supported server filters and trims keyword', () => {
    const service = TestBed.inject(SalesQuotationService);
    const http = TestBed.inject(HttpTestingController);
    service.list({ page: 2, pageSize: 20, keyword: '  Q-1  ', from: '2028-02-01',
      to: '2028-02-29', status: 'Approved', customerId: 12, sort: '-businessDate' }).subscribe();
    const request = http.expectOne(r => r.url === '/api/v1/sal/quotations');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys().sort()).toEqual(
      ['page', 'pageSize', 'keyword', 'from', 'to', 'status', 'customerId', 'sort'].sort());
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('keyword')).toBe('Q-1');
    expect(request.request.params.get('from')).toBe('2028-02-01');
    expect(request.request.params.get('to')).toBe('2028-02-29');
    expect(request.request.params.get('status')).toBe('Approved');
    expect(request.request.params.get('customerId')).toBe('12');
    expect(request.request.params.get('sort')).toBe('-businessDate');
    request.flush({ items: [], totalCount: 0, page: 2, pageSize: 20 });
    service.list({ page: 1, pageSize: 10, keyword: '  ' }).subscribe();
    const empty = http.expectOne(r => r.url === '/api/v1/sal/quotations');
    expect(empty.request.params.keys().sort()).toEqual(['page', 'pageSize'].sort());
    expect(empty.request.params.has('keyword')).toBe(false);
    empty.flush({ items: [], totalCount: 0, page: 1, pageSize: 10 });
  });

  it('reads saved detail by id without emitting a mutation', () => {
    const service = TestBed.inject(SalesQuotationService);
    const http = TestBed.inject(HttpTestingController);
    service.get(7).subscribe();
    const request = http.expectOne('/api/v1/sal/quotations/7');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 7, lines: [], approvalHistory: [] });
  });
});

describe('SD-QUOTE-UI-009 exact create transport', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends exact six-place decimal JSON numbers and receives the saved document', () => {
    const draft: QuotationDraftForm = { customerId: 7, businessDate: '2028-02-29',
      expiryDate: '2028-03-31', remark: 'test', lines: [{ lineNo: 1, itemId: 9,
        quantity: '9999999999999.999999', unitPrice: '0.000001', discountRate: '12.3456',
        currencyCode: 'USD', taxRateId: 4 }] };
    let id = 0;
    TestBed.inject(SalesQuotationService).create(draft).subscribe(saved => id = saved.id);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/quotations');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Content-Type')).toBe('application/json');
    expect(request.request.body).toContain('"quantity":9999999999999.999999');
    expect(request.request.body).toContain('"unitPrice":0.000001');
    expect(request.request.body).toContain('"discountRate":12.3456');
    expect(request.request.body).not.toContain('"quantity":"');
    request.flush({ id: 42, documentNo: 'SQ-0042', status: 'Draft', lines: [] });
    expect(id).toBe(42);
  });
});

describe('SD-QUOTE-EDIT-001 exact versioned transport', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends the saved version and exact decimal draft in one PUT', () => {
    const draft: QuotationDraftForm = { customerId: 7, businessDate: '2028-02-29',
      expiryDate: '2028-03-31', remark: 'revision', lines: [{ lineNo: 1, itemId: 9,
        quantity: '1.000001', unitPrice: '0.000001', discountRate: '12.3456',
        currencyCode: 'USD', taxRateId: 4 }] };
    let savedVersion = 0;
    TestBed.inject(SalesQuotationService).replace(41, 3, draft).subscribe(saved => savedVersion = saved.version);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/quotations/41');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toContain('"version":3');
    expect(request.request.body).toContain('"quantity":1.000001');
    expect(request.request.body).toContain('"unitPrice":0.000001');
    request.flush({ id: 41, version: 4 });
    expect(savedVersion).toBe(4);
  });

  it('preserves saved six-place decimal tokens when opening the editor', () => {
    let quantity = '';
    TestBed.inject(SalesQuotationService).getEditable(41).subscribe(saved => quantity = saved.lines[0].quantity);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/quotations/41');
    expect(request.request.responseType).toBe('text');
    request.flush('{"id":41,"version":1,"lines":[{"quantity":9999999999999.999999,"unitPrice":0.000001,"discountRate":12.3456}]}');
    expect(quantity).toBe('9999999999999.999999');
  });
});

describe('SD-QPRICE-UI-002 pricing hints in the save body (WU-SD-005b2a)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  const line = (extra: Record<string, unknown>) => ({ lineNo: 1, itemId: 9, quantity: '1', unitPrice: '100.5',
    discountRate: '0', currencyCode: 'USD', taxRateId: 4, ...extra });

  it('sends the quote pair only together and a trimmed reason only when entered', () => {
    const draft = (extra: Record<string, unknown>): QuotationDraftForm => ({ customerId: 7, businessDate: '2028-02-29',
      expiryDate: '2028-03-31', remark: '', lines: [line(extra)] });
    expect(quotationDraftJson(draft({ pricingRuleId: 5, pricingRuleVersion: 3, overrideReason: '  ' })))
      .toContain('"taxRateId":4,"pricingRuleId":5,"pricingRuleVersion":3}');
    expect(quotationDraftJson(draft({ pricingRuleId: 5, pricingRuleVersion: null }))).not.toContain('pricingRule');
    expect(quotationDraftJson(draft({ overrideReason: ' Volume "deal" ' })))
      .toContain('"taxRateId":4,"overrideReason":"Volume \\"deal\\""}');
    expect(quotationDraftJson(draft({}))).toContain('"taxRateId":4}');
  });

  it('reads saved rule price and ceiling as exact text', () => {
    let saved: { ruleUnitPrice?: string | null; ruleDiscountCeiling?: string | null; pricingMode?: string } | undefined;
    TestBed.inject(SalesQuotationService).getEditable(41).subscribe(value => saved = value.lines[0]);
    TestBed.inject(HttpTestingController).expectOne('/api/v1/sal/quotations/41').flush(
      '{"id":41,"version":1,"lines":[{"quantity":1,"unitPrice":1234567890123.123456,"discountRate":0,' +
      '"pricingMode":"Rule","ruleUnitPrice":1234567890123.123456,"ruleDiscountCeiling":12.5000}]}');
    expect(saved).toMatchObject({ pricingMode: 'Rule', ruleUnitPrice: '1234567890123.123456', ruleDiscountCeiling: '12.5000' });
  });
});

describe('SD-QPRICE-UI-003 override reason validation (WU-SD-005b2a)', () => {
  it('requires a reason only on lines known to need an override and caps it at 200 characters', () => {
    const draft: QuotationDraftForm = { customerId: 7, businessDate: '2028-02-29', expiryDate: '2028-03-31', remark: '',
      lines: [{ lineNo: 1, itemId: 9, quantity: '1', unitPrice: '1', discountRate: '0', currencyCode: 'USD', taxRateId: 4,
        overrideReason: ' ' },
      { lineNo: 2, itemId: 9, quantity: '1', unitPrice: '1', discountRate: '0', currencyCode: 'USD', taxRateId: 4,
        overrideReason: 'x'.repeat(201) }] };
    expect(validateQuotationDraft(draft)).toEqual({ 'lines[1].overrideReason': 'tooLong' });
    expect(validateQuotationDraft(draft, [true, false])).toEqual({ 'lines[0].overrideReason': 'required',
      'lines[1].overrideReason': 'tooLong' });
  });
});
