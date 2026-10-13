import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OtherReceiptService, validReceiptLine } from './other-receipt.service';

describe('INV-REC-006 other receipt API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('validates six decimal places and positive quantities', () => {
    expect(validReceiptLine({ itemId: 1, warehouseId: 2, locationId: null, quantity: '0.000001', unitCost: '0.000001' })).toBe(true);
    expect(validReceiptLine({ itemId: 1, warehouseId: 2, locationId: null, quantity: '0', unitCost: '0' })).toBe(false);
    expect(validReceiptLine({ itemId: 1, warehouseId: 2, locationId: null, quantity: '1.0000001', unitCost: '0' })).toBe(false);
  });

  it('sends filters, complete draft lines, versioned submit and delete', () => {
    const api = TestBed.inject(OtherReceiptService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 20, keyword: ' R1 ', status: 'Draft', from: '2026-10-01', to: '2026-10-31' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/inv/other-receipts');
    expect(list.request.params.get('keyword')).toBe('R1');
    expect(list.request.params.get('status')).toBe('Draft');
    expect(list.request.params.get('page')).toBe('2');
    list.flush({ items: [], totalCount: 0 });
    api.create({ businessDate: '2026-10-13', remark: ' test ', lines: [
      { itemId: 1, warehouseId: 2, locationId: null, quantity: '1.000001', unitCost: '2.500001' },
    ] }).subscribe();
    const create = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/inv/other-receipts');
    expect(create.request.body).toMatchObject({ remark: 'test', lines: [{ lineNo: 1, quantity: '1.000001', unitCost: '2.500001' }] });
    create.flush({});
    api.submit(4, 7).subscribe();
    const submit = http.expectOne('/api/v1/inv/other-receipts/4/submit');
    expect(submit.request.body).toEqual({ version: 7 });
    submit.flush({});
    api.remove(4, 8).subscribe();
    const remove = http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/v1/inv/other-receipts/4');
    expect(remove.request.params.get('version')).toBe('8');
    remove.flush(null, { status: 204, statusText: 'No Content' });
  });
});
