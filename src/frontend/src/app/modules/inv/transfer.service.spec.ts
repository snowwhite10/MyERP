import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { sameBalanceKey, TransferService, validTransferLine } from './transfer.service';

describe('INV-TRF-009 transfer API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const line = { itemId: 1, sourceWarehouseId: 2, sourceLocationId: null, destinationWarehouseId: 3, destinationLocationId: null, quantity: '0.000001' };

  it('validates six decimal places, positive quantity and distinct balance keys', () => {
    expect(validTransferLine(line)).toBe(true);
    expect(validTransferLine({ ...line, quantity: '0' })).toBe(false);
    expect(validTransferLine({ ...line, quantity: '1.0000001' })).toBe(false);
    expect(validTransferLine({ ...line, destinationWarehouseId: null })).toBe(false);
    expect(validTransferLine({ ...line, destinationWarehouseId: 2 })).toBe(false);
    expect(sameBalanceKey({ ...line, destinationWarehouseId: 2 })).toBe(true);
    expect(validTransferLine({ ...line, destinationWarehouseId: 2, destinationLocationId: 9 })).toBe(true);
  });

  it('sends filters, both balance keys, tracking, versioned update, submit and delete', () => {
    const api = TestBed.inject(TransferService);
    const http = TestBed.inject(HttpTestingController);
    api.list({ page: 2, pageSize: 20, keyword: ' T1 ', status: 'Rejected', from: '2026-10-01', to: '2026-10-31' }).subscribe();
    const list = http.expectOne((r) => r.url === '/api/v1/inv/transfers');
    expect(list.request.params.get('keyword')).toBe('T1');
    expect(list.request.params.get('status')).toBe('Rejected');
    expect(list.request.params.get('to')).toBe('2026-10-31');
    list.flush({ items: [], totalCount: 0 });
    api.create({ businessDate: '2026-10-14', remark: ' move ', lines: [
      { ...line, sourceLocationId: 5, quantity: '2.000001', tracking: [{ lotNo: ' L1 ', quantity: '2.000001' }] },
      { ...line, quantity: '1' },
    ] }).subscribe();
    const create = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/inv/transfers');
    expect(create.request.body).toMatchObject({ remark: 'move', lines: [
      { lineNo: 1, itemId: 1, sourceWarehouseId: 2, sourceLocationId: 5, destinationWarehouseId: 3, destinationLocationId: null,
        quantity: '2.000001', tracking: [{ lotNo: 'L1', serialNo: null, quantity: '2.000001' }] },
      { lineNo: 2, quantity: '1' }] });
    expect(create.request.body.lines[1].tracking).toBeUndefined();
    create.flush({});
    api.update(4, 6, { businessDate: '2026-10-14', remark: '', lines: [line] }).subscribe();
    const update = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/inv/transfers/4');
    expect(update.request.body).toMatchObject({ version: 6, remark: null });
    update.flush({});
    api.submit(4, 7).subscribe();
    const submit = http.expectOne('/api/v1/inv/transfers/4/submit');
    expect(submit.request.body).toEqual({ version: 7 });
    submit.flush({});
    api.remove(4, 8).subscribe();
    const remove = http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/v1/inv/transfers/4');
    expect(remove.request.params.get('version')).toBe('8');
    remove.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('maps transfer, stock and tracking error codes', () => {
    const api = TestBed.inject(TransferService);
    const problem = (errorCode: string) => new HttpErrorResponse({ status: 422, error: { errorCode } });
    expect(api.errorKey(problem('INV.TRANSFER.NOT_DRAFT'))).toBe('errors.INV.TRANSFER.NOT_DRAFT');
    expect(api.errorKey(problem('INV.STOCK.INSUFFICIENT'))).toBe('errors.INV.STOCK.INSUFFICIENT');
    expect(api.errorKey(problem('INV.SERIAL.NOT_AVAILABLE'))).toBe('errors.INV.SERIAL.NOT_AVAILABLE');
  });
});
