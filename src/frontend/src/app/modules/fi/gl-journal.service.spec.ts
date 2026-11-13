import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GlJournalDetail, GlJournalService } from './gl-journal.service';

const API = '/api/v1/gl/journals';

describe('FI-GL-JNL-UI journal service', () => {
  let api: GlJournalService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(GlJournalService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('FI-GL-JNL-UI-002 sends only supported filters and keeps exact amount text in the list', () => {
    let first: string | undefined;
    api.list({ page: 2, pageSize: 50, periodId: 21, from: '2026-11-01', to: '2026-11-30', sourceModule: 'SD', sourceType: 'Invoice',
      sourceId: 9, accountCode: '1100', sort: 'id' }).subscribe(result => first = result.items[0].totalDebit);
    const request = http.expectOne(r => r.url === API && r.method === 'GET');
    expect(request.request.params.keys().sort()).toEqual(['accountCode', 'from', 'page', 'pageSize', 'periodId', 'sort',
      'sourceId', 'sourceModule', 'sourceType', 'to']);
    expect(request.request.responseType).toBe('text');
    request.flush('{"items":[{"id":1,"totalDebit":123456789012345.6789,"totalCredit":123456789012345.6789}],"totalCount":1,"page":2,"pageSize":50}');
    expect(first).toBe('123456789012345.6789');
  });

  it('FI-GL-JNL-UI-002 omits empty filters', () => {
    api.list({ page: 1, pageSize: 20, periodId: null, from: '', sourceModule: null }).subscribe();
    const request = http.expectOne(r => r.url === API);
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    request.flush('{"items":[],"totalCount":0,"page":1,"pageSize":20}');
  });

  it('FI-GL-JNL-UI-001 reads one journal with header and line amounts as text', () => {
    let detail: GlJournalDetail | undefined;
    api.get(7).subscribe(value => detail = value);
    http.expectOne(`${API}/7`).flush('{"header":{"id":7,"totalDebit":100.0000,"totalCredit":100.0000},"lines":[' +
      '{"sequence":1,"debit":100.0000,"credit":0.0000},{"sequence":2,"debit":0.0000,"credit":100.0000}]}');
    expect(detail!.header.totalCredit).toBe('100.0000');
    expect(detail!.lines.map(line => [line.debit, line.credit])).toEqual([['100.0000', '0.0000'], ['0.0000', '100.0000']]);
  });

  it('FI-GL-JNL-UI-007 restores JSON error bodies of text requests and maps known codes', () => {
    let key = '';
    api.get(8).subscribe({ error: failure => key = api.errorKey(failure) });
    http.expectOne(`${API}/8`).flush('{"errorCode":"FI.GL_JOURNAL.NOT_FOUND"}', { status: 404, statusText: 'Not Found' });
    expect(key).toBe('errors.FI.GL_JOURNAL.NOT_FOUND');
    const failure = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });
    expect(api.errorKey(failure(403, 'COMMON.PERMISSION.DENIED'))).toBe('errors.COMMON.PERMISSION.DENIED');
    expect(api.errorKey(failure(400))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    expect(api.errorKey(failure(0))).toBe('errors.http.network');
    expect(api.errorKey(failure(500))).toBe('errors.http.unknown');
  });
});
