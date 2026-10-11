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
import { CurrencyPage } from './currency-page';

const twd = { id: 1, code: 'TWD', name: 'New Taiwan Dollar', symbol: 'NT$', decimalPlaces: 0, isBase: true, isActive: true, version: 3 };
const usd = { id: 2, code: 'USD', name: 'US Dollar', symbol: '$', decimalPlaces: 2, isBase: false, isActive: true, version: 2 };
const API = '/api/v1/md/currencies';

describe('MD-CUR-UI currency page', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.CURRENCY.VIEW', 'MD.CURRENCY.CREATE', 'MD.CURRENCY.EDIT', 'MD.CURRENCY.DELETE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(CurrencyPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === API).flush({ items: [twd, usd], totalCount: 2 });
    fixture.detectChanges();
    return { page: fixture.componentInstance, http, element: fixture.nativeElement as HTMLElement };
  }

  it('marks the base currency and hides its rates and delete buttons', () => {
    const { element } = open();
    expect(element.querySelector('[data-testid="md-currency-base-TWD"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="md-currency-base-USD"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-currency-rates-TWD"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-currency-delete-TWD"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-currency-rates-USD"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="md-currency-delete-USD"]')).not.toBeNull();
  });

  it('validates ISO code and decimals, then sends create and versioned update', () => {
    const { page, http } = open();
    page.openCreate(); page.form.patchValue({ code: 'US', name: 'Euro' }); page.save();
    http.expectNone((r) => r.method === 'POST');
    page.form.patchValue({ code: 'eur', decimalPlaces: 5 }); page.save();
    http.expectNone((r) => r.method === 'POST');
    page.form.patchValue({ decimalPlaces: 2, symbol: ' € ', isBase: false }); page.save();
    const post = http.expectOne((r) => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'EUR', name: 'Euro', symbol: '€', decimalPlaces: 2, isBase: false });
    post.flush({ ...usd, id: 4, code: 'EUR' });
    http.expectOne((r) => r.url === API && r.method === 'GET').flush({ items: [twd, usd], totalCount: 2 });
    expect(page.dialogOpen()).toBe(false);

    // 本位幣不可直接取消；把外幣設為本位幣時送出 isBase=true。
    page.openEdit(twd);
    expect(page.form.controls.code.disabled).toBe(true);
    expect(page.form.controls.isBase.disabled).toBe(true);
    page.closeDialog();
    page.openEdit(usd);
    expect(page.form.controls.isBase.enabled).toBe(true);
    page.form.patchValue({ isBase: true }); page.save();
    const put = http.expectOne(`${API}/2`);
    expect(put.request.body).toEqual({ name: 'US Dollar', symbol: '$', decimalPlaces: 2, isBase: true, isActive: true, version: 2 });
    put.flush({ errorCode: 'MD.CURRENCY.BASE_LOCKED' }, { status: 409, statusText: 'Conflict' });
    expect(page.dialogOpen()).toBe(true);
    expect(page.saveError()).toBe('errors.MD.CURRENCY.BASE_LOCKED');
  });

  it('confirms delete with version, shows IN_USE and opens the rate dialog', () => {
    const { page, http, element } = open();
    page.confirmDelete(usd);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.currency.deleteConfirm', params: { code: 'USD' } }));
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('2');
    del.flush({ errorCode: 'MD.CURRENCY.IN_USE' }, { status: 409, statusText: 'Conflict' });
    expect(page.error()).toBe('errors.MD.CURRENCY.IN_USE');

    element.querySelector<HTMLButtonElement>('[data-testid="md-currency-rates-USD"]')!.click();
    TestBed.tick();
    http.expectOne((r) => r.url === `${API}/2/rates`).flush({ items: [], totalCount: 0 });
    expect(page.rateCurrency()).toEqual(usd);
  });

  it('hides create, edit and delete without permissions', () => {
    granted = new Set(['MD.CURRENCY.VIEW']);
    const { element } = open();
    expect(element.querySelector('[data-testid="md-currency-create"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-currency-edit-USD"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-currency-delete-USD"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-currency-rates-USD"]')).not.toBeNull();
  });
});
