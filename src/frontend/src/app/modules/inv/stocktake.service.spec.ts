import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { StocktakeService, decimalDifference, validCount } from './stocktake.service';

describe('INV-STK-011 stocktake counting contract', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('keeps six-place quantities exact and allows zero counts', () => {
    expect(validCount('0')).toBe(true);
    expect(validCount('1.000001')).toBe(true);
    expect(validCount('1.0000001')).toBe(false);
    expect(validCount('-1')).toBe(false);
    expect(decimalDifference('1.000001', '1')).toBe('0.000001');
    expect(decimalDifference('0', '1.000001')).toBe('-1.000001');
  });

  it('sends scope, versioned counts, refresh and submit without losing zero', () => {
    const api = TestBed.inject(StocktakeService);
    const http = TestBed.inject(HttpTestingController);
    api.create({ businessDate: '2026-10-14', remark: ' count ', scope: { warehouseIds: [2], categoryIds: [], itemIds: [3] } }).subscribe();
    const create = http.expectOne('/api/v1/inv/stocktakes');
    expect(create.request.body).toEqual({ businessDate: '2026-10-14', remark: 'count', scope: { warehouseIds: [2], categoryIds: [], itemIds: [3] } });
    create.flush({});
    api.update(4, 6, { businessDate: '2026-10-14', remark: '', lines: [
      { lineId: 9, countedQuantity: '0', tracking: [{ lotNo: ' LOT ', serialNo: null, countedQuantity: '0' }] },
      { itemId: 3, warehouseId: 2, locationId: null, countedQuantity: '1.000001', tracking: [] },
    ] }).subscribe();
    const update = http.expectOne('/api/v1/inv/stocktakes/4');
    expect(update.request.body).toMatchObject({ version: 6, remark: null, lines: [
      { lineId: 9, countedQuantity: '0', tracking: [{ lotNo: 'LOT', serialNo: null, countedQuantity: '0' }] },
      { itemId: 3, warehouseId: 2, locationId: null, countedQuantity: '1.000001' },
    ] });
    update.flush({});
    api.refresh(4, 7).subscribe();
    const refresh = http.expectOne('/api/v1/inv/stocktakes/4/refresh-snapshot');
    expect(refresh.request.body).toEqual({ version: 7 });
    refresh.flush({});
    api.submit(4, 8).subscribe();
    const submit = http.expectOne('/api/v1/inv/stocktakes/4/submit');
    expect(submit.request.body).toEqual({ version: 8 });
    submit.flush({});
  });

  it('maps stale snapshot and missing counts', () => {
    const api = TestBed.inject(StocktakeService);
    const error = (errorCode: string) => new HttpErrorResponse({ status: 422, error: { errorCode } });
    expect(api.errorKey(error('INV.STOCKTAKE.SNAPSHOT_STALE'))).toBe('errors.INV.STOCKTAKE.SNAPSHOT_STALE');
    expect(api.errorKey(error('INV.STOCKTAKE.COUNT_MISSING'))).toBe('errors.INV.STOCKTAKE.COUNT_MISSING');
  });
});
