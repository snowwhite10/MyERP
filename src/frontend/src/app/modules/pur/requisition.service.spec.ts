import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RequisitionService, validRequisitionDraft } from './requisition.service';

describe('PUR-UI-001 requisition client contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const draft = { businessDate: '2028-02-29', remark: '  urgent  ', lines: [
    { itemId: 8, requiredDate: '2028-03-01', quantity: '1.000001', remark: '  first  ' },
  ] };

  it('rejects empty, invalid date, duplicate and out-of-range lines before saving', () => {
    expect(validRequisitionDraft(draft)).toBe(true);
    expect(validRequisitionDraft({ ...draft, lines: [] })).toBe(false);
    expect(validRequisitionDraft({ ...draft, lines: [{ ...draft.lines[0], requiredDate: '2028-02-28' }] })).toBe(false);
    expect(validRequisitionDraft({ ...draft, lines: [{ ...draft.lines[0], quantity: '0.0000001' }] })).toBe(false);
    expect(validRequisitionDraft({ ...draft, lines: [{ ...draft.lines[0], quantity: '0' }] })).toBe(false);
    expect(validRequisitionDraft({ ...draft, lines: [{ ...draft.lines[0], quantity: '10000000000000' }] })).toBe(false);
    expect(validRequisitionDraft({ ...draft, lines: Array(1001).fill(draft.lines[0]) })).toBe(false);
    expect(validRequisitionDraft({ ...draft, lines: [draft.lines[0], draft.lines[0]] })).toBe(true);
    expect(validRequisitionDraft({ ...draft, businessDate: '2028-02-30' })).toBe(false);
    expect(validRequisitionDraft({ ...draft, businessDate: '0000-02-29' })).toBe(false);
    expect(validRequisitionDraft({ ...draft, remark: 'x'.repeat(501) })).toBe(false);
    expect(validRequisitionDraft({ ...draft, lines: Array(1000).fill(draft.lines[0]) })).toBe(true);
  });

  it('uses server filters and versioned endpoints without sending ownership fields', () => {
    const api = TestBed.inject(RequisitionService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 20, keyword: '  REQ-01  ', from: '2028-02-01', to: '2028-02-29',
      status: 'Draft', requesterId: 9, sort: '-businessDate' }).subscribe();
    const list = http.expectOne(r => r.url === '/api/v1/pur/requisitions');
    expect(list.request.params.get('keyword')).toBe('REQ-01');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('requesterId')).toBe('9');
    expect(list.request.params.get('sort')).toBe('-businessDate');
    list.flush({ items: [], totalCount: 0 });
    api.get(7).subscribe();
    http.expectOne('/api/v1/pur/requisitions/7').flush({ approvalHistory: [] });
    api.create(draft).subscribe();
    const create = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/pur/requisitions');
    expect(create.request.body).toEqual({ businessDate: '2028-02-29', remark: 'urgent', lines: [
      { lineNo: 1, itemId: 8, requiredDate: '2028-03-01', quantity: '1.000001', remark: 'first' },
    ] });
    expect(create.request.body).not.toHaveProperty('requesterId');
    create.flush({});
    api.update(7, 3, draft).subscribe();
    const update = http.expectOne(r => r.method === 'PUT' && r.url === '/api/v1/pur/requisitions/7');
    expect(update.request.params.get('version')).toBe('3');
    expect(update.request.body.lines[0].lineNo).toBe(1);
    update.flush({});
    api.submit(7, 4).subscribe();
    const submit = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/pur/requisitions/7/submit');
    expect(submit.request.body).toEqual({ version: 4 });
    submit.flush({ requisition: {}, approval: {} });
    api.remove(7, 5).subscribe();
    const remove = http.expectOne(r => r.method === 'DELETE' && r.url === '/api/v1/pur/requisitions/7');
    expect(remove.request.params.get('version')).toBe('5');
    remove.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('pages authorized MD options and reads only saved item references for a document', () => {
    const api = TestBed.inject(RequisitionService);
    const http = TestBed.inject(HttpTestingController);
    api.itemOptions({ page: 2, pageSize: 10, keyword: '  bolt  ' }).subscribe();
    const options = http.expectOne(r => r.url === '/api/v1/pur/requisitions/item-options');
    expect(options.request.params.get('page')).toBe('2');
    expect(options.request.params.get('keyword')).toBe('bolt');
    options.flush({ items: [], totalCount: 0 });
    api.savedItemOptions(7).subscribe();
    http.expectOne('/api/v1/pur/requisitions/7/saved-item-options').flush([]);
    api.getForEdit(7).subscribe();
    http.expectOne('/api/v1/pur/requisitions/7/edit').flush({ lines: [] });
  });
});
