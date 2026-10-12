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
import { TaxCodePage } from './tax-code-page';
import { TaxCode } from './tax.service';

const vat: TaxCode = { id: 1, code: 'VAT5', name: 'Standard 5%', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 3 };
const free: TaxCode = { id: 2, code: 'FREE', name: 'Exempt', taxType: 'Output', currentRatePercent: null, isActive: true, version: 1 };
const API = '/api/v1/md/tax-codes';

describe('MD-TAX-UI tax code page', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.TAX_CODE.VIEW', 'MD.TAX_CODE.CREATE', 'MD.TAX_CODE.EDIT', 'MD.TAX_CODE.DELETE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(TaxCodePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === API).flush({ items: [vat, free], totalCount: 2 });
    fixture.detectChanges();
    return { page: fixture.componentInstance, http, element: fixture.nativeElement as HTMLElement };
  }

  it('shows the type and today rate, or a no-rate label', () => {
    const { element } = open();
    expect(element.querySelector('[data-testid="md-tax-code-row-VAT5"]')?.textContent).toContain('Sales and purchases');
    expect(element.querySelector('[data-testid="md-tax-code-rate-VAT5"]')?.textContent).toContain('5%');
    expect(element.querySelector('[data-testid="md-tax-code-rate-FREE"]')?.textContent).toContain('No rate today');
  });

  it('validates the code, then sends create and versioned update with the tax type', () => {
    const { page, http } = open();
    page.openCreate(); page.form.patchValue({ code: 'VAT 5', name: 'Zero' }); page.save();
    http.expectNone((r) => r.method === 'POST');
    page.form.patchValue({ code: 'zero', name: ' ' }); page.save();
    http.expectNone((r) => r.method === 'POST');
    page.form.patchValue({ name: 'Zero rated', taxType: 'Output' }); page.save();
    const post = http.expectOne((r) => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'ZERO', name: 'Zero rated', taxType: 'Output' });
    post.flush({ errorCode: 'MD.TAX_CODE.DUPLICATE_CODE' }, { status: 409, statusText: 'Conflict' });
    expect(page.saveError()).toBe('errors.MD.TAX_CODE.DUPLICATE_CODE');
    page.closeDialog();

    page.openEdit(vat);
    expect(page.form.controls.code.disabled).toBe(true);
    page.form.patchValue({ taxType: 'Input', isActive: false }); page.save();
    const put = http.expectOne(`${API}/1`);
    expect(put.request.body).toEqual({ name: 'Standard 5%', taxType: 'Input', isActive: false, version: 3 });
    put.flush({ ...vat, taxType: 'Input', isActive: false, version: 4 });
    http.expectOne((r) => r.url === API && r.method === 'GET').flush({ items: [vat, free], totalCount: 2 });
    expect(page.dialogOpen()).toBe(false);
  });

  it('confirms delete with version, shows IN_USE and opens rate and calculator dialogs', () => {
    const { page, http, element } = open();
    page.confirmDelete(vat);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.taxCode.deleteConfirm', params: { code: 'VAT5' } }));
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('3');
    del.flush({ errorCode: 'MD.TAX_CODE.IN_USE' }, { status: 409, statusText: 'Conflict' });
    expect(page.error()).toBe('errors.MD.TAX_CODE.IN_USE');

    element.querySelector<HTMLButtonElement>('[data-testid="md-tax-code-rates-VAT5"]')!.click();
    TestBed.tick();
    http.expectOne((r) => r.url === `${API}/1/rates`).flush({ items: [], totalCount: 0 });
    expect(page.rateTaxCode()).toEqual(vat);
    element.querySelector<HTMLButtonElement>('[data-testid="md-tax-code-calc-FREE"]')!.click();
    TestBed.tick();
    expect(page.calcTaxCode()).toEqual(free);
  });

  it('hides create, edit and delete without permissions but keeps rates and calculation', () => {
    granted = new Set(['MD.TAX_CODE.VIEW']);
    const { element } = open();
    expect(element.querySelector('[data-testid="md-tax-code-create"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-tax-code-edit-VAT5"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-tax-code-delete-VAT5"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-tax-code-rates-VAT5"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="md-tax-code-calc-VAT5"]')).not.toBeNull();
  });
});
