import { HttpErrorResponse, HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { PurchaseMapPage } from './purchase-map-page';
import { purchaseAccountUnusable, purchaseMapAccountErrors, PurchaseMapService } from './purchase-map.service';

const API = '/api/v1/gl/purchase-invoice-account-map';
const account = (code: string, name: string | null, isActive = true, isPostable = true) => ({ code, name, isActive, isPostable });
const unconfigured = { configured: false, version: 0, payableAccount: null, purchaseAccount: null, inputTaxAccount: null,
  createdAt: null, createdBy: null, updatedAt: null, updatedBy: null };
const saved = (version = 2, over: object = {}) => ({ configured: true, version,
  payableAccount: account('2101', 'Accounts payable'), purchaseAccount: account('5111', 'Purchases'),
  inputTaxAccount: account('1151', 'Input VAT'), createdAt: '2026-11-10T09:15:00', createdBy: 1,
  updatedAt: '2026-11-13T18:05:44', updatedBy: 2, ...over });

describe('FI-PUR-MAP-UI purchase invoice account map page', () => {
  let granted: Set<string>;
  const company = signal<{ id: number } | null>({ id: 1 });
  beforeEach(() => {
    granted = new Set(['GL.PURCHASE_MAP.VIEW', 'GL.PURCHASE_MAP.EDIT']);
    company.set({ id: 1 });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isGet = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const isPut = (r: HttpRequest<unknown>) => r.url === API && r.method === 'PUT';

  function create(body: object = saved()) {
    const fixture = TestBed.createComponent(PurchaseMapPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isGet).flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.trim() ?? null;
    const type = (field: string, value: string) => {
      const input = by(`fi-purchase-map-input-${field}`) as HTMLInputElement;
      input.value = value; input.dispatchEvent(new Event('input')); input.dispatchEvent(new Event('blur'));
      fixture.detectChanges();
    };
    const submit = () => { by('fi-purchase-map-save')!.click(); fixture.detectChanges(); };
    return { view: fixture.componentInstance, http, element, by, text, type, submit, fixture };
  }

  it('FI-PUR-MAP-UI-001 shows the saved accounts with MD names, version and last change; the form holds the saved codes', () => {
    const { text, by } = create();
    expect(text('fi-purchase-map-code-payableAccountCode')).toBe('2101');
    expect(text('fi-purchase-map-name-purchaseAccountCode')).toBe('Purchases');
    expect(text('fi-purchase-map-code-inputTaxAccountCode')).toBe('1151');
    expect(text('fi-purchase-map-version')).toContain('Version 2');
    expect(text('fi-purchase-map-version')).toContain('2026-11-13 18:05');
    expect(by('fi-purchase-map-unusable')).toBeNull();
    expect(by('fi-purchase-map-unconfigured')).toBeNull();
    expect((by('fi-purchase-map-input-purchaseAccountCode') as HTMLInputElement).value).toBe('5111');
    expect(text('fi-purchase-map-form')).toContain('Change accounts');
    expect((by('fi-purchase-map-undo') as HTMLButtonElement).disabled).toBe(true);
    expect(by('fi-purchase-map-undo')!.getAttribute('title')).toBe('Nothing to undo');
  });

  it('FI-PUR-MAP-UI-002 an unconfigured company says invoices cannot be posted; editor sees an empty create form', () => {
    const { text, by } = create(unconfigured);
    expect(text('fi-purchase-map-unconfigured')).toContain('cannot be posted');
    expect(text('fi-purchase-map-unconfigured')).toContain('Enter the three accounts');
    expect(by('fi-purchase-map-saved')).toBeNull();
    expect(text('fi-purchase-map-form')).toContain('Set up accounts');
    expect((by('fi-purchase-map-input-payableAccountCode') as HTMLInputElement).value).toBe('');
  });

  it('FI-PUR-MAP-UI-003 a view-only role sees the map but no form, and an unconfigured map asks to contact finance', () => {
    granted = new Set(['GL.PURCHASE_MAP.VIEW']);
    const first = create();
    expect(first.by('fi-purchase-map-form')).toBeNull();
    expect(first.element.querySelectorAll('input')).toHaveLength(0);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ...provideTestTranslations('en'),
      provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
    const second = create(unconfigured);
    expect(second.text('fi-purchase-map-unconfigured')).toContain('Ask a finance user');
    expect(second.by('fi-purchase-map-form')).toBeNull();
  });

  it('FI-PUR-MAP-UI-004 saved accounts that MD no longer allows are flagged per field with a posting warning', () => {
    const { text, by } = create(saved(3, { payableAccount: account('2101', null, false, false),
      purchaseAccount: account('5111', 'Purchases', false, false), inputTaxAccount: account('1151', 'Input VAT', true, false) }));
    expect(text('fi-purchase-map-unusable')).toContain('will fail');
    expect(text('fi-purchase-map-missing-payableAccountCode')).toBe('Not found in master data');
    expect(by('fi-purchase-map-inactive-payableAccountCode')).toBeNull();
    expect(text('fi-purchase-map-inactive-purchaseAccountCode')).toBe('Inactive');
    expect(text('fi-purchase-map-not-postable-inputTaxAccountCode')).toContain('child accounts');
  });

  it('FI-PUR-MAP-UI-005 create sends version 0 with trimmed codes and shows a green notice only for a real change', () => {
    const { http, type, submit, text, by, fixture } = create(unconfigured);
    type('payableAccountCode', ' 1131 '); type('purchaseAccountCode', '5111'); type('inputTaxAccountCode', '1151');
    expect((by('fi-purchase-map-undo') as HTMLButtonElement).disabled).toBe(false);
    submit();
    const put = http.expectOne(isPut);
    expect(put.request.body).toEqual({ version: 0, payableAccountCode: '1131', purchaseAccountCode: '5111', inputTaxAccountCode: '1151' });
    put.flush({ map: saved(1), changed: true });
    fixture.detectChanges();
    expect(by('fi-purchase-map-notice')!.getAttribute('data-type')).toBe('success');
    expect(text('fi-purchase-map-notice')).toContain('version 1');
    expect(text('fi-purchase-map-version')).toContain('Version 1');
    expect((by('fi-purchase-map-undo') as HTMLButtonElement).disabled).toBe(true);
  });

  it('FI-PUR-MAP-UI-006 update sends the saved version; changed=false is a warning that keeps the version', () => {
    const { http, type, submit, text, by, fixture } = create();
    type('purchaseAccountCode', '5112'); submit();
    const changed = http.expectOne(isPut);
    expect(changed.request.body).toMatchObject({ version: 2, purchaseAccountCode: '5112' });
    changed.flush({ map: saved(3, { purchaseAccount: account('5112', 'Freight in') }), changed: true });
    fixture.detectChanges();
    expect(text('fi-purchase-map-notice')).toContain('Accounts changed (version 3)');
    submit();
    const same = http.expectOne(isPut);
    expect(same.request.body).toMatchObject({ version: 3 });
    same.flush({ map: saved(3, { purchaseAccount: account('5112', 'Freight in') }), changed: false });
    fixture.detectChanges();
    expect(by('fi-purchase-map-notice')!.getAttribute('data-type')).toBe('warning');
    expect(text('fi-purchase-map-notice')).toContain('Nothing changed');
    expect(text('fi-purchase-map-name-purchaseAccountCode')).toBe('Freight in');
  });

  it('FI-PUR-MAP-UI-007 blank and over-long codes are refused on screen without a request', () => {
    const { http, type, submit, text } = create(unconfigured);
    type('payableAccountCode', '   '); type('purchaseAccountCode', 'X'.repeat(31));
    submit();
    http.expectNone(isPut);
    expect(text('fi-purchase-map-field-error-payableAccountCode')).toBe('Enter an account code.');
    expect(text('fi-purchase-map-field-error-purchaseAccountCode')).toBe('An account code has at most 30 characters.');
    expect(text('fi-purchase-map-field-error-inputTaxAccountCode')).toBe('Enter an account code.');
  });

  it('FI-PUR-MAP-UI-008 a 422 names the reason under each refused field and clears it when the field is edited', () => {
    const { http, type, submit, text, by } = create();
    type('payableAccountCode', '9999'); type('purchaseAccountCode', '4100'); type('inputTaxAccountCode', '2199');
    submit();
    http.expectOne(isPut).flush({ status: 422, errorCode: 'FI.PURCHASE_MAP.ACCOUNT_INVALID', accountErrors: {
      payableAccountCode: 'MD.ACCOUNT.NOT_FOUND', purchaseAccountCode: 'MD.ACCOUNT.NOT_POSTABLE', inputTaxAccountCode: 'MD.ACCOUNT.INACTIVE' } },
      { status: 422, statusText: 'Unprocessable' });
    TestBed.tick();
    expect(text('fi-purchase-map-save-error')).toContain('Nothing was saved');
    expect(text('fi-purchase-map-field-error-payableAccountCode')).toContain('No account with this code');
    expect(text('fi-purchase-map-field-error-purchaseAccountCode')).toContain('lowest-level');
    expect(text('fi-purchase-map-field-error-inputTaxAccountCode')).toContain('inactive');
    // The saved map is untouched on screen.
    expect(text('fi-purchase-map-code-payableAccountCode')).toBe('2101');
    type('payableAccountCode', '2101');
    expect(by('fi-purchase-map-field-error-payableAccountCode')).toBeNull();
    expect(by('fi-purchase-map-field-error-purchaseAccountCode')).not.toBeNull();
  });

  it('FI-PUR-MAP-UI-009 a 409 re-reads the latest map, replaces the input and keeps the conflict message', () => {
    const { http, type, submit, text, by, fixture } = create();
    type('purchaseAccountCode', '5112'); submit();
    http.expectOne(isPut).flush({ status: 409, errorCode: 'FI.PURCHASE_MAP.VERSION_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(isGet).flush(saved(5, { inputTaxAccount: account('1152', 'Output tax 2') }));
    fixture.detectChanges();
    expect(text('fi-purchase-map-save-error')).toContain('Another user changed these accounts first');
    expect(text('fi-purchase-map-version')).toContain('Version 5');
    expect((by('fi-purchase-map-input-purchaseAccountCode') as HTMLInputElement).value).toBe('5111');
    expect((by('fi-purchase-map-input-inputTaxAccountCode') as HTMLInputElement).value).toBe('1152');
    submit();
    expect(http.expectOne(isPut).request.body).toMatchObject({ version: 5 });
  });

  it('FI-PUR-MAP-UI-010 400 lists field details, 500 shows the trace ID, 403 is named; nothing changes on screen', () => {
    const { http, type, submit, text, by, fixture } = create();
    type('purchaseAccountCode', '5112');
    submit();
    http.expectOne(isPut).flush({ status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { version: ['Version must be 0 (not configured) or the saved version.'] } }, { status: 400, statusText: 'Bad' });
    fixture.detectChanges();
    expect(text('fi-purchase-map-save-error')).toContain('version: Version must be 0');
    submit();
    http.expectOne(isPut).flush({ status: 500, traceId: 'trace-77' }, { status: 500, statusText: 'Server' });
    fixture.detectChanges();
    expect(text('fi-purchase-map-save-error')).toContain('trace-77');
    submit();
    http.expectOne(isPut).flush({ status: 403, errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(by('fi-purchase-map-save-error')).not.toBeNull();
    expect(text('fi-purchase-map-save-error')).not.toContain('COMMON.');
    expect(text('fi-purchase-map-code-purchaseAccountCode')).toBe('5111');
    expect((by('fi-purchase-map-input-purchaseAccountCode') as HTMLInputElement).value).toBe('5112');
  });

  it('FI-PUR-MAP-UI-011 a failed read shows the error with retry; network and 500 are distinguished', () => {
    const fixture = TestBed.createComponent(PurchaseMapPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isGet).flush({ status: 500, traceId: 'trace-9' }, { status: 500, statusText: 'Server' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="fi-purchase-map-error"]')!.textContent).toContain('trace-9');
    expect(element.querySelector('[data-testid="fi-purchase-map-form"]')).toBeNull();
    (element.querySelector('[data-testid="fi-purchase-map-retry"]') as HTMLButtonElement).click();
    http.expectOne(isGet).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-purchase-map-error"]')!.textContent).not.toContain('trace-9');
    (element.querySelector('[data-testid="fi-purchase-map-retry"]') as HTMLButtonElement).click();
    http.expectOne(isGet).flush(saved());
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-purchase-map-error"]')).toBeNull();
  });

  it('FI-PUR-MAP-UI-012 a company switch drops input and feedback and reads the new company map; a late save result is ignored', () => {
    const { http, type, submit, text, by, fixture, view } = create();
    type('purchaseAccountCode', '5112');
    expect(view.hasUnsavedChanges()).toBe(true);
    submit();
    const pending = http.expectOne(isPut);
    company.set({ id: 2 });
    TestBed.tick();
    http.expectOne(isGet).flush(unconfigured);
    pending.flush({ map: saved(3), changed: true });
    fixture.detectChanges();
    expect(by('fi-purchase-map-notice')).toBeNull();
    expect(text('fi-purchase-map-unconfigured')).toContain('cannot be posted');
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-PUR-MAP-UI-013 undo restores the saved codes and clears field errors', () => {
    const { type, by, view } = create();
    type('payableAccountCode', '');
    expect(by('fi-purchase-map-field-error-payableAccountCode')).not.toBeNull();
    by('fi-purchase-map-undo')!.click();
    TestBed.tick();
    expect(view.form.getRawValue().payableAccountCode).toBe('2101');
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-PUR-MAP-UI-014 helpers: account errors keep only known fields, unknown reasons become other; unusable detection', () => {
    const error = (body: object, status = 422) => new HttpErrorResponse({ status, error: body });
    expect(purchaseMapAccountErrors(error({ errorCode: 'FI.PURCHASE_MAP.ACCOUNT_INVALID',
      accountErrors: { payableAccountCode: 'MD.ACCOUNT.INACTIVE', purchaseAccountCode: 'X', other: 'MD.ACCOUNT.NOT_FOUND' } })))
      .toEqual({ payableAccountCode: 'MD.ACCOUNT.INACTIVE', purchaseAccountCode: 'other' });
    expect(purchaseMapAccountErrors(error({ errorCode: 'OTHER', accountErrors: { payableAccountCode: 'MD.ACCOUNT.INACTIVE' } }))).toEqual({});
    expect(purchaseMapAccountErrors(error({ errorCode: 'FI.PURCHASE_MAP.ACCOUNT_INVALID' }, 400))).toEqual({});
    expect(purchaseMapAccountErrors(new Error('x'))).toEqual({});
    expect(purchaseAccountUnusable(null)).toBe(false);
    expect(purchaseAccountUnusable(account('1', 'A'))).toBe(false);
    expect(purchaseAccountUnusable(account('1', null))).toBe(true);
    expect(purchaseAccountUnusable(account('1', 'A', true, false))).toBe(true);
    expect(TestBed.inject(PurchaseMapService).errorKey(error({ errorCode: 'FI.PURCHASE_MAP.VERSION_CONFLICT' }, 409)))
      .toBe('errors.FI.PURCHASE_MAP.VERSION_CONFLICT');
  });

  it('FI-PUR-MAP-UI-015 purchase wording: title, the three account labels and posting sides name purchase invoices, not sales', () => {
    const { element, by } = create();
    const page = element.textContent ?? '';
    expect(element.querySelector('h1')!.textContent).toBe('Purchase invoice accounts');
    expect(by('fi-purchase-map-saved-payableAccountCode')!.textContent).toContain('Payable account');
    expect(by('fi-purchase-map-saved-purchaseAccountCode')!.textContent).toContain('Purchases or expense account');
    expect(by('fi-purchase-map-saved-inputTaxAccountCode')!.textContent).toContain('Input tax account');
    expect(page).toContain('purchase invoice');
    expect(page).not.toMatch(/sales|receivable|revenue|output tax/i);
  });
});
