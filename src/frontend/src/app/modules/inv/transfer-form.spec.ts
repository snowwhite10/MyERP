import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { TransferForm } from './transfer-form';

const API = '/api/v1/inv/transfers';
const items = [{ id: 1, code: 'A001', name: 'Bolt', isActive: true, trackingMode: 'None' },
  { id: 2, code: 'S001', name: 'Scanner', isActive: true, trackingMode: 'Serial' },
  { id: 9, code: 'OLD', name: 'Old', isActive: false, trackingMode: 'None' }];
const warehouses = [{ id: 2, code: 'WH1', name: 'Main', isActive: true }, { id: 3, code: 'WH2', name: 'Spare', isActive: true }];

describe('INV-TRF-009 transfer form', () => {
  function open(id: string | null = null) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }] });
    const fixture = TestBed.createComponent(TransferForm);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === '/api/v1/md/items').flush({ items, totalCount: items.length });
    http.expectOne(r => r.url === '/api/v1/md/warehouses').flush({ items: warehouses, totalCount: 2 });
    fixture.detectChanges();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    return { form: fixture.componentInstance, http, fixture, navigate, element: fixture.nativeElement as HTMLElement };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('offers active items and rejects an identical source and destination before calling the API', () => {
    const { form, http, fixture, element } = open();
    expect(form.items().map(x => x.id)).toEqual([1, 2]);
    form.draft.businessDate = '2026-10-14';
    Object.assign(form.draft.lines[0], { itemId: 1, sourceWarehouseId: 2, destinationWarehouseId: 2, quantity: '1' });
    form.save();
    http.expectNone(API);
    expect(form.error()).toBe('inv.transfer.sameKey');
    fixture.detectChanges();
    expect(element.querySelector('.warn')!.textContent).toContain('must not be identical');
    form.draft.lines[0].quantity = '0'; form.draft.lines[0].destinationWarehouseId = 3; form.save();
    expect(form.error()).toBe('inv.transfer.invalid');
  });

  it('saves a tracked draft without serials but rejects a mismatched serial list', () => {
    const { form, http, navigate } = open();
    form.draft.businessDate = '2026-10-14';
    Object.assign(form.draft.lines[0], { itemId: 2, sourceWarehouseId: 2, destinationWarehouseId: 3, quantity: '2' });
    form.draft.lines[0].tracking = [{ serialNo: 'SN1', quantity: '1' }];
    form.save();
    http.expectNone(API);
    expect(form.error()).toBe('errors.INV.TRACKING.REQUIRED');
    form.draft.lines[0].tracking = [];
    form.save();
    const create = http.expectOne(r => r.method === 'POST' && r.url === API);
    expect(create.request.body.lines[0]).toMatchObject({ lineNo: 1, sourceWarehouseId: 2, destinationWarehouseId: 3, quantity: '2' });
    expect(create.request.body.lines[0].tracking).toBeUndefined();
    create.flush({ id: 5 }, { status: 201, statusText: 'Created' });
    expect(navigate).toHaveBeenCalledWith('/inv/transfers');
  });

  it('loads a rejected transfer with both locations and saves the revision with its version', async () => {
    const { form, http, fixture, element } = open('8');
    http.expectOne(`${API}/8`).flush({ id: 8, documentNo: 'TR-1', businessDate: '2026-10-10', status: 'Rejected', remark: 'r', version: 4,
      postedAt: null, approval: null, lines: [{ id: 1, lineNo: 1, itemId: 2, sourceWarehouseId: 2, sourceLocationId: 21,
        destinationWarehouseId: 3, destinationLocationId: 31, quantity: 1, tracking: [{ lotNo: null, serialNo: 'SN1', quantity: 1, manufactureDate: null, expiryDate: null }] }] });
    http.expectOne(r => r.url === '/api/v1/md/warehouses/2/locations').flush({ items: [{ id: 21, code: 'A1', name: 'Shelf', isActive: true }], totalCount: 1 });
    http.expectOne(r => r.url === '/api/v1/md/warehouses/3/locations').flush({ items: [{ id: 31, code: 'B1', name: 'Bin', isActive: true }], totalCount: 1 });
    expect(form.locations(3).map(x => x.id)).toEqual([31]);
    // 詳情晚於 MD 選項回應時，OnPush 表單仍須重繪載入的內容。
    fixture.detectChanges(); await fixture.whenStable();
    expect((element.querySelector('[data-testid="inv-transfer-remark"]') as HTMLInputElement).value).toBe('r');
    form.draft.remark = 'fixed';
    form.save();
    const put = http.expectOne(r => r.method === 'PUT' && r.url === `${API}/8`);
    expect(put.request.body).toMatchObject({ version: 4, remark: 'fixed', lines: [{ sourceLocationId: 21, destinationLocationId: 31,
      quantity: '1', tracking: [{ serialNo: 'SN1', quantity: '1' }] }] });
    put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(form.error()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    expect(form.saving()).toBe(false);
  });
});
