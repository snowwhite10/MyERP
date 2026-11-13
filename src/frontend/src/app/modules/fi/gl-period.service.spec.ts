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
