import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of, Subject } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { OtherIssueForm } from './other-issue-form';
import { OtherIssueService } from './other-issue.service';
import { OtherReceiptForm } from './other-receipt-form';
import { OtherReceiptService } from './other-receipt.service';

describe.each([
  ['other-receipts', OtherReceiptForm],
  ['other-issues', OtherIssueForm],
] as const)('INV-SEL-005 %s form', (kind, component) => {
  const select = vi.fn();
  function open(id?: string) {
    select.mockReset();
    const company = signal(1);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { select } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }] });
    const fixture = TestBed.createComponent(component as Type<OtherReceiptForm>);
    TestBed.tick(); fixture.detectChanges();
    return { form: fixture.componentInstance, fixture, http: TestBed.inject(HttpTestingController), company };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('selects only confirmed options, clears location on warehouse change and never preloads MD', () => {
    const { form, http } = open();
    http.expectNone(r => r.url.startsWith('/api/v1/md/'));
    const line = form.draft.lines[0];
    select.mockReturnValueOnce(of(null));
    form.chooseItem(line);
    expect(select).toHaveBeenCalledTimes(1);
    expect(line.itemId).toBeNull();
    select.mockReturnValueOnce(of([{ id: 4, code: 'SN', name: 'Serial', trackingMode: 'Serial' }]));
    form.chooseItem(line);
    expect(line.itemId).toBe(4);
    expect(form.mode(line)).toBe('Serial');
    line.tracking = [{ serialNo: 'S-1', quantity: '1' }];
    select.mockReturnValueOnce(of([{ id: 4, code: 'SN', name: 'Serial', trackingMode: 'Serial' }]));
    form.chooseItem(line);
    expect(line.tracking).toEqual([{ serialNo: 'S-1', quantity: '1' }]);
    line.locationId = 20;
    select.mockReturnValueOnce(of([{ id: 6, code: 'WH', name: 'Warehouse' }]));
    form.chooseWarehouse(line);
    expect(line.warehouseId).toBe(6);
    expect(line.locationId).toBeNull();
    select.mockReturnValueOnce(of([{ id: 21, code: 'BIN', name: 'Bin' }]));
    form.chooseLocation(line);
    expect(line.locationId).toBe(21);
    const stale = new Subject<{ id: number; code: string; name: string }[] | null>();
    select.mockReturnValueOnce(stale.asObservable());
    form.chooseLocation(line);
    line.warehouseId = 7; line.locationId = null;
    stale.next([{ id: 22, code: 'OLD', name: 'Old warehouse bin' }]);
    expect(line.locationId).toBeNull();
  });

  it('shows saved inactive references without making them new choices', () => {
    const { form, fixture, http } = open('8');
    http.expectOne(`/api/v1/inv/${kind}/8`).flush({ id: 8, businessDate: '2026-10-10', remark: '', version: 1,
      lines: [{ itemId: 4, warehouseId: 6, locationId: 21, quantity: 1, unitCost: 0, tracking: [] }] });
    http.expectOne(`/api/v1/inv/${kind}/8/saved-references`).flush({
      items: [{ id: 4, code: 'OLD', name: 'Inactive item', trackingMode: 'Lot', isActive: false }],
      warehouses: [{ id: 6, code: 'WH', name: 'Inactive warehouse', isActive: false }],
      locations: [{ id: 21, code: 'BIN', name: 'Inactive bin', isActive: false }],
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Inactive item');
    expect(form.mode(form.draft.lines[0])).toBe('Lot');
    http.expectNone(r => r.url.startsWith('/api/v1/md/'));
  });

  it('keeps a newly selected option when saved references arrive later', () => {
    const { form, http } = open('8');
    http.expectOne(`/api/v1/inv/${kind}/8`).flush({ id: 8, businessDate: '2026-10-10', remark: '', version: 1,
      lines: [{ itemId: 4, warehouseId: 6, locationId: null, quantity: 1, unitCost: 0, tracking: [] }] });
    const saved = http.expectOne(`/api/v1/inv/${kind}/8/saved-references`);
    select.mockReturnValue(of([{ id: 9, code: 'NEW', name: 'New item', trackingMode: 'Serial' }]));
    form.chooseItem(form.draft.lines[0]);
    saved.flush({ items: [{ id: 4, code: 'OLD', name: 'Old item', isActive: false }], warehouses: [], locations: [] });
    expect(form.label(form.items(), 9)).toContain('New item');
    expect(form.mode(form.draft.lines[0])).toBe('Serial');
    expect(form.label(form.items(), 4)).toContain('Old item');
  });

  it('shows the MD guard reason when a selected reference becomes inactive', () => {
    open();
    const response = new HttpErrorResponse({ status: 422, error: { errorCode: 'MD.ITEM.INACTIVE' } });
    const key = kind === 'other-receipts' ? TestBed.inject(OtherReceiptService).errorKey(response)
      : TestBed.inject(OtherIssueService).errorKey(response);
    expect(key).toBe('errors.MD.ITEM.INACTIVE');
  });

  it('ignores saved references and details returned after a company switch', () => {
    const { form, http, company } = open('8');
    const oldDetail = http.expectOne(`/api/v1/inv/${kind}/8`);
    const oldRefs = http.expectOne(`/api/v1/inv/${kind}/8/saved-references`);
    company.set(2); TestBed.tick();
    const newDetail = http.expectOne(`/api/v1/inv/${kind}/8`);
    const newRefs = http.expectOne(`/api/v1/inv/${kind}/8/saved-references`);
    oldDetail.flush({ id: 8, businessDate: '2026-01-01', remark: '', version: 1, lines: [] });
    oldRefs.flush({ items: [{ id: 4, code: 'OLD', name: 'Old company' }], warehouses: [], locations: [] });
    newDetail.flush({ id: 8, businessDate: '2026-10-10', remark: '', version: 2, lines: [] });
    newRefs.flush({ items: [{ id: 9, code: 'NEW', name: 'New company' }], warehouses: [], locations: [] });
    expect(form.draft.businessDate).toBe('2026-10-10');
    expect(form.items().map(x => x.id)).toEqual([9]);
  });
});
