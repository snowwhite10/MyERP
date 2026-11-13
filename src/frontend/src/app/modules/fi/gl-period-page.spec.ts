import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
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
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    query = {}; navigate.mockClear(); confirm.mockClear();
    granted = new Set(['GL.PERIOD.VIEW', 'GL.PERIOD.CREATE', 'GL.PERIOD.EDIT', 'GL.PERIOD.CLOSE', 'GL.PERIOD.REOPEN', 'GL.PERIOD.DELETE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap(query) } }) },
      { provide: Router, useValue: { navigate } },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
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
    granted = new Set(['GL.PERIOD.VIEW']);
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
    // A view-only role gets no write control in rows or header (FI-GL-UI-015).
    expect(element.querySelectorAll('[data-testid^="fi-gl-period-row-"] button')).toHaveLength(0);
    expect(element.querySelector('[data-testid="fi-gl-period-create"]')).toBeNull();
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

  describe('interface 6.21 write dialogs', () => {
    const isWrite = (method: string, url: string) => (r: HttpRequest<unknown>) => r.method === method && r.url === url;
    const result = (period: object, changed = true) => ({ period, changed });
    const click = (element: HTMLElement, id: string) => element.querySelector<HTMLButtonElement>(`[data-testid="${id}"]`)!.click();

    it('FI-GL-UI-010 creates an Open period, announces the saved dates and version, then re-reads the list', () => {
      const { view, http, fixture, text, element } = create();
      click(element, 'fi-gl-period-create');
      expect(view.dialogOpen()).toBe(true);
      expect(view.editing()).toBeNull();
      view.form.setValue({ startDate: '2026-12-01', endDate: '2026-12-31' });
      view.save();
      const post = http.expectOne(isWrite('POST', API));
      expect(post.request.body).toEqual({ startDate: '2026-12-01', endDate: '2026-12-31' });
      post.flush(result({ ...open, id: 22, startDate: '2026-12-01', endDate: '2026-12-31', version: 1 }), { status: 201, statusText: 'Created' });
      http.expectOne(isList).flush(page([open, closed]));
      fixture.detectChanges();
      expect(view.dialogOpen()).toBe(false);
      expect(view.notice()?.type).toBe('success');
      expect(text('fi-gl-period-notice')).toContain('2026-12-01');
      expect(text('fi-gl-period-notice')).toContain('version 1');
    });

    it('FI-GL-UI-012 rejects missing, impossible and reversed dates without a request; a one-day leap-day period is sent', () => {
      const { view, http } = create();
      view.openCreate();
      view.save();
      expect(view.invalid('startDate')).toBe(true);
      expect(view.invalid('endDate')).toBe(true);
      view.form.setValue({ startDate: '2025-02-29', endDate: '2025-03-31' }); view.save();
      expect(view.invalid('startDate')).toBe(true);
      view.form.setValue({ startDate: '2026-12-31', endDate: '2026-12-01' }); view.save();
      expect(view.invalid('startDate')).toBe(false);
      expect(view.invalid('endDate')).toBe(true);
      http.expectNone(r => r.method !== 'GET');
      view.form.setValue({ startDate: '2028-02-29', endDate: '2028-02-29' }); view.save();
      http.expectOne(isWrite('POST', API)).flush(result({ ...open, startDate: '2028-02-29', endDate: '2028-02-29' }));
      http.expectOne(isList).flush(page([open]));
    });

    it('FI-GL-UI-011/014 date edit sends held version without status; changed=false is a warning, not success', () => {
      const { view, http, fixture, text, element } = create();
      click(element, 'fi-gl-period-edit-21');
      expect(view.form.getRawValue()).toEqual({ startDate: '2026-11-01', endDate: '2026-11-30' });
      view.save();
      const put = http.expectOne(isWrite('PUT', `${API}/21`));
      expect(put.request.body).toEqual({ version: 1, startDate: '2026-11-01', endDate: '2026-11-30' });
      put.flush(result(open, false));
      http.expectOne(isList).flush(page([open, closed]));
      fixture.detectChanges();
      expect(view.notice()?.type).toBe('warning');
      expect(text('fi-gl-period-notice')).toContain('No change was saved');
    });

    it('FI-GL-UI-011/016 offers Close only on Open rows and Reopen only on Closed rows, each after confirmation', () => {
      const { view, http, fixture, element, text } = create();
      expect(element.querySelector('[data-testid="fi-gl-period-close-21"]')).not.toBeNull();
      expect(element.querySelector('[data-testid="fi-gl-period-reopen-21"]')).toBeNull();
      expect(element.querySelector('[data-testid="fi-gl-period-close-20"]')).toBeNull();
      click(element, 'fi-gl-period-close-21');
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'fi.glPeriod.closeConfirm', danger: true }));
      const close = http.expectOne(isWrite('POST', `${API}/21/close`));
      expect(close.request.body).toEqual({ version: 1 });
      close.flush(result({ ...open, status: 'Closed', version: 2 }));
      http.expectOne(isList).flush(page([open, closed]));
      fixture.detectChanges();
      expect(view.notice()?.type).toBe('success');
      expect(text('fi-gl-period-notice')).toContain('now Closed');
      click(element, 'fi-gl-period-reopen-20');
      http.expectOne(isWrite('POST', `${API}/20/reopen`)).flush(result({ ...closed, status: 'Open' }, false));
      http.expectOne(isList).flush(page([open, closed]));
      fixture.detectChanges();
      expect(view.notice()?.type).toBe('warning');
      expect(text('fi-gl-period-notice')).toContain('already Open');
    });

    it('FI-GL-UI-011 a declined confirmation sends nothing', () => {
      const { view, http } = create();
      confirm.mockReturnValueOnce(of(false));
      view.confirmDelete(open as never);
      http.expectNone(r => r.method === 'DELETE');
      expect(view.notice()).toBeNull();
    });

    it('FI-GL-UI-015 each action follows its own permission', () => {
      granted = new Set(['GL.PERIOD.VIEW', 'GL.PERIOD.CLOSE']);
      const { element } = create();
      expect(element.querySelector('[data-testid="fi-gl-period-close-21"]')).not.toBeNull();
      for (const id of ['fi-gl-period-create', 'fi-gl-period-edit-21', 'fi-gl-period-reopen-20', 'fi-gl-period-delete-21'])
        expect(element.querySelector(`[data-testid="${id}"]`)).toBeNull();
    });

    it('FI-GL-UI-013 overlap keeps the dialog and entered dates; stale version closes it, explains and re-reads', () => {
      const { view, http, fixture, text } = create();
      view.openCreate();
      view.form.setValue({ startDate: '2026-11-15', endDate: '2026-12-15' });
      view.save();
      http.expectOne(isWrite('POST', API)).flush({ errorCode: 'FI.GL_PERIOD.OVERLAP' }, { status: 409, statusText: 'Conflict' });
      expect(view.dialogOpen()).toBe(true);
      expect(view.saveError()).toBe('errors.FI.GL_PERIOD.OVERLAP');
      expect(view.form.getRawValue().startDate).toBe('2026-11-15');
      view.closeDialog();
      view.openEdit(open as never);
      view.form.setValue({ startDate: '2026-11-01', endDate: '2026-11-29' });
      view.save();
      http.expectOne(isWrite('PUT', `${API}/21`)).flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
      http.expectOne(isList).flush(page([{ ...open, version: 2 }]));
      fixture.detectChanges();
      expect(view.dialogOpen()).toBe(false);
      expect(text('fi-gl-period-action-error')).toContain('Someone else changed this record');
      expect(text('fi-gl-period-version-21')).toBe('2');
    });

    it('FI-GL-UI-013 a second save while the first is pending sends one request', () => {
      const { view, http } = create();
      view.openCreate();
      view.form.setValue({ startDate: '2027-01-01', endDate: '2027-01-31' });
      view.save(); view.save();
      http.expectOne(isWrite('POST', API)).flush(result({ ...open, id: 30, startDate: '2027-01-01', endDate: '2027-01-31' }));
      http.expectOne(isList).flush(page([open]));
    });

    it('FI-GL-UI-016/017 Posted history: moving dates past a Posted journal stays in the dialog; delete is refused with the reason', () => {
      const { view, http, fixture, text } = create();
      view.openEdit(open as never);
      view.form.setValue({ startDate: '2026-11-10', endDate: '2026-11-30' });
      view.save();
      http.expectOne(isWrite('PUT', `${API}/21`)).flush({ errorCode: 'FI.GL_PERIOD.EXCLUDES_POSTED' }, { status: 422, statusText: 'Unprocessable' });
      expect(view.dialogOpen()).toBe(true);
      expect(view.saveError()).toBe('errors.FI.GL_PERIOD.EXCLUDES_POSTED');
      view.closeDialog();
      view.confirmDelete(closed as never);
      const remove = http.expectOne(isWrite('DELETE', `${API}/20`));
      expect(remove.request.params.get('version')).toBe('3');
      remove.flush({ errorCode: 'FI.GL_PERIOD.CONTAINS_POSTED' }, { status: 422, statusText: 'Unprocessable' });
      fixture.detectChanges();
      expect(text('fi-gl-period-action-error')).toContain('Posted');
      expect(text('fi-gl-period-action-target')).toBe('Period 2026-10-01 – 2026-10-31:');
      expect(view.notice()).toBeNull();
    });

    it('FI-GL-UI-011 delete success announces the removed dates and re-reads', () => {
      const { view, http, fixture, text } = create();
      view.confirmDelete(open as never);
      http.expectOne(isWrite('DELETE', `${API}/21`)).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne(isList).flush(page([closed]));
      fixture.detectChanges();
      expect(view.notice()?.type).toBe('success');
      expect(text('fi-gl-period-notice')).toContain('2026-11-01');
      expect(text('fi-gl-period-row-21')).toBeNull();
    });

    it('FI-GL-UI-018 500 keeps the dialog with the trace ID; a missing period closes it and re-reads; network loss is explained', () => {
      const { view, http } = create();
      view.openEdit(open as never);
      view.save();
      http.expectOne(isWrite('PUT', `${API}/21`)).flush({ errorCode: 'X', traceId: 'trace-gl-write' }, { status: 500, statusText: 'Server Error' });
      expect(view.dialogOpen()).toBe(true);
      expect(view.saveError()).toBe('errors.http.unknown');
      expect(view.saveTraceId()).toBe('trace-gl-write');
      view.save();
      http.expectOne(isWrite('PUT', `${API}/21`)).flush({ errorCode: 'FI.GL_PERIOD.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
      http.expectOne(isList).flush(page([closed]));
      expect(view.dialogOpen()).toBe(false);
      expect(view.actionError()).toBe('errors.FI.GL_PERIOD.NOT_FOUND');
      view.confirmClose(open as never);
      http.expectOne(isWrite('POST', `${API}/21/close`)).error(new ProgressEvent('error'), { status: 0 });
      expect(view.actionError()).toBe('errors.http.network');
    });

    it('FI-GL-UI-019 cancelling a changed dialog asks before discarding; an unchanged dialog closes at once', () => {
      const { view } = create();
      view.openCreate();
      view.requestCloseDialog();
      expect(confirm).not.toHaveBeenCalled();
      expect(view.dialogOpen()).toBe(false);
      view.openCreate();
      view.form.controls.startDate.setValue('2027-01-01'); view.form.markAsDirty();
      expect(view.hasUnsavedChanges()).toBe(true);
      confirm.mockReturnValueOnce(of(false));
      view.requestCloseDialog();
      expect(view.dialogOpen()).toBe(true);
      view.requestCloseDialog();
      expect(view.dialogOpen()).toBe(false);
    });
  });
});
