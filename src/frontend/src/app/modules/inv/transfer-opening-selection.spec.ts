import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, Type, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of, Subject } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { TransferForm } from './transfer-form';
import { OpeningStockForm } from './opening-stock-form';

describe('INV-SEL-004–006 transfer and opening selection', () => {
  const select = vi.fn();
  function open<T extends TransferForm | OpeningStockForm>(component: Type<T>, id?: string) {
    select.mockReset();
    const company = signal(1);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { select } }, { provide: CompanyContextService, useValue: { current: company } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }] });
    const fixture = TestBed.createComponent(component);
    TestBed.tick(); fixture.detectChanges();
    return { form: fixture.componentInstance, fixture, http: TestBed.inject(HttpTestingController), company };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('transfer keeps source and destination locations independent and ignores cancelled and stale choices', () => {
    const { form, http } = open(TransferForm);
    http.expectNone(r => r.url.startsWith('/api/v1/md/'));
    const line = form.draft.lines[0];
    select.mockReturnValueOnce(of(null)); form.chooseItem(line); expect(line.itemId).toBeNull();
    select.mockReturnValueOnce(of([{ id: 4, code: 'S', name: 'Serial', trackingMode: 'Serial' }]));
    form.chooseItem(line); expect(form.mode(line)).toBe('Serial');
    line.tracking = [{ serialNo: 'SN1', quantity: '1' }];
    select.mockReturnValueOnce(of([{ id: 4, code: 'S', name: 'Serial', trackingMode: 'Serial' }]));
    form.chooseItem(line); expect(line.tracking).toHaveLength(1);
    select.mockReturnValueOnce(of([{ id: 2, code: 'A', name: 'A' }])); form.chooseSourceWarehouse(line);
    select.mockReturnValueOnce(of([{ id: 3, code: 'B', name: 'B' }])); form.chooseDestinationWarehouse(line);
    select.mockReturnValueOnce(of([{ id: 21, code: 'A1', name: 'Source bin' }])); form.chooseSourceLocation(line);
    select.mockReturnValueOnce(of([{ id: 31, code: 'B1', name: 'Destination bin' }])); form.chooseDestinationLocation(line);
    expect([line.sourceLocationId, line.destinationLocationId]).toEqual([21, 31]);
    const stale = new Subject<{ id: number; code: string; name: string }[] | null>();
    select.mockReturnValueOnce(stale.asObservable()); form.chooseSourceLocation(line);
    select.mockReturnValueOnce(of([{ id: 5, code: 'C', name: 'C' }])); form.chooseSourceWarehouse(line);
    stale.next([{ id: 99, code: 'OLD', name: 'Old bin' }]);
    expect([line.sourceLocationId, line.destinationLocationId]).toEqual([null, 31]);
    expect(select.mock.calls.at(-2)?.[0].load).toBeTypeOf('function');
  });

  it('transfer displays saved inactive references and ignores old company responses', () => {
    const { form, http, fixture, company } = open(TransferForm, '8');
    const detail = http.expectOne('/api/v1/inv/transfers/8');
    const refs = http.expectOne('/api/v1/inv/transfers/8/saved-references');
    detail.flush({ id: 8, businessDate: '2026-10-14', remark: '', version: 1, lines: [{ itemId: 4,
      sourceWarehouseId: 2, sourceLocationId: 21, destinationWarehouseId: 3, destinationLocationId: 31, quantity: 1, tracking: [] }] });
    refs.flush({ items: [{ id: 4, code: 'OLD', name: 'Inactive item', trackingMode: 'Lot', isActive: false }],
      warehouses: [{ id: 2, code: 'A', name: 'Source', isActive: false }, { id: 3, code: 'B', name: 'Destination' }],
      locations: [{ id: 21, code: 'A1', name: 'Source bin' }, { id: 31, code: 'B1', name: 'Destination bin' }] });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Inactive item');
    expect(form.mode(form.draft.lines[0])).toBe('Lot');
    company.set(2); TestBed.tick();
    const nextDetail = http.expectOne('/api/v1/inv/transfers/8');
    const nextRefs = http.expectOne('/api/v1/inv/transfers/8/saved-references');
    nextDetail.flush({ id: 8, businessDate: '2026-10-15', remark: '', version: 2, lines: [] });
    nextRefs.flush({ items: [], warehouses: [], locations: [] });
    expect(form.items()).toEqual([]);
  });

  it('opening selection invalidates preview only when confirmed and uses tracking mode', () => {
    const { form, http } = open(OpeningStockForm);
    http.expectNone(r => r.url.startsWith('/api/v1/md/'));
    const line = form.draft.lines[0];
    select.mockReturnValueOnce(of(null)); form.chooseItem(line); expect(line.itemId).toBeNull();
    select.mockReturnValueOnce(of([{ id: 4, code: 'LOT', name: 'Lot item', trackingMode: 'Lot' }]));
    form.chooseItem(line); expect(form.mode(line)).toBe('Lot');
    select.mockReturnValueOnce(of([{ id: 2, code: 'WH', name: 'Warehouse' }])); form.chooseWarehouse(line);
    select.mockReturnValueOnce(of([{ id: 21, code: 'B', name: 'Bin' }])); form.chooseLocation(line);
    expect([line.itemId, line.warehouseId, line.locationId]).toEqual([4, 2, 21]);
    select.mockReturnValueOnce(of([{ id: 3, code: 'WH2', name: 'Warehouse 2' }])); form.chooseWarehouse(line);
    expect(line.locationId).toBeNull();
  });
});
