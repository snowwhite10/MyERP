import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { PermissionService } from '../../core/auth/permission.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { TAX_PERCENT_PATTERN, TaxRateDialog } from './tax-rate-dialog';
import { TaxCode } from './tax.service';

const vat: TaxCode = { id: 1, code: 'VAT5', name: 'Standard 5%', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 3 };
const q1 = { id: 8, taxCodeId: 1, effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', ratePercent: 5, version: 4 };
const API = '/api/v1/md/tax-codes/1/rates';

describe('MD-TAX-RATE-UI tax rate dialog', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.TAX_CODE.VIEW', 'MD.TAX_CODE.EDIT']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open(target = vat) {
    const fixture = TestBed.createComponent(TaxRateDialog);
    fixture.componentRef.setInput('taxCode', target);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const list = http.expectOne((r) => r.url === API);
    expect(list.request.params.get('sort')).toBe('-effectiveFrom');
    expect(list.request.params.has('on')).toBe(false);
    list.flush({ items: [{ ...q1, effectiveTo: null }], totalCount: 1 });
    fixture.detectChanges();
    return { dialog: fixture.componentInstance, http, fixture };
  }

  it('accepts percentages from 0 to 100 with at most 4 decimals', () => {
    expect(['0', '5', '5.25', '17.1234', '100', '100.0000', '0.0001'].every((x) => TAX_PERCENT_PATTERN.test(x))).toBe(true);
    expect(['', '-1', '100.0001', '101', '5.12345', '.5', '1e1', '005'].some((x) => TAX_PERCENT_PATTERN.test(x))).toBe(false);
  });

  it('filters by date, validates, then creates, edits and deletes with versions and notifies the list', () => {
    const { dialog, http } = open();
    const changed = vi.fn();
    dialog.changed.subscribe(changed);
    expect(document.body.querySelector('[data-testid="md-tax-rate-row-2026-01-01"]')?.textContent).toContain('Open-ended');
    dialog.onControl.setValue('2026-02-15'); dialog.search();
    expect(http.expectOne((r) => r.url === API).request.params.get('on')).toBe('2026-02-15');

    dialog.form.patchValue({ effectiveFrom: '2026-04-01', effectiveTo: '2026-03-31', ratePercent: '5' }); dialog.save();
    expect(dialog.form.hasError('period')).toBe(true);
    dialog.form.patchValue({ effectiveTo: '', ratePercent: '100.5' }); dialog.save();
    expect(dialog.form.controls.ratePercent.invalid).toBe(true);
    http.expectNone((r) => r.method === 'POST');
    dialog.form.patchValue({ ratePercent: '0' }); dialog.save();
    const post = http.expectOne((r) => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ effectiveFrom: '2026-04-01', effectiveTo: null, ratePercent: 0 });
    post.flush({ errorCode: 'MD.TAX_RATE.OVERLAP' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.error()).toBe('errors.MD.TAX_RATE.OVERLAP');
    expect(changed).not.toHaveBeenCalled();

    dialog.startEdit(q1);
    expect(dialog.form.getRawValue()).toEqual({ effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', ratePercent: '5' });
    dialog.form.patchValue({ ratePercent: '5.5' }); dialog.save();
    const put = http.expectOne(`${API}/8`);
    expect(put.request.body).toEqual({ effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', ratePercent: 5.5, version: 4 });
    put.flush({ ...q1, ratePercent: 5.5, version: 5 });
    http.expectOne((r) => r.url === API).flush({ items: [{ ...q1, version: 5 }], totalCount: 1 });
    expect(dialog.editing()).toBeNull();
    expect(changed).toHaveBeenCalledTimes(1);

    dialog.confirmDelete({ ...q1, version: 5 });
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.taxRate.deleteConfirm', params: { date: '2026-01-01' } }));
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('5');
    del.flush(null);
    http.expectOne((r) => r.url === API).flush({ items: [], totalCount: 0 });
    expect(dialog.rows()).toEqual([]);
    expect(changed).toHaveBeenCalledTimes(2);
  });

  it('hides the add form for an inactive tax code and resets when closed', () => {
    const { dialog, fixture } = open({ ...vat, isActive: false });
    expect(document.body.querySelector('[data-testid="md-tax-rate-inactive"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-tax-rate-form"]')).toBeNull();
    dialog.startEdit(q1); fixture.detectChanges();
    expect(document.body.querySelector('[data-testid="md-tax-rate-form"]')).not.toBeNull();
    fixture.componentRef.setInput('taxCode', null); TestBed.tick();
    expect(dialog.rows()).toEqual([]);
    expect(dialog.editing()).toBeNull();
  });

  it('read-only users see rates without edit controls', () => {
    granted.delete('MD.TAX_CODE.EDIT');
    open();
    expect(document.body.querySelector('[data-testid="md-tax-rate-row-2026-01-01"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-tax-rate-form"]')).toBeNull();
    expect(document.body.querySelector('[data-testid="md-tax-rate-edit-2026-01-01"]')).toBeNull();
  });
});
