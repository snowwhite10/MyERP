import { APP_BASE_HREF } from '@angular/common';
import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { APP_ICONS } from '../../core/icons';
import enFi from '../../../../public/i18n/en/fi.json';
import hansFi from '../../../../public/i18n/zh-Hans/fi.json';
import hantFi from '../../../../public/i18n/zh-Hant/fi.json';
import { GlJournalPage } from './gl-journal-page';

const API = '/api/v1/gl/journals';
const PERIODS = '/api/v1/gl/periods';
const journal = (id: number, extra: Record<string, unknown> = {}) => ({ id, periodId: 21, businessDate: '2026-11-12', baseCurrency: 'TWD',
  status: 'Posted', totalDebit: '1250000.5000', totalCredit: '1250000.5000', sourceModule: 'SD', sourceType: 'Invoice', sourceId: 9,
  sourceVersion: 2, createdAt: '2026-11-12T10:20:30', createdBy: 4, ...extra });
/** Raw JSON text as the server sends it: decimal amounts are JSON numbers with every saved digit. */
const listText = (items: object[], totalCount = items.length) => JSON.stringify({ items, totalCount, page: 1, pageSize: 20 })
  .replace(/"(totalDebit|totalCredit|debit|credit)":"([^"]+)"/g, '"$1":$2');
const period = { id: 21, startDate: '2026-11-01', endDate: '2026-11-30', status: 'Open', version: 1,
  createdAt: '2026-10-20T09:15:00', createdBy: 1, updatedAt: null, updatedBy: null };

describe('FI-GL-JNL-UI posted journal page', () => {
  let query: Record<string, string>;
  const navigate = vi.fn(() => Promise.resolve(true));
  /** Enough of Router for link targets: the tree is the query params, serialised as the page path plus non-null params. */
  const createUrlTree = (_commands: unknown[], extras: { queryParams: Record<string, unknown> }) => extras.queryParams;
  const serializeUrl = (params: Record<string, unknown>) => '/gl/journals?' + new URLSearchParams(Object.entries(params)
    .filter(([, value]) => value !== null && value !== undefined).map(([key, value]) => [key, String(value)])).toString();
  let granted: Set<string>;
  beforeEach(() => {
    query = {}; navigate.mockClear();
    granted = new Set(['GL.JOURNAL.VIEW', 'GL.PERIOD.VIEW']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }, { provide: APP_BASE_HREF, useValue: '/' },
      { provide: ActivatedRoute, useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap(query) } }) },
      { provide: Router, useValue: { navigate, createUrlTree, serializeUrl } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const lastQuery = () => (navigate.mock.calls.at(-1) as unknown as [unknown, { queryParams: Record<string, unknown> }])[1].queryParams;

  function create(body = listText([journal(7), journal(6, { businessDate: '2026-11-03', periodId: 99 })]), periods: object[] = [period]) {
    const fixture = TestBed.createComponent(GlJournalPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    if (granted.has('GL.PERIOD.VIEW')) http.expectOne(r => r.url === PERIODS).flush({ items: periods, totalCount: periods.length, page: 1, pageSize: 200 });
    const first = http.expectOne(isList);
    first.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const text = (id: string) => (document.querySelector(`[data-testid="${id}"]`) ?? element.querySelector(`[data-testid="${id}"]`))
      ?.textContent?.trim() ?? null;
    return { view: fixture.componentInstance, http, element, text, fixture, first };
  }

  it('FI-GL-JNL-UI-001 lists Posted journals newest first with saved source, grouped exact totals and period dates', () => {
    const { text, first } = create();
    expect(first.request.params.get('sort')).toBe('-businessDate');
    expect(text('fi-gl-journal-date-7')).toBe('2026-11-12');
    expect(text('fi-gl-journal-source-7')).toBe('SD / Invoice #9 v2');
    expect(text('fi-gl-journal-debit-7')).toBe('1,250,000.5 TWD');
    expect(text('fi-gl-journal-credit-7')).toBe('1,250,000.5 TWD');
    expect(text('fi-gl-journal-period-7')).toBe('2026-11-01 – 2026-11-30');
    // A period not in the loaded list falls back to its number instead of a blank cell.
    expect(text('fi-gl-journal-period-6')).toBe('#99');
    expect(text('fi-gl-journal-posted-7')).toBe('2026-11-12 10:20');
  });

  it('FI-GL-JNL-UI-002 keeps 19-digit amounts exact', () => {
    const { text } = create(listText([journal(1, { totalDebit: '999999999999999.9999', totalCredit: '999999999999999.9999' })]));
    expect(text('fi-gl-journal-debit-1')).toBe('999,999,999,999,999.9999 TWD');
  });

  it('FI-GL-JNL-UI-002 sends every filter trimmed, writes the URL and resets to page 1', () => {
    const { view, http } = create();
    view.page.set(3);
    view.periodId = '21'; view.from = '2026-11-01'; view.to = '2026-11-30';
    view.sourceModule = ' SD '; view.sourceType = 'Invoice'; view.sourceId = ' 9 '; view.accountCode = '1100';
    view.search();
    const request = http.expectOne(isList);
    const p = request.request.params;
    expect([p.get('periodId'), p.get('from'), p.get('to'), p.get('sourceModule'), p.get('sourceType'), p.get('sourceId'), p.get('accountCode'), p.get('page')])
      .toEqual(['21', '2026-11-01', '2026-11-30', 'SD', 'Invoice', '9', '1100', '1']);
    request.flush(listText([]));
    expect(lastQuery()).toMatchObject({ periodId: 21, sourceModule: 'SD', sourceId: 9, accountCode: '1100', page: null });
  });

  it('FI-GL-JNL-UI-002 rejects reversed or impossible dates and a non-positive source ID without a request', () => {
    const { view, http, fixture, text } = create();
    view.from = '2026-12-01'; view.to = '2026-11-01'; view.search();
    fixture.detectChanges();
    expect(text('fi-gl-journal-validation')).toContain('cannot be after');
    view.from = '2026-02-30'; view.to = ''; view.search();
    expect(view.validation()).toBe('fi.glJournal.invalidRange');
    view.from = ''; view.sourceId = '0'; view.search();
    expect(view.validation()).toBe('fi.glJournal.invalidSourceId');
    view.sourceId = '1.5'; view.search();
    expect(view.validation()).toBe('fi.glJournal.invalidSourceId');
    http.expectNone(isList);
    view.sourceId = ''; view.from = '2026-11-30'; view.to = '2026-11-30'; view.search();
    expect(view.validation()).toBeNull();
    http.expectOne(isList).flush(listText([]));
  });

  it('FI-GL-JNL-UI-004 distinguishes an empty company from a filtered search with no result', () => {
    const { view, http, fixture, element } = create(listText([]));
    expect(element.textContent).toContain('No journals have been posted for this company yet');
    view.accountCode = '9999'; view.search();
    http.expectOne(isList).flush(listText([]));
    fixture.detectChanges();
    expect(element.textContent).toContain('No Posted journals match these filters');
  });

  it('FI-GL-JNL-UI-005 without period permission hides the period filter, sends no period read and shows period numbers', () => {
    granted = new Set(['GL.JOURNAL.VIEW']);
    query = { periodId: '21' };
    const { element, text, first } = create();
    expect(element.querySelector('[data-testid="fi-gl-journal-period"]')).toBeNull();
    expect(first.request.params.has('periodId')).toBe(false);
    expect(text('fi-gl-journal-period-7')).toBe('#21');
  });

  it('FI-GL-JNL-UI-005 a failed period read keeps the journal list and explains the numbered periods', () => {
    const fixture = TestBed.createComponent(GlJournalPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === PERIODS).flush({}, { status: 500, statusText: 'Error' });
    http.expectOne(isList).flush(listText([journal(7)]));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="fi-gl-journal-periods-failed"]')?.textContent).toContain('shown by number');
    expect(element.querySelector('[data-testid="fi-gl-journal-period-7"]')?.textContent?.trim()).toBe('#21');
  });

  it('FI-GL-JNL-UI-006 detail shows the saved source, account snapshots, one side per line and saved totals; no write control', () => {
    const { view, http, fixture, text } = create();
    view.openDetail(7);
    expect(lastQuery()).toMatchObject({ journal: 7 });
    http.expectOne(`${API}/7`).flush(JSON.stringify({ header: journal(7), lines: [
      { sequence: 1, accountId: 11, accountCode: '1100', accountName: 'Accounts receivable', accountVersion: 3, mappingVersion: 2, debit: '1250000.5000', credit: '0.0000' },
      { sequence: 2, accountId: 12, accountCode: '4100', accountName: 'Sales revenue (old name)', accountVersion: 1, mappingVersion: 2, debit: '0.0000', credit: '1250000.5000' },
    ] }).replace(/"(totalDebit|totalCredit|debit|credit)":"([^"]+)"/g, '"$1":$2'));
    fixture.detectChanges();
    expect(text('fi-gl-journal-detail-status')).toBe('Posted');
    expect(text('fi-gl-journal-detail-module')).toBe('SD');
    expect(text('fi-gl-journal-detail-source-version')).toBe('2');
    expect(text('fi-gl-journal-detail-period')).toBe('2026-11-01 – 2026-11-30');
    expect(text('fi-gl-journal-line-name-2')).toBe('Sales revenue (old name)');
    expect(text('fi-gl-journal-line-version-1')).toBe('Account v3 · mapping v2');
    expect(text('fi-gl-journal-line-debit-1')).toBe('1,250,000.5');
    expect(text('fi-gl-journal-line-credit-1')).toBe('');
    expect(text('fi-gl-journal-line-credit-2')).toBe('1,250,000.5');
    expect(text('fi-gl-journal-total-debit')).toBe('1,250,000.5');
    expect(text('fi-gl-journal-total-credit')).toBe('1,250,000.5');
    const detail = document.querySelector('[data-testid="fi-gl-journal-detail"]')!;
    expect(detail.querySelectorAll('button')).toHaveLength(0);
    view.closeDetail();
    expect(view.detailId()).toBeNull();
    expect(lastQuery()).toMatchObject({ journal: null });
  });

  it('FI-GL-JNL-UI-006 posted by user ID 0 shows System; a real user keeps the ID', () => {
    const { view, http, fixture, text } = create();
    view.openDetail(7);
    http.expectOne(`${API}/7`).flush({ header: journal(7, { createdBy: 0 }), lines: [] });
    fixture.detectChanges();
    expect(text('fi-gl-journal-detail-posted-by')).toBe('System');
    view.closeDetail();
    view.openDetail(6);
    http.expectOne(`${API}/6`).flush({ header: journal(6, { createdBy: 4 }), lines: [] });
    fixture.detectChanges();
    expect(text('fi-gl-journal-detail-posted-by')).toBe('4');
  });

  it('FI-GL-JNL-UI-007 detail 404 explains without retry; 500 shows trace ID and retry re-reads', () => {
    const { view, http, fixture, text } = create();
    view.openDetail(70);
    http.expectOne(`${API}/70`).flush(JSON.stringify({ errorCode: 'FI.GL_JOURNAL.NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(text('fi-gl-journal-detail-error')).toContain('Journal not found');
    expect(view.detailRetryable()).toBe(false);
    view.openDetail(7);
    http.expectOne(`${API}/7`).flush(JSON.stringify({ traceId: 'trace-jnl-500' }), { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(text('fi-gl-journal-detail-error')).toContain('trace-jnl-500');
    expect(view.detailRetryable()).toBe(true);
    view.loadDetail();
    http.expectOne(`${API}/7`).flush(JSON.stringify({ header: journal(7), lines: [] }));
    expect(view.detailError()).toBeNull();
    expect(view.detail()!.header.id).toBe(7);
  });

  it('FI-GL-JNL-UI-007 list 400, 403, 500 trace ID and network loss show distinct messages; retry re-reads', () => {
    const { view, http, fixture, text } = create();
    const fail = (body: object, status: number) => { view.load(); http.expectOne(isList).flush(JSON.stringify(body), { status, statusText: 'x' }); fixture.detectChanges(); };
    fail({ errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' }, 400);
    expect(view.error()).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    fail({ errorCode: 'COMMON.PERMISSION.DENIED' }, 403);
    expect(view.error()).toBe('errors.COMMON.PERMISSION.DENIED');
    fail({ traceId: 'trace-jnl-list' }, 500);
    expect(text('fi-gl-journal-error')).toContain('trace-jnl-list');
    view.load();
    http.expectOne(isList).error(new ProgressEvent('error'));
    expect(view.error()).toBe('errors.http.network');
    view.load();
    http.expectOne(isList).flush(listText([journal(7)]));
    expect(view.error()).toBeNull();
    expect(view.rows()).toHaveLength(1);
  });

  it('FI-GL-JNL-UI-008 restores filters, page, sort and open journal from the URL; invalid values fall back', () => {
    query = { from: '2026-11-01', to: 'bad', sourceModule: 'PUR', sourceId: '-3', accountCode: '2100', page: '2', pageSize: '50',
      sort: 'id', journal: '7', periodId: '21' };
    const fixture = TestBed.createComponent(GlJournalPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === PERIODS).flush({ items: [period], totalCount: 1, page: 1, pageSize: 200 });
    const list = http.expectOne(isList);
    const p = list.request.params;
    expect([p.get('from'), p.get('to'), p.get('sourceModule'), p.get('sourceId'), p.get('accountCode'), p.get('page'), p.get('pageSize'), p.get('sort'), p.get('periodId')])
      .toEqual(['2026-11-01', null, 'PUR', null, '2100', '2', '50', 'id', '21']);
    list.flush(listText([journal(7)], 21));
    http.expectOne(`${API}/7`).flush(JSON.stringify({ header: journal(7), lines: [] }));
    expect(fixture.componentInstance.detailId()).toBe(7);
  });

  it('FI-GL-JNL-UI-008 a deep link past the last page re-reads page 1', () => {
    query = { page: '9' };
    const { view, http } = create(listText([], 3));
    const retry = http.expectOne(isList);
    expect(retry.request.params.get('page')).toBe('1');
    retry.flush(listText([journal(7)], 3));
    expect(view.page()).toBe(1);
    expect(view.rows()).toHaveLength(1);
  });

  it('FI-GL-JNL-UI-010 journal number is a link with the applied query; plain click opens in place, modified clicks go to the browser', () => {
    query = { sourceModule: 'SD', sort: 'id' };
    const { view, http, element } = create();
    const link = element.querySelector<HTMLAnchorElement>('[data-testid="fi-gl-journal-open-7"]')!;
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe('/gl/journals?sourceModule=SD&sort=id&journal=7');
    expect(element.querySelector('[data-testid="fi-gl-journal-open-6"]')!.getAttribute('href'))
      .toBe('/gl/journals?sourceModule=SD&sort=id&journal=6');
    expect(link.getAttribute('aria-label')).toBe('Open journal 7 of 2026-11-12');
    // Record whether the page cancelled each click, then stop the test DOM from following the link.
    let leftToBrowser: boolean[] = [];
    element.addEventListener('click', event => { leftToBrowser.push(!event.defaultPrevented); event.preventDefault(); });
    for (const init of [{ ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { altKey: true }, { button: 1 }])
      link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ...init }));
    expect(leftToBrowser).toEqual([true, true, true, true, true]);
    expect(view.detailId()).toBeNull();
    http.expectNone(`${API}/7`);
    leftToBrowser = [];
    link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    expect(leftToBrowser).toEqual([false]);
    expect(view.detailId()).toBe(7);
    expect(lastQuery()).toMatchObject({ sourceModule: 'SD', sort: 'id', journal: 7 });
    http.expectOne(`${API}/7`).flush(JSON.stringify({ header: journal(7), lines: [] }));
  });

  it('FI-GL-JNL-UI-010 the source module example ends with an ellipsis in every language', () => {
    const { element } = create();
    expect(element.querySelector<HTMLInputElement>('[data-testid="fi-gl-journal-source-module"]')!.placeholder).toBe('e.g. SD…');
    for (const lang of [enFi, hansFi, hantFi]) expect(lang.glJournal.sourceModuleHint.endsWith('…')).toBe(true);
  });
});
