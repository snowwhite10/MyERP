import { HttpErrorResponse, HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { CollectionMapPage } from './collection-map-page';
import { collectionAccountUnusable, collectionMapAccountErrors, CollectionMapService, receivableDiffersFromSalesMap } from './collection-map.service';

const API = '/api/v1/gl/sales-collection-account-map';
const DIFFERS = 'FI.COLLECTION_MAP.RECEIVABLE_DIFFERS_FROM_SALES_MAP';
const account = (code: string, name: string | null, isActive = true, isPostable = true) => ({ code, name, isActive, isPostable });
const unconfigured = { configured: false, version: 0, unappliedCashAccount: null, receivableAccount: null,
  salesInvoiceReceivableAccountCode: '1131', warnings: [], createdAt: null, createdBy: null, updatedAt: null, updatedBy: null };
const saved = (version = 2, over: object = {}) => ({ configured: true, version,
  unappliedCashAccount: account('2181', 'Unapplied cash'), receivableAccount: account('1131', 'Accounts receivable'),
  salesInvoiceReceivableAccountCode: '1131', warnings: [], createdAt: '2026-11-10T09:15:00', createdBy: 1,
  updatedAt: '2026-11-13T18:05:44', updatedBy: 2, ...over });

describe('FI-COLL-MAP-UI sales collection account map page', () => {
  let granted: Set<string>;
  const company = signal<{ id: number } | null>({ id: 1 });
  const configure = () => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
    { provide: CompanyContextService, useValue: { current: company } },
    { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
      hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  beforeEach(() => {
    granted = new Set(['GL.COLLECTION_MAP.VIEW', 'GL.COLLECTION_MAP.EDIT']);
    company.set({ id: 1 });
    configure();
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isGet = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const isPut = (r: HttpRequest<unknown>) => r.url === API && r.method === 'PUT';

  function create(body: object = saved()) {
    const fixture = TestBed.createComponent(CollectionMapPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isGet).flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.trim() ?? null;
    const input = (field: string) => by(`fi-collection-map-input-${field}`) as HTMLInputElement;
    const type = (field: string, value: string) => {
      const target = input(field);
      target.value = value; target.dispatchEvent(new Event('input')); target.dispatchEvent(new Event('blur'));
      fixture.detectChanges();
    };
    const submit = () => { by('fi-collection-map-save')!.click(); fixture.detectChanges(); };
    return { view: fixture.componentInstance, http, element, by, text, input, type, submit, fixture };
  }

  it('FI-COLL-MAP-UI-001 shows the saved accounts with MD names, version and last change; the form holds the saved codes', () => {
    const { text, by, input } = create();
    expect(text('fi-collection-map-code-unappliedCashAccountCode')).toBe('2181');
    expect(text('fi-collection-map-name-unappliedCashAccountCode')).toBe('Unapplied cash');
    expect(text('fi-collection-map-code-receivableAccountCode')).toBe('1131');
    expect(text('fi-collection-map-version')).toContain('Version 2');
    expect(text('fi-collection-map-version')).toContain('2026-11-13 18:05');
    expect(by('fi-collection-map-unusable')).toBeNull();
    expect(by('fi-collection-map-unconfigured')).toBeNull();
    expect(by('fi-collection-map-receivable-differs')).toBeNull();
    expect(input('receivableAccountCode').value).toBe('1131');
    expect(text('fi-collection-map-form')).toContain('Change accounts');
    expect((by('fi-collection-map-undo') as HTMLButtonElement).disabled).toBe(true);
    expect(by('fi-collection-map-undo')!.getAttribute('title')).toBe('Nothing to undo');
  });

  it('FI-COLL-MAP-UI-002 an unconfigured company says receipts cannot be posted; editor sees an empty create form', () => {
    const { text, by, input } = create(unconfigured);
    expect(text('fi-collection-map-unconfigured')).toContain('cannot be posted');
    expect(text('fi-collection-map-unconfigured')).toContain('Enter the two accounts');
    expect(by('fi-collection-map-saved')).toBeNull();
    expect(by('fi-collection-map-receivable-differs')).toBeNull();
    expect(text('fi-collection-map-form')).toContain('Set up accounts');
    expect(input('unappliedCashAccountCode').value).toBe('');
  });

  it('FI-COLL-MAP-UI-003 a view-only role sees the map but no form, and an unconfigured map asks to contact finance', () => {
    granted = new Set(['GL.COLLECTION_MAP.VIEW']);
    const first = create();
    expect(first.by('fi-collection-map-form')).toBeNull();
    expect(first.element.querySelectorAll('input')).toHaveLength(0);
    TestBed.resetTestingModule();
    configure();
    const second = create(unconfigured);
    expect(second.text('fi-collection-map-unconfigured')).toContain('Ask a finance user');
    expect(second.by('fi-collection-map-form')).toBeNull();
  });

  it('FI-COLL-MAP-UI-004 saved accounts that MD no longer allows are flagged per field with a posting warning', () => {
    const { text, by } = create(saved(3, { unappliedCashAccount: account('2181', null, false, false),
      receivableAccount: account('1131', 'Accounts receivable', false, false) }));
    expect(text('fi-collection-map-unusable')).toContain('will fail');
    expect(text('fi-collection-map-missing-unappliedCashAccountCode')).toBe('Not found in master data');
    expect(by('fi-collection-map-inactive-unappliedCashAccountCode')).toBeNull();
    expect(text('fi-collection-map-inactive-receivableAccountCode')).toBe('Inactive');
    const notPostable = create(saved(3, { receivableAccount: account('1130', 'Receivables', true, false) }));
    expect(notPostable.text('fi-collection-map-not-postable-receivableAccountCode')).toContain('child accounts');
  });

  it('FI-COLL-MAP-UI-005 create sends version 0 with trimmed codes and shows a green notice only for a real change', () => {
    const { http, type, submit, text, by, fixture } = create(unconfigured);
    type('unappliedCashAccountCode', ' 2181 '); type('receivableAccountCode', '1131');
    expect((by('fi-collection-map-undo') as HTMLButtonElement).disabled).toBe(false);
    submit();
    const put = http.expectOne(isPut);
    expect(put.request.body).toEqual({ version: 0, unappliedCashAccountCode: '2181', receivableAccountCode: '1131' });
    put.flush({ map: saved(1), changed: true });
    fixture.detectChanges();
    expect(by('fi-collection-map-notice')!.getAttribute('data-type')).toBe('success');
    expect(text('fi-collection-map-notice')).toContain('version 1');
    expect(text('fi-collection-map-notice')).toContain('Collection receipts can now be posted');
    expect(text('fi-collection-map-version')).toContain('Version 1');
    expect((by('fi-collection-map-undo') as HTMLButtonElement).disabled).toBe(true);
  });

  it('FI-COLL-MAP-UI-006 update sends the saved version; changed=false is a warning that keeps the version', () => {
    const { http, type, submit, text, by, fixture } = create();
    type('unappliedCashAccountCode', '2182'); submit();
    const changed = http.expectOne(isPut);
    expect(changed.request.body).toMatchObject({ version: 2, unappliedCashAccountCode: '2182' });
    changed.flush({ map: saved(3, { unappliedCashAccount: account('2182', 'Customer deposits') }), changed: true });
    fixture.detectChanges();
    expect(text('fi-collection-map-notice')).toContain('Accounts changed (version 3)');
    submit();
    const same = http.expectOne(isPut);
    expect(same.request.body).toMatchObject({ version: 3 });
    same.flush({ map: saved(3, { unappliedCashAccount: account('2182', 'Customer deposits') }), changed: false });
    fixture.detectChanges();
    expect(by('fi-collection-map-notice')!.getAttribute('data-type')).toBe('warning');
    expect(text('fi-collection-map-notice')).toContain('Nothing changed');
    expect(text('fi-collection-map-name-unappliedCashAccountCode')).toBe('Customer deposits');
  });

  it('FI-COLL-MAP-UI-007 blank, over-long and identical codes are refused on screen without a request', () => {
    const { http, type, submit, text, by, element } = create(unconfigured);
    type('unappliedCashAccountCode', '   '); type('receivableAccountCode', 'X'.repeat(31));
    submit();
    http.expectNone(isPut);
    expect(text('fi-collection-map-field-error-unappliedCashAccountCode')).toBe('Enter an account code.');
    expect(text('fi-collection-map-field-error-receivableAccountCode')).toBe('An account code has at most 30 characters.');
    // Same code after trimming and ignoring case (BR-FI-154) is named on the AR field.
    type('unappliedCashAccountCode', '2181a'); type('receivableAccountCode', ' 2181A ');
    submit();
    http.expectNone(isPut);
    expect(by('fi-collection-map-field-error-unappliedCashAccountCode')).toBeNull();
    expect(text('fi-collection-map-field-error-receivableAccountCode')).toBe('The receivable control account must differ from the unapplied cash account.');
    expect(element.querySelector('#fi-collection-map-input-receivableAccountCode')!.getAttribute('aria-invalid')).toBe('true');
  });

  it('FI-COLL-MAP-UI-008 a 422 names the reason under each refused field (incl. same account) and clears it when the field is edited', () => {
    const { http, type, submit, text, by } = create();
    type('unappliedCashAccountCode', '9999'); type('receivableAccountCode', '1130');
    submit();
    http.expectOne(isPut).flush({ status: 422, errorCode: 'FI.COLLECTION_MAP.ACCOUNT_INVALID', accountErrors: {
      unappliedCashAccountCode: 'MD.ACCOUNT.NOT_FOUND', receivableAccountCode: 'MD.ACCOUNT.NOT_POSTABLE' } },
      { status: 422, statusText: 'Unprocessable' });
    TestBed.tick();
    expect(text('fi-collection-map-save-error')).toContain('Nothing was saved');
    expect(text('fi-collection-map-field-error-unappliedCashAccountCode')).toContain('No account with this code');
    expect(text('fi-collection-map-field-error-receivableAccountCode')).toContain('lowest-level');
    expect(text('fi-collection-map-code-unappliedCashAccountCode')).toBe('2181');
    type('unappliedCashAccountCode', '2181');
    expect(by('fi-collection-map-field-error-unappliedCashAccountCode')).toBeNull();
    expect(by('fi-collection-map-field-error-receivableAccountCode')).not.toBeNull();
    // The server's same-account reason (e.g. codes equal ignoring case the client did not catch) uses the shared error text.
    type('receivableAccountCode', '1132'); submit();
    http.expectOne(isPut).flush({ status: 422, errorCode: 'FI.COLLECTION_MAP.ACCOUNT_INVALID', accountErrors: {
      receivableAccountCode: 'FI.COLLECTION_MAP.SAME_ACCOUNT' } }, { status: 422, statusText: 'Unprocessable' });
    TestBed.tick();
    expect(text('fi-collection-map-field-error-receivableAccountCode')).toBe('The receivable control account must differ from the unapplied cash account.');
  });

  it('FI-COLL-MAP-UI-009 a 409 re-reads the latest map, replaces the input and keeps the conflict message', () => {
    const { http, type, submit, text, input, fixture } = create();
    type('unappliedCashAccountCode', '2182'); submit();
    http.expectOne(isPut).flush({ status: 409, errorCode: 'FI.COLLECTION_MAP.VERSION_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(isGet).flush(saved(5, { receivableAccount: account('1132', 'Notes receivable') }));
    fixture.detectChanges();
    expect(text('fi-collection-map-save-error')).toContain('Another user changed these accounts first');
    expect(text('fi-collection-map-version')).toContain('Version 5');
    expect(input('unappliedCashAccountCode').value).toBe('2181');
    expect(input('receivableAccountCode').value).toBe('1132');
    submit();
    expect(http.expectOne(isPut).request.body).toMatchObject({ version: 5 });
  });

  it('FI-COLL-MAP-UI-010 400 lists field details, 500 shows the trace ID, 403 is named; nothing changes on screen', () => {
    const { http, type, submit, text, by, input, fixture } = create();
    type('unappliedCashAccountCode', '2182');
    submit();
    http.expectOne(isPut).flush({ status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { version: ['Version must be 0 (not configured) or the saved version.'] } }, { status: 400, statusText: 'Bad' });
    fixture.detectChanges();
    expect(text('fi-collection-map-save-error')).toContain('version: Version must be 0');
    submit();
    http.expectOne(isPut).flush({ status: 500, traceId: 'trace-77' }, { status: 500, statusText: 'Server' });
    fixture.detectChanges();
    expect(text('fi-collection-map-save-error')).toContain('trace-77');
    submit();
    http.expectOne(isPut).flush({ status: 403, errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(by('fi-collection-map-save-error')).not.toBeNull();
    expect(text('fi-collection-map-save-error')).not.toContain('COMMON.');
    expect(text('fi-collection-map-code-unappliedCashAccountCode')).toBe('2181');
    expect(input('unappliedCashAccountCode').value).toBe('2182');
  });

  it('FI-COLL-MAP-UI-011 a failed read shows the error with retry; network and 500 are distinguished', () => {
    const fixture = TestBed.createComponent(CollectionMapPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isGet).flush({ status: 500, traceId: 'trace-9' }, { status: 500, statusText: 'Server' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="fi-collection-map-error"]')!.textContent).toContain('trace-9');
    expect(element.querySelector('[data-testid="fi-collection-map-form"]')).toBeNull();
    (element.querySelector('[data-testid="fi-collection-map-retry"]') as HTMLButtonElement).click();
    http.expectOne(isGet).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-collection-map-error"]')!.textContent).not.toContain('trace-9');
    (element.querySelector('[data-testid="fi-collection-map-retry"]') as HTMLButtonElement).click();
    http.expectOne(isGet).flush(saved());
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-collection-map-error"]')).toBeNull();
  });

  it('FI-COLL-MAP-UI-012 a company switch drops input and feedback and reads the new company map; a late save result is ignored', () => {
    const { http, type, submit, text, by, fixture, view } = create();
    type('unappliedCashAccountCode', '2182');
    expect(view.hasUnsavedChanges()).toBe(true);
    submit();
    const pending = http.expectOne(isPut);
    company.set({ id: 2 });
    TestBed.tick();
    http.expectOne(isGet).flush(unconfigured);
    pending.flush({ map: saved(3), changed: true });
    fixture.detectChanges();
    expect(by('fi-collection-map-notice')).toBeNull();
    expect(text('fi-collection-map-unconfigured')).toContain('cannot be posted');
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-COLL-MAP-UI-013 undo restores the saved codes and clears field errors', () => {
    const { type, by, view } = create();
    type('unappliedCashAccountCode', '');
    expect(by('fi-collection-map-field-error-unappliedCashAccountCode')).not.toBeNull();
    by('fi-collection-map-undo')!.click();
    TestBed.tick();
    expect(view.form.getRawValue().unappliedCashAccountCode).toBe('2181');
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-COLL-MAP-UI-014 helpers: account errors keep only known fields and reasons; unusable and differs detection', () => {
    const error = (body: object, status = 422) => new HttpErrorResponse({ status, error: body });
    expect(collectionMapAccountErrors(error({ errorCode: 'FI.COLLECTION_MAP.ACCOUNT_INVALID',
      accountErrors: { unappliedCashAccountCode: 'X', receivableAccountCode: 'FI.COLLECTION_MAP.SAME_ACCOUNT', payableAccountCode: 'MD.ACCOUNT.NOT_FOUND' } })))
      .toEqual({ unappliedCashAccountCode: 'other', receivableAccountCode: 'FI.COLLECTION_MAP.SAME_ACCOUNT' });
    expect(collectionMapAccountErrors(error({ errorCode: 'FI.PURCHASE_MAP.ACCOUNT_INVALID', accountErrors: { receivableAccountCode: 'MD.ACCOUNT.INACTIVE' } }))).toEqual({});
    expect(collectionMapAccountErrors(error({ errorCode: 'FI.COLLECTION_MAP.ACCOUNT_INVALID' }, 400))).toEqual({});
    expect(collectionMapAccountErrors(new Error('x'))).toEqual({});
    expect(collectionAccountUnusable(null)).toBe(false);
    expect(collectionAccountUnusable(account('1', null))).toBe(true);
    expect(receivableDiffersFromSalesMap(saved(1, { warnings: [DIFFERS] }) as never)).toBe(true);
    expect(receivableDiffersFromSalesMap(saved(1) as never)).toBe(false);
    expect(receivableDiffersFromSalesMap({ ...unconfigured, warnings: [DIFFERS] } as never)).toBe(false);
    expect(TestBed.inject(CollectionMapService).errorKey(error({ errorCode: 'FI.COLLECTION_MAP.VERSION_CONFLICT' }, 409)))
      .toBe('errors.FI.COLLECTION_MAP.VERSION_CONFLICT');
  });

  it('FI-COLL-MAP-UI-015 collection wording: title, labels and hints name receipts and applications, never purchases or payables', () => {
    const { element, by } = create();
    const page = element.textContent ?? '';
    expect(element.querySelector('h1')!.textContent).toBe('Collection accounts');
    expect(by('fi-collection-map-saved-unappliedCashAccountCode')!.textContent).toContain('Unapplied cash account');
    expect(by('fi-collection-map-saved-receivableAccountCode')!.textContent).toContain('Receivable control account');
    expect(page).toContain('collection receipt');
    expect(page).toContain('fund account');
    expect(page).not.toMatch(/purchase|payable|input tax/i);
  });

  it('FI-COLL-MAP-UI-016 an AR account that differs from the sales invoice map shows a yellow warning naming both codes', () => {
    const { text, by } = create(saved(4, { receivableAccount: account('1132', 'Notes receivable'), warnings: [DIFFERS] }));
    const warning = by('fi-collection-map-receivable-differs')!;
    expect(warning.querySelector('.ant-alert-warning')).not.toBeNull();
    expect(text('fi-collection-map-receivable-differs')).toContain('1132');
    expect(text('fi-collection-map-receivable-differs')).toContain('1131');
    expect(text('fi-collection-map-receivable-differs')).toContain('Sales invoice accounts');
    expect(text('fi-collection-map-receivable-differs')).not.toContain('{{');
  });
});
