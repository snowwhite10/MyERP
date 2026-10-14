import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { InventorySelectionOptionsService } from './selection-options.service';

describe('INV-SEL-004 selection option client', () => {
  const select = vi.fn().mockReturnValue(of(null));
  beforeEach(() => {
    select.mockClear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: ErpDialogService, useValue: { select } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses INV permission scoped endpoints, trimmed keyword and server paging', () => {
    const service = TestBed.inject(InventorySelectionOptionsService);
    const http = TestBed.inject(HttpTestingController);
    const query = { page: 2, pageSize: 10, keyword: '  bolt  ' };
    service.items(query).subscribe();
    const item = http.expectOne(r => r.url === '/api/v1/inv/selection-options/items');
    expect(item.request.params.get('keyword')).toBe('bolt');
    expect(item.request.params.get('page')).toBe('2');
    expect(item.request.params.get('pageSize')).toBe('10');
    item.flush({ items: [], totalCount: 0 });
    service.warehouses(query).subscribe();
    http.expectOne(r => r.url === '/api/v1/inv/selection-options/warehouses').flush({ items: [], totalCount: 0 });
    service.locations(42, query).subscribe();
    http.expectOne(r => r.url === '/api/v1/inv/selection-options/warehouses/42/locations').flush({ items: [], totalCount: 0 });
  });

  it('opens the shared dialog for each mode and keeps the warehouse constraint', () => {
    const service = TestBed.inject(InventorySelectionOptionsService);
    expect(() => service.choose('location')).toThrow('Warehouse required');
    service.choose('item').subscribe();
    service.choose('warehouse').subscribe();
    service.choose('location', 42).subscribe();
    expect(select).toHaveBeenCalledTimes(3);
    const choices = select.mock.calls.map(([options]) => options);
    expect(choices.map(x => x.titleKey)).toEqual(['inv.selection.item', 'inv.selection.warehouse', 'inv.selection.location']);
    expect(choices[0].columns.map((x: { key: string }) => x.key)).toEqual(['code', 'name']);
    choices[2].load({ page: 1, pageSize: 10, keyword: '' }).subscribe();
    TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/inv/selection-options/warehouses/42/locations')
      .flush({ items: [], totalCount: 0 });
  });
});
