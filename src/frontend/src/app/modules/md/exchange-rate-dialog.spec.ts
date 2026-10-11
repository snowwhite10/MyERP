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
import { ExchangeRateDialog, RATE_PATTERN } from './exchange-rate-dialog';

const usd = { id: 2, code: 'USD', name: 'US Dollar', symbol: '$', decimalPlaces: 2, isBase: false, isActive: true, version: 2 };
const q1 = { id: 8, currencyId: 2, effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', rate: 31.5, version: 4 };
const API = '/api/v1/md/currencies/2/rates';

describe('MD-CUR-RATE-UI exchange rate dialog', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.CURRENCY.VIEW', 'MD.CURRENCY.EDIT']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open(target = usd) {
    const fixture = TestBed.createComponent(ExchangeRateDialog);
    fixture.componentRef.setInput('currency', target);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const list = http.expectOne((r) => r.url === API);
    expect(list.request.params.get('sort')).toBe('-effectiveFrom');
    expect(list.request.params.has('on')).toBe(false);
    list.flush({ items: [{ ...q1, effectiveTo: null }], totalCount: 1 });
    fixture.detectChanges();
    return { dialog: fixture.componentInstance, http, fixture };
  }

  it('accepts at most 6 integer and 8 decimal digits', () => {
    expect(['1', '32.15', '0.00012345', '999999.12345678'].every((x) => RATE_PATTERN.test(x))).toBe(true);
    expect(['', '-1', '1.123456789', '1234567', '1e3', '.5'].some((x) => RATE_PATTERN.test(x))).toBe(false);
  });

  it('filters by date, validates the period and rate, then creates, edits and deletes with versions', () => {
    const { dialog, http } = open();
    expect(document.body.querySelector('[data-testid="md-rate-row-2026-01-01"]')?.textContent).toContain('Open-ended');
    dialog.onControl.setValue('2026-02-15'); dialog.search();
    expect(http.expectOne((r) => r.url === API).request.params.get('on')).toBe('2026-02-15');

    dialog.form.patchValue({ effectiveFrom: '2026-04-01', effectiveTo: '2026-03-31', rate: '32' }); dialog.save();
    expect(dialog.form.hasError('period')).toBe(true);
    dialog.form.patchValue({ effectiveTo: '', rate: '0' }); dialog.save();
    expect(dialog.form.controls.rate.invalid).toBe(true);
    dialog.form.patchValue({ rate: '1.123456789' }); dialog.save();
    http.expectNone((r) => r.method === 'POST');
    dialog.form.patchValue({ rate: '32.12345678' }); dialog.save();
    const post = http.expectOne((r) => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ effectiveFrom: '2026-04-01', effectiveTo: null, rate: 32.12345678 });
    post.flush({ errorCode: 'MD.EXCHANGE_RATE.OVERLAP' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.error()).toBe('errors.MD.EXCHANGE_RATE.OVERLAP');

    dialog.startEdit(q1);
    expect(dialog.form.getRawValue()).toEqual({ effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', rate: '31.5' });
    dialog.form.patchValue({ rate: '31.25' }); dialog.save();
    const put = http.expectOne(`${API}/8`);
    expect(put.request.body).toEqual({ effectiveFrom: '2026-01-01', effectiveTo: '2026-03-31', rate: 31.25, version: 4 });
    put.flush({ ...q1, rate: 31.25, version: 5 });
    http.expectOne((r) => r.url === API).flush({ items: [{ ...q1, version: 5 }], totalCount: 1 });
    expect(dialog.editing()).toBeNull();

    dialog.confirmDelete({ ...q1, version: 5 });
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.exchangeRate.deleteConfirm', params: { date: '2026-01-01' } }));
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('5');
    del.flush(null);
    http.expectOne((r) => r.url === API).flush({ items: [], totalCount: 0 });
    expect(dialog.rows()).toEqual([]);
  });

  it('hides the add form for an inactive currency and resets when closed', () => {
    const { dialog, fixture } = open({ ...usd, isActive: false });
    expect(document.body.querySelector('[data-testid="md-rate-inactive"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-rate-form"]')).toBeNull();
    dialog.startEdit(q1); fixture.detectChanges();
    expect(document.body.querySelector('[data-testid="md-rate-form"]')).not.toBeNull();
    fixture.componentRef.setInput('currency', null); TestBed.tick();
    expect(dialog.rows()).toEqual([]);
    expect(dialog.editing()).toBeNull();
  });

  it('read-only users see rates without edit controls', () => {
    granted.delete('MD.CURRENCY.EDIT');
    open();
    expect(document.body.querySelector('[data-testid="md-rate-row-2026-01-01"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-rate-form"]')).toBeNull();
    expect(document.body.querySelector('[data-testid="md-rate-edit-2026-01-01"]')).toBeNull();
  });
});
