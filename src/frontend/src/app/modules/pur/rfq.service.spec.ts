import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QuotationDraft, RfqDraft, RfqService, quotationFieldErrors, rfqFieldErrors } from './rfq.service';

describe('PUR-RFQ-076/077 RFQ draft client contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const draft: RfqDraft = { businessDate: '2028-02-29', replyDeadline: '2028-03-01', remark: '  note  ',
    lines: [{ itemId: 8, requisitionLineId: null, requiredDate: '2028-03-01', quantity: '1.000001', remark: ' first ' }],
    supplierIds: [3] };

  it('identifies dates, quantity, source, line and supplier errors by field', () => {
    expect(rfqFieldErrors(draft)).toEqual([]);
    expect(rfqFieldErrors({ ...draft, replyDeadline: '2028-02-28' })).toContain('replyDeadline');
    expect(rfqFieldErrors({ ...draft, businessDate: '2028-02-30' })).toContain('businessDate');
    expect(rfqFieldErrors({ ...draft, lines: [] })).toContain('lines');
    expect(rfqFieldErrors({ ...draft, supplierIds: [] })).toContain('supplierIds');
    expect(rfqFieldErrors({ ...draft, supplierIds: [3, 3] })).toContain('supplierIds');
    expect(rfqFieldErrors({ ...draft, supplierIds: Array.from({ length: 51 }, (_, i) => i + 1) })).toContain('supplierIds');
    expect(rfqFieldErrors({ ...draft, lines: [{ ...draft.lines[0], requisitionLineId: 0,
      quantity: '0.0000001', requiredDate: '2028-02-28' }] })).toEqual([
      'lines.0.requisitionLineId', 'lines.0.requiredDate', 'lines.0.quantity']);
    expect(rfqFieldErrors({ ...draft, lines: [{ ...draft.lines[0], quantity: '10000000000000' }] }))
      .toContain('lines.0.quantity');
  });

  it('sends ordered decimal strings and only editable fields with current version', () => {
    const api = TestBed.inject(RfqService); const http = TestBed.inject(HttpTestingController);
    api.create(draft).subscribe();
    const create = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/pur/rfqs');
    expect(create.request.body).toEqual({ businessDate: '2028-02-29', replyDeadline: '2028-03-01',
      remark: 'note', supplierIds: [3], lines: [{ lineNo: 1, itemId: 8, requisitionLineId: null,
        requiredDate: '2028-03-01', quantity: '1.000001', remark: 'first' }] });
    expect(create.request.body).not.toHaveProperty('companyId');
    create.flush({});
    api.update(7, 4, draft).subscribe();
    const update = http.expectOne(r => r.method === 'PUT' && r.url === '/api/v1/pur/rfqs/7');
    expect(update.request.params.get('version')).toBe('4');
    expect(update.request.body).toEqual(create.request.body);
    update.flush({});
  });

  it('uses RFQ authorization paths for editor data and MD choices', () => {
    const api = TestBed.inject(RfqService); const http = TestBed.inject(HttpTestingController);
    api.getForEdit(7).subscribe();
    http.expectOne('/api/v1/pur/rfqs/7/edit').flush({});
    api.savedOptions(7).subscribe();
    http.expectOne('/api/v1/pur/rfqs/7/saved-options').flush({ items: [], suppliers: [] });
    api.itemOptions({ page: 2, pageSize: 10, keyword: '  part ' }).subscribe();
    const items = http.expectOne(r => r.url === '/api/v1/pur/rfqs/item-options');
    expect(items.request.params.get('keyword')).toBe('part');
    expect(items.request.params.get('page')).toBe('2');
    items.flush({ items: [], totalCount: 0 });
    api.supplierOptions({ page: 1, pageSize: 10, keyword: '  vendor ' }).subscribe();
    const suppliers = http.expectOne(r => r.url === '/api/v1/pur/rfqs/supplier-options');
    expect(suppliers.request.params.get('keyword')).toBe('vendor');
    suppliers.flush({ items: [], totalCount: 0 });
    api.sourceOptions({ page: 1, pageSize: 10, keyword: '  PR-1 ' }, 8).subscribe();
    const sources = http.expectOne(r => r.url === '/api/v1/pur/rfqs/source-options');
    expect(sources.request.params.get('keyword')).toBe('PR-1');
    expect(sources.request.params.get('itemId')).toBe('8');
    sources.flush({ items: [], totalCount: 0 });
  });

  it('sends the saved version to the distinct SEND and CLOSE endpoints without a body', () => {
    const api = TestBed.inject(RfqService); const http = TestBed.inject(HttpTestingController);
    api.send(17, 2).subscribe();
    const send = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/pur/rfqs/17/send');
    expect(send.request.params.get('version')).toBe('2');
    expect(send.request.body).toBeNull();
    send.flush({});
    api.close(17, 3).subscribe();
    const close = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/pur/rfqs/17/close');
    expect(close.request.params.get('version')).toBe('3');
    expect(close.request.body).toBeNull();
    close.flush({});
  });

  it('explains references that changed after a draft was saved', () => {
    const api = TestBed.inject(RfqService);
    const failure = (errorCode: string) => new HttpErrorResponse({ status: 422, error: { errorCode } });
    expect(api.errorKey(failure('MD.ITEM.INACTIVE'))).toBe('pur.rfq.itemUnavailable');
    expect(api.errorKey(failure('MD.SUPPLIER.NOT_APPROVED'))).toBe('pur.rfq.supplierUnavailable');
    expect(api.errorKey(failure('PUR.REQUISITION.LINE_NOT_FOUND'))).toBe('pur.rfq.sourceUnavailable');
  });
});

describe('PUR-QUOTE-CTX-001 quotation editor choices', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('reads one QUOTE-authorized context with saved lines, invitations, currencies and input rates', () => {
    const api = TestBed.inject(RfqService); const http = TestBed.inject(HttpTestingController);
    api.quotationContext(7, '2028-02-29').subscribe();
    const request = http.expectOne(r => r.method === 'GET' && r.url === '/api/v1/pur/rfqs/7/quotation-context');
    expect(request.request.params.get('quoteDate')).toBe('2028-02-29');
    request.flush({ rfq: {}, currencies: [], taxRates: [] });
  });
});

describe('PUR-COMPARE-UI-001 read-only comparison contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads the current company comparison without a write request or client-calculated amount', () => {
    const api = TestBed.inject(RfqService);
    const http = TestBed.inject(HttpTestingController);
    api.compare(17).subscribe(result => expect(result.lines[0].offers[0].baseGross).toBe(24.5));
    const request = http.expectOne('/api/v1/pur/rfqs/17/comparison');
    expect(request.request.method).toBe('GET');
    request.flush({ rfqId: 17, comparisonDate: '2026-10-23', baseCurrencyCode: 'TWD',
      lines: [{ rfqLineId: 170, lineNo: 1, quantity: 2,
        offers: [{ quotationId: 31, supplierId: 7, baseGross: 24.5 }] }] });
  });
});

describe('PUR-QUOTE-UI-005/006 quotation write contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  const draft: QuotationDraft = { supplierId: 3, quoteDate: '2028-02-29', validUntil: '2028-03-31', remark: ' note ',
    lines: [{ rfqLineId: 11, unitPrice: '0.123456', currencyCode: 'twd', taxRateId: 8 }] };

  it('validates every editable field without rounding prices', () => {
    expect(quotationFieldErrors(draft)).toEqual([]);
    expect(quotationFieldErrors({ ...draft, supplierId: null })).toContain('supplierId');
    expect(quotationFieldErrors({ ...draft, quoteDate: '2028-02-30' })).toContain('quoteDate');
    expect(quotationFieldErrors({ ...draft, validUntil: '2028-02-28' })).toContain('validUntil');
    expect(quotationFieldErrors({ ...draft, remark: 'x'.repeat(501) })).toContain('remark');
    expect(quotationFieldErrors({ ...draft, lines: [] })).toContain('lines');
    expect(quotationFieldErrors({ ...draft, lines: Array.from({ length: 1000 }, (_, i) =>
      ({ ...draft.lines[0], rfqLineId: i + 1 })) })).toEqual([]);
    expect(quotationFieldErrors({ ...draft, lines: Array.from({ length: 1001 }, (_, i) =>
      ({ ...draft.lines[0], rfqLineId: i + 1 })) })).toContain('lines');
    expect(quotationFieldErrors({ ...draft, lines: [draft.lines[0], draft.lines[0]] })).toContain('lines');
    expect(quotationFieldErrors({ ...draft, lines: [{ ...draft.lines[0], rfqLineId: 0 }] }))
      .toContain('lines.0.rfqLineId');
    expect(quotationFieldErrors({ ...draft, lines: [{ ...draft.lines[0], unitPrice: '0' }] })).toEqual([]);
    for (const [key, value] of [['unitPrice', '-1'], ['unitPrice', '1.1234567'],
      ['unitPrice', '10000000000000'], ['currencyCode', 'T1D'], ['taxRateId', 0]] as const) {
      expect(quotationFieldErrors({ ...draft, lines: [{ ...draft.lines[0], [key]: value }] }))
        .toContain(`lines.0.${key}`);
    }
  });

  it('uses QUOTE endpoints, decimal strings and saved version', () => {
    const api = TestBed.inject(RfqService); const http = TestBed.inject(HttpTestingController);
    api.createQuotation(17, draft).subscribe();
    const create = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/pur/rfqs/17/quotations');
    expect(create.request.body).toEqual({ supplierId: 3, quoteDate: '2028-02-29', validUntil: '2028-03-31',
      remark: 'note', lines: [{ rfqLineId: 11, unitPrice: '0.123456', currencyCode: 'TWD', taxRateId: 8 }] });
    create.flush({});
    api.reviseQuotation(17, 31, 4, draft).subscribe();
    const revise = http.expectOne(r => r.url === '/api/v1/pur/rfqs/17/quotations/31/revisions');
    expect(revise.request.params.get('version')).toBe('4');
    expect(revise.request.body).toEqual(create.request.body);
    revise.flush({});
    api.withdrawQuotation(17, 31, 5).subscribe();
    const withdraw = http.expectOne(r => r.url === '/api/v1/pur/rfqs/17/quotations/31/withdraw');
    expect(withdraw.request.params.get('version')).toBe('5');
    expect(withdraw.request.body).toBeNull();
    withdraw.flush({});
  });

  it('maps quotation lifecycle and invalid MD references to localized reasons', () => {
    const api = TestBed.inject(RfqService);
    const error = (code: string) => new HttpErrorResponse({ status: 422, error: { errorCode: code } });
    expect(api.errorKey(error('PUR.QUOTATION.EXPIRED'))).toBe('errors.PUR.QUOTATION.EXPIRED');
    expect(api.errorKey(error('PUR.RFQ.NOT_OPEN'))).toBe('errors.PUR.RFQ.NOT_OPEN');
    expect(api.errorKey(error('MD.CURRENCY.INACTIVE'))).toBe('pur.quotation.currencyUnavailable');
    expect(api.errorKey(error('MD.TAX_RATE.INACTIVE'))).toBe('pur.quotation.taxUnavailable');
  });
});

describe('PUR-QUOTE-UI-001 quotation read contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses quotation VIEW endpoints and preserves filters and paging', () => {
    const api = TestBed.inject(RfqService); const http = TestBed.inject(HttpTestingController);
    api.listQuotations(17, { page: 2, pageSize: 20, supplierId: 7, status: 'Withdrawn',
      validOn: '2028-02-29', sort: '-quoteDate' }).subscribe();
    const list = http.expectOne(r => r.url === '/api/v1/pur/rfqs/17/quotations');
    expect(list.request.params.toString()).toContain('page=2');
    expect(list.request.params.toString()).toContain('supplierId=7');
    expect(list.request.params.toString()).toContain('status=Withdrawn');
    expect(list.request.params.toString()).toContain('validOn=2028-02-29');
    expect(list.request.params.toString()).toContain('sort=-quoteDate');
    list.flush({ items: [], totalCount: 0 });
    api.getQuotation(17, 31).subscribe();
    http.expectOne('/api/v1/pur/rfqs/17/quotations/31').flush({ revisions: [] });
  });
});
