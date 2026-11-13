import { HttpErrorResponse, HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { SalesMapPage } from './sales-map-page';
import { accountUnusable, salesMapAccountErrors, SalesMapService } from './sales-map.service';
import { SALES_POSTING_ERROR_CODES, salesPostingErrorKey } from './sales-posting-errors';

const API = '/api/v1/gl/sales-invoice-account-map';
const account = (code: string, name: string | null, isActive = true, isPostable = true) => ({ code, name, isActive, isPostable });
const unconfigured = { configured: false, version: 0, receivableAccount: null, revenueAccount: null, outputTaxAccount: null,
  createdAt: null, createdBy: null, updatedAt: null, updatedBy: null };
const saved = (version = 2, over: object = {}) => ({ configured: true, version,
  receivableAccount: account('1131', 'Accounts receivable'), revenueAccount: account('4111', 'Sales revenue'),
  outputTaxAccount: account('2141', 'Output VAT'), createdAt: '2026-11-10T09:15:00', createdBy: 1,
  updatedAt: '2026-11-13T18:05:44', updatedBy: 2, ...over });

describe('FI-SALES-MAP-UI sales invoice account map page', () => {
  let granted: Set<string>;
  const company = signal<{ id: number } | null>({ id: 1 });
  beforeEach(() => {
    granted = new Set(['GL.SALES_MAP.VIEW', 'GL.SALES_MAP.EDIT']);
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
    const fixture = TestBed.createComponent(SalesMapPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isGet).flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.trim() ?? null;
    const type = (field: string, value: string) => {
      const input = by(`fi-sales-map-input-${field}`) as HTMLInputElement;
      input.value = value; input.dispatchEvent(new Event('input')); input.dispatchEvent(new Event('blur'));
      fixture.detectChanges();
    };
    const submit = () => { by('fi-sales-map-save')!.click(); fixture.detectChanges(); };
    return { view: fixture.componentInstance, http, element, by, text, type, submit, fixture };
  }

  it('FI-SALES-MAP-UI-001 shows the saved accounts with MD names, version and last change; the form holds the saved codes', () => {
    const { text, by } = create();
    expect(text('fi-sales-map-code-receivableAccountCode')).toBe('1131');
    expect(text('fi-sales-map-name-revenueAccountCode')).toBe('Sales revenue');
    expect(text('fi-sales-map-code-outputTaxAccountCode')).toBe('2141');
    expect(text('fi-sales-map-version')).toContain('Version 2');
    expect(text('fi-sales-map-version')).toContain('2026-11-13 18:05');
    expect(by('fi-sales-map-unusable')).toBeNull();
    expect(by('fi-sales-map-unconfigured')).toBeNull();
    expect((by('fi-sales-map-input-revenueAccountCode') as HTMLInputElement).value).toBe('4111');
    expect(text('fi-sales-map-form')).toContain('Change accounts');
    expect((by('fi-sales-map-undo') as HTMLButtonElement).disabled).toBe(true);
    expect(by('fi-sales-map-undo')!.getAttribute('title')).toBe('Nothing to undo');
  });

  it('FI-SALES-MAP-UI-002 an unconfigured company says invoices cannot be posted; editor sees an empty create form', () => {
    const { text, by } = create(unconfigured);
    expect(text('fi-sales-map-unconfigured')).toContain('cannot be posted');
    expect(text('fi-sales-map-unconfigured')).toContain('Enter the three accounts');
    expect(by('fi-sales-map-saved')).toBeNull();
    expect(text('fi-sales-map-form')).toContain('Set up accounts');
    expect((by('fi-sales-map-input-receivableAccountCode') as HTMLInputElement).value).toBe('');
  });

  it('FI-SALES-MAP-UI-003 a view-only role sees the map but no form, and an unconfigured map asks to contact finance', () => {
    granted = new Set(['GL.SALES_MAP.VIEW']);
    const first = create();
    expect(first.by('fi-sales-map-form')).toBeNull();
    expect(first.element.querySelectorAll('input')).toHaveLength(0);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ...provideTestTranslations('en'),
      provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
    const second = create(unconfigured);
    expect(second.text('fi-sales-map-unconfigured')).toContain('Ask a finance user');
    expect(second.by('fi-sales-map-form')).toBeNull();
  });

  it('FI-SALES-MAP-UI-004 saved accounts that MD no longer allows are flagged per field with a posting warning', () => {
    const { text, by } = create(saved(3, { receivableAccount: account('1131', null, false, false),
      revenueAccount: account('4111', 'Sales revenue', false, false), outputTaxAccount: account('2141', 'Output VAT', true, false) }));
    expect(text('fi-sales-map-unusable')).toContain('will fail');
    expect(text('fi-sales-map-missing-receivableAccountCode')).toBe('Not found in master data');
    expect(by('fi-sales-map-inactive-receivableAccountCode')).toBeNull();
    expect(text('fi-sales-map-inactive-revenueAccountCode')).toBe('Inactive');
    expect(text('fi-sales-map-not-postable-outputTaxAccountCode')).toContain('child accounts');
  });

  it('FI-SALES-MAP-UI-005 create sends version 0 with trimmed codes and shows a green notice only for a real change', () => {
    const { http, type, submit, text, by, fixture } = create(unconfigured);
    type('receivableAccountCode', ' 1131 '); type('revenueAccountCode', '4111'); type('outputTaxAccountCode', '2141');
    expect((by('fi-sales-map-undo') as HTMLButtonElement).disabled).toBe(false);
    submit();
    const put = http.expectOne(isPut);
    expect(put.request.body).toEqual({ version: 0, receivableAccountCode: '1131', revenueAccountCode: '4111', outputTaxAccountCode: '2141' });
    put.flush({ map: saved(1), changed: true });
    fixture.detectChanges();
    expect(by('fi-sales-map-notice')!.getAttribute('data-type')).toBe('success');
    expect(text('fi-sales-map-notice')).toContain('version 1');
    expect(text('fi-sales-map-version')).toContain('Version 1');
    expect((by('fi-sales-map-undo') as HTMLButtonElement).disabled).toBe(true);
  });

  it('FI-SALES-MAP-UI-006 update sends the saved version; changed=false is a warning that keeps the version', () => {
    const { http, type, submit, text, by, fixture } = create();
    type('revenueAccountCode', '4112'); submit();
    const changed = http.expectOne(isPut);
    expect(changed.request.body).toMatchObject({ version: 2, revenueAccountCode: '4112' });
    changed.flush({ map: saved(3, { revenueAccount: account('4112', 'Service revenue') }), changed: true });
    fixture.detectChanges();
    expect(text('fi-sales-map-notice')).toContain('Accounts changed (version 3)');
    submit();
    const same = http.expectOne(isPut);
    expect(same.request.body).toMatchObject({ version: 3 });
    same.flush({ map: saved(3, { revenueAccount: account('4112', 'Service revenue') }), changed: false });
    fixture.detectChanges();
    expect(by('fi-sales-map-notice')!.getAttribute('data-type')).toBe('warning');
    expect(text('fi-sales-map-notice')).toContain('Nothing changed');
    expect(text('fi-sales-map-name-revenueAccountCode')).toBe('Service revenue');
  });

  it('FI-SALES-MAP-UI-007 blank and over-long codes are refused on screen without a request', () => {
    const { http, type, submit, text } = create(unconfigured);
    type('receivableAccountCode', '   '); type('revenueAccountCode', 'X'.repeat(31));
    submit();
    http.expectNone(isPut);
    expect(text('fi-sales-map-field-error-receivableAccountCode')).toBe('Enter an account code.');
    expect(text('fi-sales-map-field-error-revenueAccountCode')).toBe('An account code has at most 30 characters.');
    expect(text('fi-sales-map-field-error-outputTaxAccountCode')).toBe('Enter an account code.');
  });

  it('FI-SALES-MAP-UI-008 a 422 names the reason under each refused field and clears it when the field is edited', () => {
    const { http, type, submit, text, by } = create();
    type('receivableAccountCode', '9999'); type('revenueAccountCode', '4100'); type('outputTaxAccountCode', '2199');
    submit();
    http.expectOne(isPut).flush({ status: 422, errorCode: 'FI.SALES_MAP.ACCOUNT_INVALID', accountErrors: {
      receivableAccountCode: 'MD.ACCOUNT.NOT_FOUND', revenueAccountCode: 'MD.ACCOUNT.NOT_POSTABLE', outputTaxAccountCode: 'MD.ACCOUNT.INACTIVE' } },
      { status: 422, statusText: 'Unprocessable' });
    TestBed.tick();
    expect(text('fi-sales-map-save-error')).toContain('Nothing was saved');
    expect(text('fi-sales-map-field-error-receivableAccountCode')).toContain('No account with this code');
    expect(text('fi-sales-map-field-error-revenueAccountCode')).toContain('lowest-level');
    expect(text('fi-sales-map-field-error-outputTaxAccountCode')).toContain('inactive');
    // The saved map is untouched on screen.
    expect(text('fi-sales-map-code-receivableAccountCode')).toBe('1131');
    type('receivableAccountCode', '1131');
    expect(by('fi-sales-map-field-error-receivableAccountCode')).toBeNull();
    expect(by('fi-sales-map-field-error-revenueAccountCode')).not.toBeNull();
  });

  it('FI-SALES-MAP-UI-009 a 409 re-reads the latest map, replaces the input and keeps the conflict message', () => {
    const { http, type, submit, text, by, fixture } = create();
    type('revenueAccountCode', '4112'); submit();
    http.expectOne(isPut).flush({ status: 409, errorCode: 'FI.SALES_MAP.VERSION_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(isGet).flush(saved(5, { outputTaxAccount: account('2142', 'Output tax 2') }));
    fixture.detectChanges();
    expect(text('fi-sales-map-save-error')).toContain('Another user changed these accounts first');
    expect(text('fi-sales-map-version')).toContain('Version 5');
    expect((by('fi-sales-map-input-revenueAccountCode') as HTMLInputElement).value).toBe('4111');
    expect((by('fi-sales-map-input-outputTaxAccountCode') as HTMLInputElement).value).toBe('2142');
    submit();
    expect(http.expectOne(isPut).request.body).toMatchObject({ version: 5 });
  });

  it('FI-SALES-MAP-UI-010 400 lists field details, 500 shows the trace ID, 403 is named; nothing changes on screen', () => {
    const { http, type, submit, text, by, fixture } = create();
    type('revenueAccountCode', '4112');
    submit();
    http.expectOne(isPut).flush({ status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { version: ['Version must be 0 (not configured) or the saved version.'] } }, { status: 400, statusText: 'Bad' });
    fixture.detectChanges();
    expect(text('fi-sales-map-save-error')).toContain('version: Version must be 0');
    submit();
    http.expectOne(isPut).flush({ status: 500, traceId: 'trace-77' }, { status: 500, statusText: 'Server' });
    fixture.detectChanges();
    expect(text('fi-sales-map-save-error')).toContain('trace-77');
    submit();
    http.expectOne(isPut).flush({ status: 403, errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(by('fi-sales-map-save-error')).not.toBeNull();
    expect(text('fi-sales-map-save-error')).not.toContain('COMMON.');
    expect(text('fi-sales-map-code-revenueAccountCode')).toBe('4111');
    expect((by('fi-sales-map-input-revenueAccountCode') as HTMLInputElement).value).toBe('4112');
  });

  it('FI-SALES-MAP-UI-011 a failed read shows the error with retry; network and 500 are distinguished', () => {
    const fixture = TestBed.createComponent(SalesMapPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isGet).flush({ status: 500, traceId: 'trace-9' }, { status: 500, statusText: 'Server' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="fi-sales-map-error"]')!.textContent).toContain('trace-9');
    expect(element.querySelector('[data-testid="fi-sales-map-form"]')).toBeNull();
    (element.querySelector('[data-testid="fi-sales-map-retry"]') as HTMLButtonElement).click();
    http.expectOne(isGet).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-sales-map-error"]')!.textContent).not.toContain('trace-9');
    (element.querySelector('[data-testid="fi-sales-map-retry"]') as HTMLButtonElement).click();
    http.expectOne(isGet).flush(saved());
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-sales-map-error"]')).toBeNull();
  });

  it('FI-SALES-MAP-UI-012 a company switch drops input and feedback and reads the new company map; a late save result is ignored', () => {
    const { http, type, submit, text, by, fixture, view } = create();
    type('revenueAccountCode', '4112');
    expect(view.hasUnsavedChanges()).toBe(true);
    submit();
    const pending = http.expectOne(isPut);
    company.set({ id: 2 });
    TestBed.tick();
    http.expectOne(isGet).flush(unconfigured);
    pending.flush({ map: saved(3), changed: true });
    fixture.detectChanges();
    expect(by('fi-sales-map-notice')).toBeNull();
    expect(text('fi-sales-map-unconfigured')).toContain('cannot be posted');
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-SALES-MAP-UI-013 undo restores the saved codes and clears field errors', () => {
    const { type, by, view } = create();
    type('receivableAccountCode', '');
    expect(by('fi-sales-map-field-error-receivableAccountCode')).not.toBeNull();
    by('fi-sales-map-undo')!.click();
    TestBed.tick();
    expect(view.form.getRawValue().receivableAccountCode).toBe('1131');
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-SALES-MAP-UI-014 helpers: account errors keep only known fields, unknown reasons become other; unusable detection', () => {
    const error = (body: object, status = 422) => new HttpErrorResponse({ status, error: body });
    expect(salesMapAccountErrors(error({ errorCode: 'FI.SALES_MAP.ACCOUNT_INVALID',
      accountErrors: { receivableAccountCode: 'MD.ACCOUNT.INACTIVE', revenueAccountCode: 'X', other: 'MD.ACCOUNT.NOT_FOUND' } })))
      .toEqual({ receivableAccountCode: 'MD.ACCOUNT.INACTIVE', revenueAccountCode: 'other' });
    expect(salesMapAccountErrors(error({ errorCode: 'OTHER', accountErrors: { receivableAccountCode: 'MD.ACCOUNT.INACTIVE' } }))).toEqual({});
    expect(salesMapAccountErrors(error({ errorCode: 'FI.SALES_MAP.ACCOUNT_INVALID' }, 400))).toEqual({});
    expect(salesMapAccountErrors(new Error('x'))).toEqual({});
    expect(accountUnusable(null)).toBe(false);
    expect(accountUnusable(account('1', 'A'))).toBe(false);
    expect(accountUnusable(account('1', null))).toBe(true);
    expect(accountUnusable(account('1', 'A', true, false))).toBe(true);
    expect(TestBed.inject(SalesMapService).errorKey(error({ errorCode: 'FI.SALES_MAP.VERSION_CONFLICT' }, 409)))
      .toBe('errors.FI.SALES_MAP.VERSION_CONFLICT');
  });

  it('FI-SALES-MAP-UI-015 sales posting codes map to translated keys; unknown codes stay generic', () => {
    for (const code of SALES_POSTING_ERROR_CODES) {
      expect(salesPostingErrorKey(new HttpErrorResponse({ status: 422, error: { errorCode: code } }))).toBe(`errors.${code}`);
    }
    expect(salesPostingErrorKey(new HttpErrorResponse({ status: 422, error: { errorCode: 'FI.SALES_POSTING.NOPE' } }))).toBe('errors.http.unknown');
  });
});
