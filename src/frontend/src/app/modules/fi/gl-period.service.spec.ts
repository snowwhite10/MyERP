import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GlPeriodService } from './gl-period.service';

const API = '/api/v1/gl/periods';

describe('FI-GL-UI period service', () => {
  let api: GlPeriodService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(GlPeriodService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('FI-GL-UI-002 sends only supported filters and never a keyword', () => {
    let count = -1;
    api.list({ page: 3, pageSize: 50, status: 'Closed', from: '2026-01-31', to: null, sort: 'endDate' })
      .subscribe(result => count = result.totalCount);
    const request = http.expectOne(r => r.url === API && r.method === 'GET');
    expect(request.request.params.keys().sort()).toEqual(['from', 'page', 'pageSize', 'sort', 'status']);
    expect(request.request.params.get('from')).toBe('2026-01-31');
    request.flush({ items: [], totalCount: 0, page: 3, pageSize: 50 });
    expect(count).toBe(0);
  });

  it('FI-GL-UI-007 maps known codes, validation, network and unknown failures to message keys', () => {
    const failure = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
    expect(api.errorKey(failure(403, 'COMMON.PERMISSION.DENIED'))).toBe('errors.COMMON.PERMISSION.DENIED');
    expect(api.errorKey(failure(404, 'FI.GL_PERIOD.NOT_FOUND'))).toBe('errors.FI.GL_PERIOD.NOT_FOUND');
    expect(api.errorKey(failure(400))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    expect(api.errorKey(failure(0))).toBe('errors.http.network');
    expect(api.errorKey(failure(500, 'SOMETHING.ELSE'))).toBe('errors.http.unknown');
  });
});

describe('FI-GL-UI period write service (interface 6.21)', () => {
  let api: GlPeriodService;
  let http: HttpTestingController;
  const saved = { id: 21, startDate: '2026-11-01', endDate: '2026-11-30', status: 'Open', version: 2,
    createdAt: '2026-10-20T09:15:00', createdBy: 1, updatedAt: null, updatedBy: null };
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(GlPeriodService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('FI-GL-UI-010 creates with dates only; the server decides company and Open state', () => {
    let changed: boolean | undefined;
    api.create({ startDate: '2026-12-01', endDate: '2026-12-31' }).subscribe(result => changed = result.changed);
    const request = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(request.request.body).toEqual({ startDate: '2026-12-01', endDate: '2026-12-31' });
    request.flush({ period: saved, changed: true });
    expect(changed).toBe(true);
  });

  it('FI-GL-UI-011 date edit sends version and dates but never a status field', () => {
    api.update(21, 4, { startDate: '2026-11-02', endDate: '2026-11-30' }).subscribe();
    const request = http.expectOne(r => r.url === `${API}/21` && r.method === 'PUT');
    expect(request.request.body).toEqual({ version: 4, startDate: '2026-11-02', endDate: '2026-11-30' });
    expect(Object.keys(request.request.body as object)).not.toContain('status');
    request.flush({ period: saved, changed: false });
  });

  it('FI-GL-UI-011 close, reopen and delete use their own routes with the held version', () => {
    api.close(21, 4).subscribe();
    expect(http.expectOne(r => r.url === `${API}/21/close` && r.method === 'POST').request.body).toEqual({ version: 4 });
    api.reopen(20, 3).subscribe();
    expect(http.expectOne(r => r.url === `${API}/20/reopen` && r.method === 'POST').request.body).toEqual({ version: 3 });
    api.remove(22, 2).subscribe();
    const remove = http.expectOne(r => r.url === `${API}/22` && r.method === 'DELETE');
    expect(remove.request.params.get('version')).toBe('2');
    remove.flush(null, { status: 204, statusText: 'No Content' });
    http.match(() => true).forEach(request => request.flush({ period: saved, changed: true }));
  });

  it('FI-GL-UI-013/016/018 maps overlap, stale version, Posted protection and missing period codes', () => {
    const failure = (status: number, errorCode: string) => new HttpErrorResponse({ status, error: { errorCode } });
    expect(api.errorKey(failure(409, 'FI.GL_PERIOD.OVERLAP'))).toBe('errors.FI.GL_PERIOD.OVERLAP');
    expect(api.errorKey(failure(409, 'COMMON.CONCURRENCY.CONFLICT'))).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    expect(api.errorKey(failure(422, 'FI.GL_PERIOD.EXCLUDES_POSTED'))).toBe('errors.FI.GL_PERIOD.EXCLUDES_POSTED');
    expect(api.errorKey(failure(422, 'FI.GL_PERIOD.CONTAINS_POSTED'))).toBe('errors.FI.GL_PERIOD.CONTAINS_POSTED');
    expect(api.errorKey(failure(400, 'COMMON.REQUEST.VALIDATION_FAILED'))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
  });
});
