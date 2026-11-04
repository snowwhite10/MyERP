import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { CreditLimitPage } from './credit-limit-page';

const API = '/api/v1/ar/credit-limits';
const active = '{"id":11,"customerId":7,"baseCurrency":"TWD","effectiveFrom":"2026-01-01","effectiveTo":"2026-12-31","amount":999999999999999.9999,"status":"Active","version":2}';
const history = '{"id":12,"customerId":8,"baseCurrency":"TWD","effectiveFrom":"2025-01-01","effectiveTo":"2025-12-31","amount":0,"status":"Deactivated","version":3}';
const page = (...rows: string[]) => `{"items":[${rows.join(',')}],"totalCount":${rows.length},"page":1,"pageSize":20}`;
const customer = { id: 7, code: 'C007', name: 'Harbor Trading', categoryId: null, isActive: true, version: 1 };

describe('FI-CREDIT-019 credit limit page', () => {
  const confirm = vi.fn(() => of(true));
  const select = vi.fn(() => of([customer]));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear(); select.mockClear();
    granted = new Set(['AR.CREDIT.VIEW', 'AR.CREDIT.CREATE', 'AR.CREDIT.EDIT', 'AR.CREDIT.DEACTIVATE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm, select } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open(body = page(active, history)) {
    const fixture = TestBed.createComponent(CreditLimitPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === API && r.method === 'GET').flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const text = (id: string) => element.querySelector(`[data-testid="${id}"]`)?.textContent?.trim() ?? null;
    return { page: fixture.componentInstance, http, element, text, fixture };
  }

  it('shows exact amounts, status and history-only reason; actions follow permission and state', () => {
    const { text, element } = open();
    expect(text('fi-credit-amount-11')).toBe('999,999,999,999,999.9999 TWD');
    expect(text('fi-credit-customer-11')).toBe('Customer #7');
    expect(text('fi-credit-state-12')).toBe('Deactivated');
    expect(text('fi-credit-history-12')).toContain('cannot be edited');
    expect(element.querySelector('[data-testid="fi-credit-edit-11"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="fi-credit-edit-12"]')).toBeNull();
    expect(element.querySelector('[data-testid="fi-credit-deactivate-12"]')).toBeNull();
  });

  it('FI-CREDIT-022/023 labels rows, deep-linked filter and notices from server customer labels', () => {
    const labelled = active.replace('"version":2', '"version":2,"customerCode":"C007","customerName":"Harbor Trading"');
    const deleted = history.replace('"version":3', '"version":3,"customerCode":"C008","customerName":"Old Mill"');
    const { page: view, http, fixture, text } = open(page(labelled, deleted));
    expect(text('fi-credit-customer-11')).toBe('C007 · Harbor Trading');
    expect(text('fi-credit-customer-12')).toBe('C008 · Old Mill');
    // A deep link only knows the id; the filter shows the server label once the list arrives.
    view.filterCustomer.set({ id: 8, code: '', name: '' });
    fixture.detectChanges();
    expect(text('fi-credit-filter-customer')).toBe('C008 · Old Mill');
    // Server labels win over an older picker label; a row MD cannot label stays "Customer #id".
    view.customers.update(map => new Map(map).set(7, { id: 7, code: 'C007', name: 'Old picker name' }));
    view.load();
    http.expectOne(r => r.url === API).flush(page(labelled, history.replace('"customerId":8', '"customerId":9'),
      active.replace('"id":11', '"id":14').replace('"customerId":7', '"customerId":10')
        .replace('"version":2', '"version":2,"customerCode":null,"customerName":null')));
    fixture.detectChanges();
    expect(text('fi-credit-customer-11')).toBe('C007 · Harbor Trading');
    expect(text('fi-credit-customer-12')).toBe('Customer #9');
    expect(text('fi-credit-customer-14')).toBe('Customer #10');
    view.load();
    http.expectOne(r => r.url === API).flush(page(active.replace('"version":2', '"version":2,"customerCode":null,"customerName":null')));
    fixture.detectChanges();
    expect(text('fi-credit-customer-11')).toBe('C007 · Old picker name');
    view.confirmDeactivate(view.rows()[0]);
    http.expectOne(`${API}/11/deactivate`).flush('{"id":11,"customerId":7,"baseCurrency":"TWD","effectiveFrom":"2026-01-01",'
      + '"effectiveTo":"2026-12-31","amount":1,"status":"Deactivated","version":3,"customerCode":"C007","customerName":"Harbor Renamed"}');
    http.expectOne(r => r.url === API && r.method === 'GET').flush(page());
    expect(view.notice()?.params['customer']).toBe('C007 · Harbor Renamed');
  });

  it('FI-CREDIT-012 hides create, edit and deactivate with VIEW only', () => {
    granted = new Set(['AR.CREDIT.VIEW']);
    const { element } = open();
    for (const id of ['fi-credit-create', 'fi-credit-edit-11', 'fi-credit-deactivate-11'])
      expect(element.querySelector(`[data-testid="${id}"]`)).toBeNull();
  });

  it('validates the filter window locally and sends customer, status and dates', () => {
    const { page: view, http } = open();
    view.from = '2026-12-01'; view.to = '2026-01-01'; view.search();
    expect(view.validation()).toBe('fi.creditLimit.invalidWindow');
    http.expectNone(r => r.url === API);
    view.pickFilterCustomer();
    view.from = '2026-02-29'; view.to = ''; view.search();
    expect(view.validation()).toBe('fi.creditLimit.invalidWindow');
    view.from = '2028-02-29'; view.status = 'Active'; view.search();
    const request = http.expectOne(r => r.url === API);
    expect(request.request.params.get('customerId')).toBe('7');
    expect(request.request.params.get('status')).toBe('Active');
    expect(request.request.params.get('from')).toBe('2028-02-29');
    expect(request.request.params.has('to')).toBe(false);
    request.flush(page());
    expect(view.filtered()).toBe(true);
  });

  it('FI-CREDIT-004 blocks invalid input without a request, then creates with exact text and re-reads', () => {
    const { page: view, http, fixture, text } = open();
    view.openCreate(); view.save();
    expect(view.invalid('customerId')).toBe(true);
    view.pickFormCustomer();
    view.form.patchValue({ effectiveFrom: '2027-01-31', effectiveTo: '2027-01-30', amount: '1.23456' }); view.save();
    expect(view.invalid('effectiveTo')).toBe(true);
    expect(view.invalid('amount')).toBe(true);
    http.expectNone(r => r.method === 'POST');
    view.form.patchValue({ effectiveTo: '2027-12-31', amount: '123456789012.3456' }); view.save();
    const post = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ customerId: 7, effectiveFrom: '2027-01-31', effectiveTo: '2027-12-31', amount: '123456789012.3456' });
    post.flush('{"id":13,"customerId":7,"baseCurrency":"TWD","effectiveFrom":"2027-01-31","effectiveTo":"2027-12-31","amount":123456789012.3456,"status":"Active","version":1}');
    http.expectOne(r => r.url === API && r.method === 'GET').flush(page(active));
    fixture.detectChanges();
    expect(view.dialogOpen()).toBe(false);
    expect(text('fi-credit-notice')).toContain('Added the credit limit for C007 · Harbor Trading: 2027-01-31 to 2027-12-31, 123,456,789,012.3456 TWD (version 1).');
  });

  it('FI-CREDIT-007/009 keeps the dialog and named reason on overlap; stale version reloads the list', () => {
    const { page: view, http } = open();
    view.openEdit(view.rows()[0]);
    expect(view.form.getRawValue().amount).toBe('999999999999999.9999');
    view.save();
    const put = http.expectOne(`${API}/11`);
    expect(put.request.body).toEqual({ version: 2, customerId: 7, effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', amount: '999999999999999.9999' });
    put.flush('{"errorCode":"FI.CREDIT.LIMIT_OVERLAP","traceId":"t1"}', { status: 409, statusText: 'Conflict' });
    expect(view.dialogOpen()).toBe(true);
    expect(view.saveError()).toBe('errors.FI.CREDIT.LIMIT_OVERLAP');
    http.expectNone(r => r.method === 'GET');
    view.save();
    http.expectOne(`${API}/11`).flush('{"errorCode":"COMMON.CONCURRENCY.CONFLICT"}', { status: 409, statusText: 'Conflict' });
    // A held stale version can never succeed: the dialog closes and the reason stays above the re-read list.
    expect(view.dialogOpen()).toBe(false);
    expect(view.actionError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    http.expectOne(r => r.url === API && r.method === 'GET').flush(page(active.replace('"version":2', '"version":3')));
    expect(view.rows()[0].version).toBe(3);
    expect(view.notice()).toBeNull();
  });

  it('FI-CREDIT-017 marks server field errors and shows trace ID for 500 without a success message', () => {
    const { page: view, http } = open();
    view.openEdit(view.rows()[0]);
    view.save();
    http.expectOne(`${API}/11`).flush('{"errorCode":"COMMON.REQUEST.VALIDATION_FAILED","errors":{"amount":["bad"]}}', { status: 400, statusText: 'Bad' });
    expect(view.invalid('amount')).toBe(true);
    expect(view.saveError()).toBe('fi.creditLimit.validationFailed');
    view.save();
    http.expectOne(`${API}/11`).flush('{"traceId":"trace-credit-500"}', { status: 500, statusText: 'Error' });
    expect(view.saveTraceId()).toBe('trace-credit-500');
    expect(view.notice()).toBeNull();
  });

  it('deactivates with version after confirmation and reports the saved period', () => {
    const { page: view, http } = open();
    view.confirmDeactivate(view.rows()[0]);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'fi.creditLimit.deactivateConfirm', danger: true }));
    const request = http.expectOne(`${API}/11/deactivate`);
    expect(request.request.body).toEqual({ version: 2 });
    request.flush('{"id":11,"customerId":7,"baseCurrency":"TWD","effectiveFrom":"2026-01-01","effectiveTo":"2026-12-31","amount":1,"status":"Deactivated","version":3}');
    http.expectOne(r => r.url === API && r.method === 'GET').flush(page(history));
    expect(view.notice()?.key).toBe('fi.creditLimit.deactivated');

    view.confirmDeactivate({ ...view.rows()[0], id: 12, version: 3 });
    http.expectOne(`${API}/12/deactivate`).flush('{"errorCode":"FI.CREDIT.LIMIT_NOT_ACTIVE"}', { status: 422, statusText: 'Unprocessable' });
    expect(view.actionError()).toBe('errors.FI.CREDIT.LIMIT_NOT_ACTIVE');
    http.expectOne(r => r.url === API && r.method === 'GET').flush(page(history));
  });

  it('shows empty, search-empty and retryable load failure', () => {
    const { page: view, http, fixture, element, text } = open(page());
    expect(element.textContent).toContain('No credit limits are set');
    view.status = 'Deactivated'; view.search();
    http.expectOne(r => r.url === API).flush(page());
    fixture.detectChanges();
    expect(element.textContent).toContain('No credit limits match');
    view.load();
    http.expectOne(r => r.url === API).flush('{"traceId":"trace-list"}', { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(text('fi-credit-error')).toContain('trace-list');
    element.querySelector<HTMLButtonElement>('[data-testid="fi-credit-retry"]')!.click();
    http.expectOne(r => r.url === API).flush(page(active));
    fixture.detectChanges();
    expect(text('fi-credit-error')).toBeNull();
  });
});
