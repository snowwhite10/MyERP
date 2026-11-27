import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { OtherPaymentEditor } from './other-payment-editor';
import { OtherPaymentPage } from './other-payment-page';

const row = (status = 'Draft') => ({ id: 7, documentNo: 'OP2026110007', payeeDescription: 'Utility', businessDate: '2026-11-27',
  currency: 'TWD', amount: 'EXACT_AMOUNT', expenseCategoryId: 3, expenseCategoryCode: 'EXP', expenseCategoryName: 'Utilities',
  bankAccountId: 9, bankAccountCode: 'BANK', bankAccountName: 'Cash', bankReference: 'WD-7', status, version: 1, journalId: null });
const list = (rows: object[] = [row()]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 })
  .replace('"EXACT_AMOUNT"', '999999999999999.9999');
const isList = (r: HttpRequest<unknown>) => r.url === '/api/v1/ap/other-payments';

describe('FI-OP-UI other payment list', () => {
  let granted: Set<string>;
  const company = signal<{ id: number } | null>({ id: 1 });
  beforeEach(() => {
    granted = new Set(['AP.OTHER_PAYMENT.VIEW', 'GL.OTHER_PAYMENT_MAP.VIEW']);
    company.set({ id: 1 });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function create(body = list()) {
    const fixture = TestBed.createComponent(OtherPaymentPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(body);
    if (granted.has('GL.OTHER_PAYMENT_MAP.VIEW')) http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [
      { expenseCategoryId: 3, configured: false, expenseCategoryActive: true, expenseAccount: null },
    ] });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, http, element, view: fixture.componentInstance };
  }

  it('FI-OP-UI-003 labels a non-AP expense, exact amount, and missing account setup', () => {
    const { element } = create();
    expect(element.textContent).toContain('does not settle a purchase invoice');
    expect(element.textContent).toContain('999,999,999,999,999.9999 TWD');
    expect(element.querySelector('[data-testid="fi-op-map-7"]')?.textContent).toContain('Missing expense account');
    expect(element.textContent).toContain('Draft');
  });

  it('FI-OP-UI-002 without GL map permission does not request or guess account setup', () => {
    granted.delete('GL.OTHER_PAYMENT_MAP.VIEW');
    const { element } = create();
    expect(element.querySelector('[data-testid="fi-op-map-7"]')?.textContent).toContain('Check with a GL map viewer');
  });

  it('FI-OP-UI-004 distinguishes empty, filtered empty and invalid date range', () => {
    const { fixture, http, element, view } = create(list([]));
    expect(element.textContent).toContain('No other payments yet.');
    view.status = 'Posted'; view.search();
    http.expectOne(r => isList(r) && r.params.get('status') === 'Posted').flush(list([]));
    http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [] });
    fixture.detectChanges();
    expect(element.textContent).toContain('No payments match these filters.');
    view.from = '2026-11-30'; view.to = '2026-11-01'; view.search(); fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-range-error"]')?.textContent).toContain('End date');
    http.expectNone(isList);
  });

  it('FI-OP-UI-004 500 shows traceId and retry; mapping failure is never called missing', () => {
    const { fixture, http, element, view } = create();
    view.load();
    http.expectOne(isList).flush({ errorCode: 'UNKNOWN', traceId: 'trace-77' }, { status: 500, statusText: 'Server Error' });
    http.expectOne('/api/v1/gl/other-payment-account-map').flush('error', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(element.textContent).toContain('trace-77');
    element.querySelector<HTMLButtonElement>('[data-testid="fi-op-retry"]')!.click();
    http.expectOne(isList).flush(list());
    http.expectOne('/api/v1/gl/other-payment-account-map').flush('error', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-map-7"]')?.textContent).toContain('unavailable');
  });

  it('FI-OP-UI-010 a dirty inline editor survives filter reset and blocks route leave', () => {
    granted.add('AP.OTHER_PAYMENT.CREATE');
    const { fixture, http, view, element } = create();
    view.openCreate(); fixture.detectChanges();
    http.expectOne(r => r.url === '/api/v1/md/income-expense-categories').flush({ items: [], totalCount: 0 });
    http.expectOne(r => r.url === '/api/v1/md/fund-accounts').flush({ items: [], totalCount: 0 });
    const child = fixture.debugElement.query(By.directive(OtherPaymentEditor)).componentInstance as OtherPaymentEditor;
    child.draft.payeeDescription = 'Power'; fixture.detectChanges();
    expect(view.hasUnsavedChanges()).toBe(true);
    view.reset(); http.expectOne(isList).flush(list());
    http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [] });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-editor"]')).not.toBeNull();
    expect(view.hasUnsavedChanges()).toBe(true);
  });

  it('FI-OP-UI-022 a deleted draft is confirmed by number and the refreshed list no longer has it', () => {
    granted.add('AP.OTHER_PAYMENT.DELETE');
    const { fixture, http, view, element } = create();
    view.openDetail(7); fixture.detectChanges();
    http.expectOne(r => r.url === '/api/v1/md/income-expense-categories').flush({ items: [], totalCount: 0 });
    http.expectOne(r => r.url === '/api/v1/md/fund-accounts').flush({ items: [], totalCount: 0 });
    http.expectOne('/api/v1/ap/other-payments/7').flush(list().replace(/^.*?\[(.*)\].*$/, '$1'));
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    element.querySelector<HTMLButtonElement>('[data-testid="fi-op-delete"]')!.click();
    http.expectOne(r => r.method === 'DELETE').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(isList).flush(list([]));
    http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [] });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-editor"]')).toBeNull();
    expect(element.querySelector('[data-testid="fi-op-deleted"]')?.textContent).toContain('Draft payment OP2026110007 was deleted');
    expect(element.querySelector('[data-testid="fi-op-row-7"]')).toBeNull();
  });
});

describe('FI-OP-UI other payment list URL state (WU-FI-008c5)', () => {
  let query: Record<string, string>;
  const navigate = vi.fn((_commands: unknown[], _extras: { queryParams: Record<string, unknown> }) => Promise.resolve(true));
  const company = signal<{ id: number } | null>({ id: 1 });
  beforeEach(() => {
    query = {}; navigate.mockClear(); company.set({ id: 1 });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap(query) } }) },
      { provide: Router, useValue: { navigate } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => code === 'AP.OTHER_PAYMENT.VIEW' } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  const lastQuery = () => navigate.mock.calls.at(-1)![1].queryParams;
  const params = (r: HttpRequest<unknown>) => ['keyword', 'status', 'from', 'to', 'page', 'pageSize', 'sort'].map(key => r.params.get(key));
  function start() {
    const fixture = TestBed.createComponent(OtherPaymentPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    return { fixture, http, view: fixture.componentInstance, first: http.expectOne(isList) };
  }

  it('FI-OP-UI-030 restores keyword, status, dates, page, size and sort from a shared link into the first request', async () => {
    query = { keyword: ' WD-7 ', status: 'Posted', from: '2026-11-01', to: '2026-11-30', page: '3', pageSize: '50', sort: 'amount' };
    const { fixture, first, view } = start();
    expect(params(first.request)).toEqual(['WD-7', 'Posted', '2026-11-01', '2026-11-30', '3', '50', 'amount']);
    first.flush(list([row()]).replace('"totalCount":1', '"totalCount":120'));
    fixture.detectChanges(); await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector<HTMLInputElement>('[data-testid="fi-op-keyword"]')!.value).toBe('WD-7');
    expect(element.querySelector<HTMLSelectElement>('[data-testid="fi-op-status-filter"]')!.value).toBe('Posted');
    expect(element.querySelector('th[nzcolumnkey="amount"]')?.className).toContain('ant-table-column-sort');
    expect([view.keyword, view.status, view.from, view.to, view.page(), view.pageSize(), view.sort()])
      .toEqual(['WD-7', 'Posted', '2026-11-01', '2026-11-30', 3, 50, 'amount']);
    expect(view.filtered()).toBe(true);
  });

  it('FI-OP-UI-031 invalid URL values fall back to defaults without an error or a bad request', () => {
    query = { keyword: 'x'.repeat(51), status: 'Deleted', from: '2026-02-30', to: 'bad', page: '-2', pageSize: '33', sort: 'payee' };
    const { fixture, first, view } = start();
    expect(params(first.request)).toEqual([null, null, null, null, '1', '20', '-businessDate']);
    first.flush(list());
    fixture.detectChanges();
    expect(view.error()).toBeNull();
    expect(view.rangeInvalid()).toBe(false);
    expect(view.filtered()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="fi-op-range-error"]')).toBeNull();
  });

  it('FI-OP-UI-031 a reversed date range in the URL is dropped instead of blocking the list', () => {
    query = { from: '2026-11-30', to: '2026-11-01', status: 'Draft' };
    const { first, view } = start();
    expect(params(first.request).slice(0, 4)).toEqual([null, 'Draft', null, null]);
    first.flush(list());
    expect(view.rangeInvalid()).toBe(false);
  });

  it('FI-OP-UI-032 search writes the submitted filters to the URL, omits defaults and resets to page 1', () => {
    query = { page: '2' };
    const { first, http, view } = start();
    first.flush(list());
    view.keyword = '  Power '; view.status = 'Draft'; view.from = '2026-11-01'; view.search();
    const request = http.expectOne(isList);
    expect(params(request.request)).toEqual(['Power', 'Draft', '2026-11-01', null, '1', '20', '-businessDate']);
    request.flush(list());
    expect(lastQuery()).toEqual({ keyword: 'Power', status: 'Draft', from: '2026-11-01', to: null, page: null, pageSize: null, sort: null });
    expect(navigate.mock.calls.at(-1)![1]).toMatchObject({ replaceUrl: true });
  });

  it('FI-OP-UI-032 paging and sort use the submitted filters, not unsent typing, and are written to the URL', () => {
    query = { status: 'Posted' };
    const { first, http, view } = start();
    first.flush(list());
    view.keyword = 'typed but not searched';
    view.onQuery({ pageIndex: 2, pageSize: 100, sort: [{ key: 'amount', value: 'descend' }], filter: [] });
    const request = http.expectOne(isList);
    expect(params(request.request)).toEqual([null, 'Posted', null, null, '2', '100', '-amount']);
    request.flush(list());
    expect(lastQuery()).toEqual({ keyword: null, status: 'Posted', from: null, to: null, page: 2, pageSize: 100, sort: '-amount' });
  });

  it('FI-OP-UI-033 an invalid range typed by the user keeps the URL and the last list unchanged', () => {
    const { first, http, view } = start();
    first.flush(list());
    navigate.mockClear();
    view.from = '2026-11-30'; view.to = '2026-11-01'; view.search();
    http.expectNone(isList);
    expect(navigate).not.toHaveBeenCalled();
    expect(view.rangeInvalid()).toBe(true);
  });

  it('FI-OP-UI-033 a deep link past the last page re-reads page 1 and corrects the URL', () => {
    query = { page: '9' };
    const { first, http, view } = start();
    first.flush(JSON.stringify({ items: [], totalCount: 3, page: 9, pageSize: 20 }));
    const retry = http.expectOne(isList);
    expect(retry.request.params.get('page')).toBe('1');
    retry.flush(list());
    expect(view.page()).toBe(1);
    expect(lastQuery()['page']).toBeNull();
  });

  it('FI-OP-UI-034 a company switch drops the restored query, reads page 1 and clears the URL', () => {
    query = { keyword: 'WD', page: '2', sort: 'documentNo' };
    const { first, http, view } = start();
    first.flush(list());
    company.set({ id: 2 }); TestBed.tick();
    const request = http.expectOne(isList);
    expect(params(request.request)).toEqual([null, null, null, null, '1', '20', 'documentNo']);
    request.flush(list());
    expect(view.keyword).toBe('');
    expect(lastQuery()).toEqual({ keyword: null, status: null, from: null, to: null, page: null, pageSize: null, sort: 'documentNo' });
  });
});
