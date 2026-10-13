import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { newRequestKey, OpeningDraft, openingLineErrorKey, OpeningStockService, validOpeningLine } from './opening-stock.service';

const draft = (): OpeningDraft => ({ requestKey: 'key-1', openingDate: '2026-10-01', remark: '  ',
  lines: [{ itemId: 1, warehouseId: 2, locationId: null, quantity: ' 5.5 ', unitCost: '10' }] });

describe('INV-OPN-007 opening stock service', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends only the selected filters with server paging', () => {
    const api = TestBed.inject(OpeningStockService); const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 20, from: '2026-10-01', to: '', itemId: 3, keyword: ' 期初 ' }).subscribe();
    const req = http.expectOne(r => r.url === '/api/v1/inv/opening-stocks');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('from')).toBe('2026-10-01');
    expect(req.request.params.has('to')).toBe(false);
    expect(req.request.params.get('itemId')).toBe('3');
    expect(req.request.params.has('warehouseId')).toBe(false);
    expect(req.request.params.get('keyword')).toBe('期初');
    req.flush({ items: [], totalCount: 0 });
  });

  it('previews and submits the same numbered body with the batch request key', () => {
    const api = TestBed.inject(OpeningStockService); const http = TestBed.inject(HttpTestingController);
    api.preview(draft()).subscribe();
    const preview = http.expectOne('/api/v1/inv/opening-stocks/preview');
    expect(preview.request.body).toEqual({ requestKey: 'key-1', openingDate: '2026-10-01', remark: null,
      lines: [{ lineNo: 1, itemId: 1, warehouseId: 2, locationId: null, quantity: '5.5', unitCost: '10' }] });
    preview.flush({ lines: [], errorCount: 0, totalQuantity: 0, totalAmount: 0 });
    let replayed: boolean | undefined;
    api.submit(draft()).subscribe(x => replayed = x.replayed);
    const submit = http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/inv/opening-stocks');
    expect(submit.request.body.requestKey).toBe('key-1');
    submit.flush({ id: 9, replayed: false, lines: [] }, { status: 200, statusText: 'OK' });
    expect(replayed).toBe(true);
    api.submit(draft()).subscribe(x => replayed = x.replayed);
    http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/inv/opening-stocks')
      .flush({ id: 10, replayed: false, lines: [] }, { status: 201, statusText: 'Created' });
    expect(replayed).toBe(false);
  });

  it('maps line and submit error codes to trilingual keys', () => {
    const api = TestBed.inject(OpeningStockService);
    expect(openingLineErrorKey('MD.ITEM.INACTIVE')).toBe('errors.MD.ITEM.INACTIVE');
    expect(openingLineErrorKey('INV.OPENING_STOCK.AFTER_MOVEMENT')).toBe('errors.INV.OPENING_STOCK.AFTER_MOVEMENT');
    expect(openingLineErrorKey('SOMETHING.ELSE')).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    const conflict = new HttpErrorResponse({ status: 409, error: { errorCode: 'INV.OPENING_STOCK.REQUEST_CONFLICT' } });
    expect(api.errorKey(conflict)).toBe('errors.INV.OPENING_STOCK.REQUEST_CONFLICT');
    const invalid = new HttpErrorResponse({ status: 422, error: { errorCode: 'INV.OPENING_STOCK.HAS_ERRORS' } });
    expect(api.errorKey(invalid)).toBe('errors.INV.OPENING_STOCK.HAS_ERRORS');
  });

  it('validates line drafts and creates distinct request keys', () => {
    const line = draft().lines[0];
    expect(validOpeningLine(line)).toBe(true);
    expect(validOpeningLine({ ...line, quantity: '0' })).toBe(false);
    expect(validOpeningLine({ ...line, quantity: '1.1234567' })).toBe(false);
    expect(validOpeningLine({ ...line, unitCost: '-1' })).toBe(false);
    expect(validOpeningLine({ ...line, itemId: null })).toBe(false);
    const key = newRequestKey();
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
    expect(newRequestKey()).not.toBe(key);
  });
});
