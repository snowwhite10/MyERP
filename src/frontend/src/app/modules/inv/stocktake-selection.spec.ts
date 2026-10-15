import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of, Subject } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { StocktakeCount } from './stocktake-count';

describe('INV-SEL-004–006 stocktake selection', () => {
  const select = vi.fn();
  const company = signal(1);
  const has = vi.fn((permission: string) => permission !== 'MD.ITEM_CATEGORY.VIEW');
  function open(id?: string) {
    select.mockReset(); company.set(1); has.mockClear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { select } }, { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has, hasAny: () => true } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }] });
    const fixture = TestBed.createComponent(StocktakeCount); TestBed.tick(); fixture.detectChanges();
    return { form: fixture.componentInstance, fixture, http: TestBed.inject(HttpTestingController) };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('creates scope from confirmed paged choices without loading MD masters; cancel and stale company choices do nothing', () => {
    const { form, http } = open();
    http.expectNone(r => r.url.startsWith('/api/v1/md/'));
    select.mockReturnValueOnce(of([{ id: 4, code: 'WH', name: 'Main' }]));
    form.chooseScopeWarehouses(); expect(form.warehouseIds).toEqual([4]);
    select.mockReturnValueOnce(of(null)); form.chooseScopeItems(); expect(form.itemIds).toEqual([]);
    const stale = new Subject<{ id: number; code: string; name: string }[] | null>();
    select.mockReturnValueOnce(stale.asObservable()); form.chooseScopeItems();
    company.set(2); TestBed.tick(); stale.next([{ id: 3, code: 'OLD', name: 'Old' }]);
    expect(form.itemIds).toEqual([]); expect(form.warehouseIds).toEqual([]);
    expect(select.mock.calls[0][0].multiple).toBe(true);
  });

  it('uses selected tracking mode, clears a found line location on warehouse change and ignores old location choice', () => {
    const { form, http } = open('7');
    const detail = { id: 7, businessDate: '2026-10-14', status: 'Draft', remark: null, version: 1,
      scope: { warehouseIds: [4, 5], categoryIds: [], itemIds: [] }, lines: [] };
    http.expectOne('/api/v1/inv/stocktakes/7').flush(detail);
    http.expectOne('/api/v1/inv/stocktakes/7/saved-references').flush({ items: [], warehouses: [], locations: [] });
    form.addLine(); const row = form.rows[0];
    select.mockReturnValueOnce(of([{ id: 9, code: 'LOT', name: 'Lot', trackingMode: 'Lot' }]));
    form.chooseItem(row); expect(row.trackingMode).toBe('Lot');
    select.mockReturnValueOnce(of([{ id: 4, code: 'WH', name: 'Main' }])); form.chooseWarehouse(row);
    select.mockReturnValueOnce(of([{ id: 10, code: 'A', name: 'Bin' }])); form.chooseLocation(row);
    expect(row.locationId).toBe(10);
    const stale = new Subject<{ id: number; code: string; name: string }[] | null>();
    select.mockReturnValueOnce(stale.asObservable()); form.chooseLocation(row);
    select.mockReturnValueOnce(of([{ id: 5, code: 'WH2', name: 'Second' }])); form.chooseWarehouse(row);
    stale.next([{ id: 11, code: 'OLD', name: 'Old bin' }]);
    expect([row.warehouseId, row.locationId]).toEqual([5, null]);
    expect(select.mock.calls.at(-2)?.[0].load).toBeTypeOf('function');
  });

  it('shows saved inactive references without making them new choices', () => {
    const { form, http, fixture } = open('7');
    http.expectOne('/api/v1/inv/stocktakes/7').flush({ id: 7, businessDate: '2026-10-14', status: 'Draft', remark: null,
      version: 1, scope: { warehouseIds: [4], categoryIds: [], itemIds: [] }, lines: [{ id: 1, itemId: 9,
        warehouseId: 4, locationId: 10, bookQuantity: 1, countedQuantity: null, trackingMode: 'None',
        isAdded: false, recountRequired: false, tracking: [] }] });
    http.expectOne('/api/v1/inv/stocktakes/7/saved-references').flush({
      items: [{ id: 9, code: 'OLD', name: 'Inactive item', isActive: false }],
      warehouses: [{ id: 4, code: 'WH', name: 'Inactive warehouse', isActive: false }],
      locations: [{ id: 10, code: 'A', name: 'Inactive bin', isActive: false }],
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Inactive item');
    expect(form.itemName(9)).toContain('OLD');
    http.expectNone(r => r.url.startsWith('/api/v1/md/'));
  });

  it('resolves names for lines newly added by a snapshot refresh', () => {
    const { form, http } = open('7');
    const base = { id: 7, businessDate: '2026-10-14', status: 'Draft', remark: null, version: 1,
      scope: { warehouseIds: [4], categoryIds: [], itemIds: [] }, lines: [] };
    http.expectOne('/api/v1/inv/stocktakes/7').flush(base);
    http.expectOne('/api/v1/inv/stocktakes/7/saved-references').flush({ items: [], warehouses: [], locations: [] });
    form.refresh();
    http.expectOne('/api/v1/inv/stocktakes/7').flush({ ...base, version: 2 });
    http.expectOne('/api/v1/inv/stocktakes/7/refresh-snapshot').flush({ ...base, version: 3, lines: [{
      id: 11, itemId: 8, warehouseId: 4, locationId: null, bookQuantity: 2, countedQuantity: null,
      trackingMode: 'None', isAdded: false, recountRequired: true, tracking: [],
    }] });
    http.expectOne('/api/v1/inv/stocktakes/7/saved-references').flush({
      items: [{ id: 8, code: 'NEW', name: 'New item' }], warehouses: [{ id: 4, code: 'WH', name: 'Main' }], locations: [],
    });
    expect(form.itemName(8)).toContain('New item');
  });
});
