// 介面 8.55（REQ_F4118.17.4）：系統參數頁、API 服務、用戶端檢查、批次保存與錯誤處理。
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { firstValueFrom } from 'rxjs';
import { seedSession } from '../../../../testing/auth-fixtures';
import { provideTestTranslations } from '../../../../testing/i18n-fixtures';
import { AUTHORIZATION_API, PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { APP_ICONS } from '../../../core/icons';
import { ParameterSettings } from './parameter-settings';
import {
  ParameterDetail, parameterErrorKey, parameterFieldErrors, PARAMETERS_API, ParameterService, parameterValueError,
} from './parameter.service';

const param = (overrides: Partial<ParameterDetail> = {}): ParameterDetail => ({
  key: 'SYS.QUANTITY_DECIMALS', module: 'SYS', group: 'precision', type: 'WholeNumber', value: '2', defaultValue: '2',
  isDefault: true, minimum: 0, maximum: 4, maxLength: 11, options: [], version: 0, ...overrides,
});
const list = (): ParameterDetail[] => [
  param(),
  param({ key: 'SYS.UNIT_PRICE_DECIMALS', value: '5', defaultValue: '4', isDefault: false, maximum: 6, version: 3 }),
  param({ key: 'SYS.FLAG', group: 'other', type: 'Boolean', value: 'false', defaultValue: 'false', minimum: null, maximum: null }),
  param({ key: 'SYS.MODE', group: 'other', type: 'Option', value: 'A', defaultValue: 'A', minimum: null, maximum: null, options: ['A', 'B'] }),
  param({ key: 'SYS.NOTE', group: 'other', type: 'Text', value: '', defaultValue: '', minimum: null, maximum: null, maxLength: 5 }),
];
const httpError = (status: number, body: object) => new HttpErrorResponse({ status, error: body });

describe('SYS parameters', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    seedSession();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
        { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  async function grant(codes: string[]): Promise<void> {
    const done = firstValueFrom(TestBed.inject(PermissionService).load());
    controller.expectOne(AUTHORIZATION_API).flush({
      companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }, { id: 2, code: 'HK01', name: 'Hong Kong', isDefault: false }],
      companyId: 1, isFullAccess: false, permissions: codes.map((code) => ({ code, dataScope: 'Company' })),
    });
    await done;
  }
  const expectGet = (): TestRequest => controller.expectOne((r) => r.method === 'GET' && r.url === PARAMETERS_API);
  const expectPut = (): TestRequest => controller.expectOne((r) => r.method === 'PUT' && r.url === PARAMETERS_API);

  describe('service and helpers', () => {
    it('lists with an optional module filter and saves items as one batch', () => {
      const api = TestBed.inject(ParameterService);
      api.list('SYS').subscribe();
      expect(expectGet().request.params.get('module')).toBe('SYS');
      api.list().subscribe();
      const all = expectGet();
      expect(all.request.params.has('module')).toBe(false);
      all.flush([]);
      api.save([{ key: 'SYS.QUANTITY_DECIMALS', value: '3', version: 0 }]).subscribe();
      const put = expectPut();
      expect(put.request.body).toEqual({ items: [{ key: 'SYS.QUANTITY_DECIMALS', value: '3', version: 0 }] });
      put.flush([]);
      controller.match(() => true).forEach((r) => r.flush([]));
    });

    it('checks values by type before sending', () => {
      const [whole, decimals, flag, mode, note] = list();
      expect(parameterValueError(whole, '3')).toBeNull();
      expect(parameterValueError(whole, '1.5')).toBe('sys.parameters.errors.number');
      expect(parameterValueError(whole, '')).toBe('sys.parameters.errors.number');
      expect(parameterValueError(whole, '5')).toBe('sys.parameters.errors.range');
      expect(parameterValueError(decimals, '-1')).toBe('sys.parameters.errors.range');
      expect(parameterValueError({ ...whole, type: 'Number', minimum: null, maximum: null }, '12.50')).toBeNull();
      expect(parameterValueError({ ...whole, type: 'Number' }, '1e3')).toBe('sys.parameters.errors.number');
      expect(parameterValueError(flag, 'true')).toBeNull();
      expect(parameterValueError(flag, 'yes')).toBe('sys.parameters.errors.invalid');
      expect(parameterValueError(mode, 'B')).toBeNull();
      expect(parameterValueError(mode, 'C')).toBe('sys.parameters.errors.invalid');
      expect(parameterValueError(note, '12345')).toBeNull();
      expect(parameterValueError(note, '123456')).toBe('sys.parameters.errors.length');
      expect(parameterValueError(note, ' 12345 ')).toBeNull(); // 伺服器去除前後空白後才檢查長度
    });

    it('maps API errors and items[i] validation fields to parameter keys', () => {
      const sent = [{ key: 'SYS.A', value: '1', version: 0 }, { key: 'SYS.B', value: 'x', version: 2 }];
      expect(parameterFieldErrors(httpError(400, { errors: { 'items[1].value': ['bad'], items: ['x'], 'items[9].key': ['?'] } }), sent))
        .toEqual(['SYS.B']);
      expect(parameterFieldErrors(httpError(409, {}), sent)).toEqual([]);
      expect(parameterFieldErrors(new Error('x'), sent)).toEqual([]);
      expect(parameterErrorKey(httpError(409, { errorCode: 'COMMON.CONCURRENCY.CONFLICT' }))).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
      expect(parameterErrorKey(httpError(400, { errors: {} }))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    });
  });

  describe('page', () => {
    let fixture: ComponentFixture<ParameterSettings>;
    const el = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
    async function render(): Promise<void> { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
    async function open(codes = ['SYS.PARAMETER.VIEW', 'SYS.PARAMETER.EDIT']): Promise<ParameterSettings> {
      await grant(codes);
      fixture = TestBed.createComponent(ParameterSettings);
      await render();
      expectGet().flush(list());
      await render();
      return fixture.componentInstance;
    }

    it('groups parameters by module and group with translated labels, defaults and ranges', async () => {
      const page = await open();
      expect(page.groups().map((g) => [g.id, g.items.length])).toEqual([['SYS-precision', 2], ['SYS-other', 3]]);
      expect(el('param-group-SYS-precision')!.textContent).toContain('Precision');
      expect(el('param-row-SYS.QUANTITY_DECIMALS')!.textContent).toContain('Quantity decimal places');
      expect(el('param-row-SYS.QUANTITY_DECIMALS')!.textContent).toContain('Range 0–4');
      expect(el('param-default-SYS.UNIT_PRICE_DECIMALS')!.textContent).toContain('Default: 4');
      expect(el('param-default-SYS.FLAG')!.textContent).toContain('Default: No');
      expect(el('param-restore-SYS.UNIT_PRICE_DECIMALS')).not.toBeNull();
      expect(el('param-restore-SYS.QUANTITY_DECIMALS')).toBeNull();
      expect((el('param-save') as HTMLButtonElement).disabled).toBe(true);
      expect(page.hasUnsavedChanges()).toBe(false);
    });

    it('sends only changed items with their versions and shows the saved list', async () => {
      const page = await open();
      const [quantity, price, flag] = page.parameters();
      page.setValue(quantity, '3');
      page.setValue(price, price.defaultValue);
      page.setValue(flag, 'true');
      page.setValue(flag, 'false'); // 改回原值即不算修改
      await render();
      expect(el('param-changed-SYS.QUANTITY_DECIMALS')).not.toBeNull();
      expect(page.hasUnsavedChanges()).toBe(true);
      (el('param-save') as HTMLButtonElement).click();
      const put = expectPut();
      expect(put.request.body).toEqual({ items: [
        { key: 'SYS.QUANTITY_DECIMALS', value: '3', version: 0 },
        { key: 'SYS.UNIT_PRICE_DECIMALS', value: '4', version: 3 },
      ] });
      put.flush(list().map((p) => p.key === 'SYS.QUANTITY_DECIMALS' ? { ...p, value: '3', isDefault: false, version: 1 } : p));
      await render();
      expect(page.dirty()).toBe(false);
      expect(page.parameters()[0].version).toBe(1);
      expect(el('param-notice')!.textContent).toContain('Parameters saved.');
    });

    it('blocks saving invalid values and shows the field error', async () => {
      const page = await open();
      const input = el('param-input-SYS.QUANTITY_DECIMALS') as HTMLInputElement;
      input.value = '9';
      input.dispatchEvent(new Event('input'));
      await render();
      expect(el('param-error-SYS.QUANTITY_DECIMALS')!.textContent).toContain('Enter a value from 0 to 4.');
      expect(page.invalid()).toBe(true);
      expect((el('param-save') as HTMLButtonElement).disabled).toBe(true);
      page.save();
      controller.expectNone((r) => r.method === 'PUT');
      page.discard();
      await render();
      expect(el('param-error-SYS.QUANTITY_DECIMALS')).toBeNull();
    });

    it('marks server validation errors and offers reload on version conflict', async () => {
      const page = await open();
      const [, , , mode, note] = page.parameters();
      page.setValue(mode, 'B');
      page.setValue(note, 'abc');
      page.save();
      expectPut().flush({ errors: { 'items[1].value': ['Invalid'] } }, { status: 400, statusText: 'Bad Request' });
      await render();
      expect(page.saveError()).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
      expect(el('param-error-SYS.NOTE')!.textContent).toContain('Invalid value.');
      expect(el('param-error-SYS.MODE')).toBeNull();
      page.setValue(note, 'abd');
      expect(page.fieldError(note)).toBeNull();
      page.save();
      expectPut().flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
      await render();
      expect(page.conflict()).toBe(true);
      expect(page.dirty()).toBe(true);
      (el('param-conflict-reload') as HTMLButtonElement).click();
      expectGet().flush(list());
      await render();
      expect(page.dirty()).toBe(false);
      expect(page.saveError()).toBeNull();
    });

    it('ignores whitespace-only edits and trims values before sending', async () => {
      const page = await open();
      const [quantity, , , , note] = page.parameters();
      page.setValue(quantity, ' 2 ');
      page.setValue(note, '   ');
      expect(page.dirty()).toBe(false);
      expect(page.fieldError(quantity)).toBeNull();
      page.setValue(note, ' memo ');
      expect(page.changes()).toEqual([{ key: 'SYS.NOTE', value: 'memo', version: 0 }]);
    });

    it('is read-only without EDIT permission', async () => {
      const page = await open(['SYS.PARAMETER.VIEW']);
      expect(el('param-read-only')).not.toBeNull();
      expect(el('param-save')).toBeNull();
      expect(el('param-restore-SYS.UNIT_PRICE_DECIMALS')).toBeNull();
      expect((el('param-input-SYS.QUANTITY_DECIMALS') as HTMLInputElement).disabled).toBe(true);
      page.setValue(page.parameters()[0], '3');
      page.save();
      controller.expectNone((r) => r.method === 'PUT');
    });

    it('reloads and discards drafts when the company changes, and shows load errors', async () => {
      const page = await open();
      page.setValue(page.parameters()[0], '3');
      TestBed.inject(CompanyContextService).select(2);
      await render();
      expectGet().flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
      await render();
      expect(page.dirty()).toBe(false);
      expect(page.parameters()).toEqual([]);
      expect(el('param-load-error')!.textContent).toContain('You do not have permission');
      expect(el('param-empty')).toBeNull();
    });

    it('hides the print group, which has its own page', async () => {
      await grant(['SYS.PARAMETER.VIEW']);
      fixture = TestBed.createComponent(ParameterSettings);
      await render();
      expectGet().flush([...list(), ...printList()]);
      await render();
      expect(fixture.componentInstance.groups().map((g) => g.id)).toEqual(['SYS-precision', 'SYS-other']);
      expect(el('print-preview')).toBeNull();
    });
  });

  // 介面 8.56（REQ_F4118.18.1–2，ADR-027）：同一元件以 view=print 顯示列印設定分組與抬頭預覽。
  describe('print settings view', () => {
    let fixture: ComponentFixture<ParameterSettings>;
    const el = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
    async function render(): Promise<void> { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
    async function openPrint(items = [...list(), ...printList()]): Promise<ParameterSettings> {
      await grant(['SYS.PARAMETER.VIEW', 'SYS.PARAMETER.EDIT']);
      fixture = TestBed.createComponent(ParameterSettings);
      fixture.componentRef.setInput('view', 'print');
      await render();
      expectGet().flush(items);
      await render();
      return fixture.componentInstance;
    }

    it('shows only print parameters under the print title, with long text as a textarea', async () => {
      const page = await openPrint();
      expect(el('sys-parameters')!.querySelector('h2')!.textContent).toContain('Print settings');
      expect(page.parameters().map((p) => p.key)).toEqual(printList().map((p) => p.key));
      expect(page.groups().map((g) => g.id)).toEqual(['SYS-print']);
      expect(el('param-row-SYS.PRINT_ADDRESS')!.textContent).toContain('Address');
      expect(el('param-input-SYS.PRINT_SALES_DELIVERY_FOOTER')!.tagName).toBe('TEXTAREA');
      expect(el('param-input-SYS.PRINT_PHONE')!.tagName).toBe('INPUT');
      expect(el('param-input-SYS.PRINT_SHOW_LOGO')!.tagName).toBe('NZ-SWITCH');
    });

    it('previews the header from drafts and falls back to the company name', async () => {
      const page = await openPrint();
      const preview = (): string => el('print-preview')!.textContent!.replace(/\s+/g, ' ');
      expect(el('print-preview-name')!.textContent).toContain('Taipei'); // 公司名稱空白時用目前公司名稱
      expect(preview()).toContain('1 Example Road');
      expect(preview()).toContain('Tel 02-1234-5678');
      expect(el('print-preview-fax')).toBeNull();
      const byKey = (key: string) => page.parameters().find((p) => p.key === key)!;
      page.setValue(byKey('SYS.PRINT_COMPANY_NAME'), 'Example Trading Co.');
      page.setValue(byKey('SYS.PRINT_FAX'), '02-1234-0000');
      page.setValue(byKey('SYS.PRINT_SHOW_STAMP'), 'true');
      await render();
      expect(el('print-preview-name')!.textContent).toContain('Example Trading Co.');
      expect(preview()).toContain('Fax 02-1234-0000');
      expect(el('print-preview-stamp')).not.toBeNull();
      expect(el('print-preview-logo')).toBeNull();
      page.setValue(byKey('SYS.PRINT_SALES_DELIVERY_FOOTER'), 'x'.repeat(501));
      await render();
      expect(el('param-error-SYS.PRINT_SALES_DELIVERY_FOOTER')!.textContent).toContain('Enter up to 500 characters.');
      expect(page.invalid()).toBe(true);
      page.setValue(byKey('SYS.PRINT_SALES_DELIVERY_FOOTER'), 'Thank you.');
      page.save();
      const put = expectPut();
      expect(put.request.body.items.map((i: { key: string }) => i.key)).toEqual(
        ['SYS.PRINT_COMPANY_NAME', 'SYS.PRINT_FAX', 'SYS.PRINT_SALES_DELIVERY_FOOTER', 'SYS.PRINT_SHOW_STAMP']);
      put.flush([...list(), ...printList()]);
      await render();
      expect(page.parameters().every((p) => p.group === 'print')).toBe(true);
    });

    it('shows the empty message when no print parameters are declared', async () => {
      await openPrint(list());
      expect(el('param-empty')).not.toBeNull();
      expect(el('print-preview')).toBeNull();
    });
  });
});

function printList(): ParameterDetail[] {
  const text = (name: string, maxLength: number, value = ''): ParameterDetail => param({
    key: `SYS.PRINT_${name}`, group: 'print', type: 'Text', value, defaultValue: '', isDefault: value === '', minimum: null, maximum: null, maxLength,
  });
  const flag = (name: string): ParameterDetail => param({
    key: `SYS.PRINT_${name}`, group: 'print', type: 'Boolean', value: 'false', defaultValue: 'false', minimum: null, maximum: null, maxLength: 5,
  });
  return [
    text('ADDRESS', 200, '1 Example Road'), text('COMPANY_NAME', 100), text('FAX', 30), text('PHONE', 30, '02-1234-5678'),
    text('POSTAL_CODE', 10), text('PURCHASE_ORDER_FOOTER', 500), text('SALES_DELIVERY_FOOTER', 500), flag('SHOW_LOGO'), flag('SHOW_STAMP'),
  ];
}
