import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { StockQueryService, stockCsvCell } from './stock-query.service';

describe('INV-QRY-004 stock query', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('keeps the selected filters and server paging for each permission-specific endpoint', () => {
    const api = TestBed.inject(StockQueryService);
    const http = TestBed.inject(HttpTestingController);
    api.balances({ page: 2, pageSize: 20, itemId: 3, warehouseId: 4, locationId: 5 }).subscribe();
    const balance = http.expectOne(r => r.url === '/api/v1/inv/balances');
    expect(balance.request.params.get('page')).toBe('2');
    expect(balance.request.params.get('locationId')).toBe('5');
    expect(balance.request.params.has('from')).toBe(false);
    balance.flush({ items: [], totalCount: 0 });
    api.movements({ page: 1, pageSize: 200, itemId: 3, from: '2026-10-01', to: '2026-10-13', direction: 'Out' }).subscribe();
    const movement = http.expectOne(r => r.url === '/api/v1/inv/movements');
    expect(movement.request.params.get('from')).toBe('2026-10-01');
    expect(movement.request.params.get('direction')).toBe('Out');
    movement.flush({ items: [], totalCount: 0 });
  });

  it('quotes CSV cells and neutralizes spreadsheet formula prefixes', () => {
    expect(stockCsvCell(' =1+1')).toBe('"\' =1+1"');
    expect(stockCsvCell('@SUM(A1)')).toBe('"\'@SUM(A1)"');
    expect(stockCsvCell('A,"B"')).toBe('"A,""B"""');
    expect(stockCsvCell('-5')).toBe('"\'-5"');
    expect(stockCsvCell(-5.25)).toBe('"-5.25"');
    expect(stockCsvCell(null)).toBe('""');
  });
});
