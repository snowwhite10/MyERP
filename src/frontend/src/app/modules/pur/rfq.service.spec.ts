import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RfqDraft, RfqService, rfqFieldErrors } from './rfq.service';

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
});
