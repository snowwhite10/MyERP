import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { GlPeriodPage, validPeriodDate } from './gl-period-page';

const API = '/api/v1/gl/periods';
const open = { id: 21, startDate: '2026-11-01', endDate: '2026-11-30', status: 'Open', version: 1,
  createdAt: '2026-10-20T09:15:00', createdBy: 1, updatedAt: null, updatedBy: null };
const closed = { id: 20, startDate: '2026-10-01', endDate: '2026-10-31', status: 'Closed', version: 3,
  createdAt: '2026-09-20T09:15:00', createdBy: 1, updatedAt: '2026-11-02T18:05:44', updatedBy: 2 };
const page = (items: object[], totalCount = items.length) => ({ items, totalCount, page: 1, pageSize: 20 });

describe('FI-GL-UI accounting period page', () => {
  let query: Record<string, string>;
  const navigate = vi.fn(() => Promise.resolve(true));
  beforeEach(() => {
    query = {}; navigate.mockClear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap(query) } }) },
      { provide: Router, useValue: { navigate } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';

  function create(body: object = page([open, closed])) {
    const fixture = TestBed.createComponent(GlPeriodPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne(isList);
    first.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const text = (id: string) => element.querySelector(`[data-testid="${id}"]`)?.textContent?.trim() ?? null;
    return { view: fixture.componentInstance, http, element, text, fixture, first };
  }

  it('FI-GL-UI-001/006 shows saved dates, translated state, version and last change; closed rows explain posting is rejected', () => {
    const { text, element, first } = create();
    expect(first.request.params.get('sort')).toBe('-startDate');
    expect(text('fi-gl-period-start-21')).toBe('2026-11-01');
    expect(text('fi-gl-period-end-21')).toBe('2026-11-30');
    expect(text('fi-gl-period-state-21')).toBe('Open');
    expect(text('fi-gl-period-state-20')).toBe('Closed');
    expect(text('fi-gl-period-closed-hint-20')).toContain('rejected until the period is reopened');
    expect(element.querySelector('[data-testid="fi-gl-period-closed-hint-21"]')).toBeNull();
    expect(text('fi-gl-period-version-20')).toBe('3');
    expect(text('fi-gl-period-changed-21')).toBe('2026-10-20 09:15');
    expect(text('fi-gl-period-changed-20')).toBe('2026-11-02 18:05');
    // Read-only slice: no write controls are rendered in rows.
    expect(element.querySelectorAll('[data-testid^="fi-gl-period-row-"] button')).toHaveLength(0);
  });

  it('FI-GL-UI-004 distinguishes an empty company from a filtered search with no result', () => {
    const { view, http, fixture, element } = create(page([]));
    expect(element.textContent).toContain('No accounting periods have been set up');
    view.status = 'Closed'; view.search();
    http.expectOne(isList).flush(page([]));
    fixture.detectChanges();
    expect(element.textContent).toContain('No periods match these filters');
  });

  it('FI-GL-UI-002 rejects an invalid or reversed window without a request and sends a valid one-day window', () => {
    const { view, http, fixture, text } = create();
    view.from = '2026-12-31'; view.to = '2026-01-01'; view.search(); fixture.detectChanges();
    expect(text('fi-gl-period-validation')).toContain('cannot be after');
    view.from = '2026-02-30'; view.to = ''; view.search();
    http.expectNone(isList);
    view.from = '2024-02-29'; view.to = '2024-02-29'; view.search(); fixture.detectChanges();
    expect(text('fi-gl-period-validation')).toBeNull();
    const request = http.expectOne(isList);
    expect(request.request.params.get('from')).toBe('2024-02-29');
    expect(request.request.params.get('to')).toBe('2024-02-29');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([open]));
    expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ from: '2024-02-29', to: '2024-02-29' }) }));
  });

  it('FI-GL-UI-008 restores valid URL filters, ignores unknown values and re-reads page 1 past the end', () => {
    query = { status: 'Closed', from: '2026-01-01', to: 'bad', page: '9', pageSize: '50', sort: 'endDate' };
    const fixture = TestBed.createComponent(GlPeriodPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne(isList);
    expect(request.request.params.get('status')).toBe('Closed');
    expect(request.request.params.get('from')).toBe('2026-01-01');
    expect(request.request.params.has('to')).toBe(false);
    expect(request.request.params.get('page')).toBe('9');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.get('sort')).toBe('endDate');
    request.flush({ items: [], totalCount: 2, page: 9, pageSize: 50 });
    const retry = http.expectOne(isList);
    expect(retry.request.params.get('page')).toBe('1');
    retry.flush(page([closed]));
    fixture.detectChanges();
    expect(fixture.componentInstance.rows()).toHaveLength(1);
  });

  it('FI-GL-UI-008 falls back to defaults for an unknown status and sort', () => {
    query = { status: 'Archived', sort: 'version' };
    TestBed.createComponent(GlPeriodPage);
    TestBed.tick();
    const fallback = TestBed.inject(HttpTestingController).expectOne(isList);
    expect(fallback.request.params.has('status')).toBe(false);
    expect(fallback.request.params.get('sort')).toBe('-startDate');
    fallback.flush(page([]));
  });

  it('FI-GL-UI-007 shows server error with trace ID, permission and network messages; retry re-reads', () => {
    const { view, http, fixture, text } = create();
    view.load();
    http.expectOne(isList).flush({ errorCode: 'X', traceId: 'trace-500' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('fi-gl-period-error')).toContain('trace-500');
    expect(view.rows()).toHaveLength(0);
    view.load();
    http.expectOne(isList).flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(view.error()).toBe('errors.COMMON.PERMISSION.DENIED');
    view.load();
    http.expectOne(isList).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();
    expect(view.error()).toBe('errors.http.network');
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="fi-gl-period-retry"]')!.click();
    http.expectOne(isList).flush(page([open]));
    fixture.detectChanges();
    expect(view.error()).toBeNull();
    expect(text('fi-gl-period-start-21')).toBe('2026-11-01');
  });

  it('FI-GL-UI-002 drops a stale response when a newer search was sent', () => {
    const { view, http, fixture, text } = create();
    view.load(); const stale = http.expectOne(isList);
    view.load(); const fresh = http.expectOne(isList);
    fresh.flush(page([closed])); stale.flush(page([open]));
    fixture.detectChanges();
    expect(text('fi-gl-period-start-20')).toBe('2026-10-01');
    expect(text('fi-gl-period-start-21')).toBeNull();
  });

  it('validates real calendar dates only', () => {
    expect(validPeriodDate('2024-02-29')).toBe(true);
    expect(validPeriodDate('2025-02-29')).toBe(false);
    expect(validPeriodDate('2026-1-01')).toBe(false);
  });
});
