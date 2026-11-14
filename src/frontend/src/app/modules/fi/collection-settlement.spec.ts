import { HttpErrorResponse, HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import enErrors from '../../../../public/i18n/en/errors.json';
import enFi from '../../../../public/i18n/en/fi.json';
import enLayout from '../../../../public/i18n/en/layout.json';
import hansErrors from '../../../../public/i18n/zh-Hans/errors.json';
import hansFi from '../../../../public/i18n/zh-Hans/fi.json';
import hansLayout from '../../../../public/i18n/zh-Hans/layout.json';
import hantErrors from '../../../../public/i18n/zh-Hant/errors.json';
import hantFi from '../../../../public/i18n/zh-Hant/fi.json';
import hantLayout from '../../../../public/i18n/zh-Hant/layout.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { CollectionSettlementPage } from './collection-settlement-page';
import {
  COLLECTION_SETTLEMENT_ERROR_CODES, SETTLEMENT_LINE_REASONS, settlementLineErrors, settlementLineReasonKey,
} from './collection-settlement.service';

const API = '/api/v1/ar/collection-settlements';
const row = (id: number, status: string, over: object = {}) => ({ id, documentNo: `ST2026110${id}`, customerId: 7, businessDate: '2026-11-14',
  currency: 'TWD', receiptId: 30 + id, receiptDocumentNo: `RC2026110${id}`, totalAmount: 500.25, lineCount: 2, status, version: 1,
  customerCode: 'C001', customerName: 'Acme', ...over });
const page = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });
const lineError = (status: number, lineErrors: unknown) => new HttpErrorResponse({ status,
  error: { status, errorCode: 'FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID', lineErrors } });

describe('FI-SETL-UI collection settlement rules', () => {
  it('FI-SETL-UI-U01 422 lineErrors are read in line order; malformed entries, other statuses and missing arrays give none', () => {
    expect(settlementLineErrors(lineError(422, [
      { line: 3, receivableId: 12, reason: 'FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN' },
      { line: 1, receivableId: 10, reason: 'FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_NOT_FOUND' },
      { line: 'x', receivableId: 11, reason: 'A' }, null, { line: 2, receivableId: 11 },
    ]))).toEqual([
      { line: 1, receivableId: 10, reason: 'FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_NOT_FOUND' },
      { line: 3, receivableId: 12, reason: 'FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN' },
    ]);
    expect(settlementLineErrors(lineError(409, [{ line: 1, receivableId: 1, reason: 'r' }]))).toEqual([]);
    expect(settlementLineErrors(lineError(422, 'nope'))).toEqual([]);
    expect(settlementLineErrors(new Error('x'))).toEqual([]);
  });

  it('FI-SETL-UI-U02 known line reasons use their error text; an unknown reason falls back to the generic line text', () => {
    expect(settlementLineReasonKey('FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN')).toBe('errors.FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN');
    expect(settlementLineReasonKey('FI.COLLECTION_WRITEOFF.LINE.SOMETHING_NEW')).toBe('fi.collectionSettlement.lineReasonUnknown');
  });

  it('FI-SETL-UI-U03 every whitelisted code, line reason, menu label and screen text exists in all three languages', () => {
    const at = (tree: unknown, path: string) => path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown>)?.[key], tree);
    for (const errors of [enErrors, hantErrors, hansErrors]) {
      for (const code of [...COLLECTION_SETTLEMENT_ERROR_CODES, ...SETTLEMENT_LINE_REASONS]) expect(typeof at(errors, code), code).toBe('string');
    }
    for (const layout of [enLayout, hantLayout, hansLayout]) expect(typeof layout.menu.collectionSettlements).toBe('string');
    const keys = (node: object, prefix = ''): string[] => Object.entries(node).flatMap(([k, v]) =>
      typeof v === 'object' ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]);
    const en = keys(enFi.collectionSettlement).sort();
    expect(keys(hantFi.collectionSettlement).sort()).toEqual(en);
    expect(keys(hansFi.collectionSettlement).sort()).toEqual(en);
  });
});

describe('FI-SETL-UI collection settlement list', () => {
  let granted: Set<string>;
  const company = signal<{ id: number } | null>({ id: 1 });
  let confirm: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    granted = new Set(['AR.SETTLEMENT.VIEW', 'AR.SETTLEMENT.CREATE', 'AR.SETTLEMENT.EDIT', 'AR.SETTLEMENT.DELETE', 'AR.SETTLEMENT.SUBMIT']);
    company.set({ id: 1 });
    confirm = vi.fn().mockReturnValue(of(true));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code), hasAny: (codes: string[]) => codes.some(c => granted.has(c)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const seeded = () => page(row(1, 'Draft'), row(2, 'Pending'), row(3, 'Posted', { totalAmount: 'BIG', receiptDocumentNo: null }), row(4, 'Rejected'))
    .replace('"totalAmount":"BIG"', '"totalAmount":999999999999999.9999');
  function create(body = seeded()) {
    const fixture = TestBed.createComponent(CollectionSettlementPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, http, element, by, text, view: fixture.componentInstance };
  }

  it('FI-SETL-UI-001 rows show exact totals, the collection number (or ID) and only the actions each state allows', () => {
    const { by, text } = create();
    expect(text('fi-settle-total-3')).toBe('999,999,999,999,999.9999 TWD');
    expect(text('fi-settle-receipt-1')).toBe('RC20261101');
    expect(text('fi-settle-receipt-3')).toBe('#33');
    expect(text('fi-settle-status-2')).toBe('Pending approval');
    for (const id of ['fi-settle-submit-1', 'fi-settle-delete-1', 'fi-settle-withdraw-2', 'fi-settle-delete-4'])
      expect(by(id), id).not.toBeNull();
    for (const id of ['fi-settle-submit-2', 'fi-settle-delete-2', 'fi-settle-submit-3', 'fi-settle-withdraw-3', 'fi-settle-delete-3',
      'fi-settle-submit-4', 'fi-settle-withdraw-4'])
      expect(by(id), id).toBeNull();
  });

  it('FI-SETL-UI-002 view-only users see no row actions; receipt permissions grant nothing here', () => {
    granted = new Set(['AR.SETTLEMENT.VIEW', 'AR.COLLECTION.SUBMIT', 'AR.COLLECTION.DELETE']);
    const { element } = create();
    expect(element.querySelectorAll('td.actions button, td.actions a').length).toBe(0);
  });

  it('FI-SETL-UI-003 filters go to the server; empty and search-empty differ; a reversed range is refused without a request', () => {
    const { element, view, fixture, http } = create(page());
    expect(element.textContent).toContain('No settlements yet.');
    view.keyword = ' ST01 '; view.status = 'Posted'; view.from = '2026-11-01'; view.to = '2026-11-30'; view.search();
    const request = http.expectOne(isList);
    expect(['keyword', 'status', 'from', 'to'].map(k => request.request.params.get(k))).toEqual(['ST01', 'Posted', '2026-11-01', '2026-11-30']);
    request.flush(page());
    fixture.detectChanges();
    expect(element.textContent).toContain('No settlements match these filters.');
    view.from = '2026-11-30'; view.to = '2026-11-01'; view.search(); fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-settle-range-error"]')).not.toBeNull();
    http.expectNone(isList);
  });

  it('FI-SETL-UI-004 submit that posts at once states what was applied and what is left unapplied', () => {
    const { by, text, http, fixture } = create();
    by('fi-settle-submit-1')!.click();
    const submit = http.expectOne(r => r.url === `${API}/1/submit`);
    expect(submit.request.body).toEqual({ version: 1 });
    submit.flush('{"id":1,"documentNo":"ST20261101","status":"Posted","version":3,'
      + '"approval":{"instanceId":9,"status":"Approved","currentStepNo":null,"stepCount":0},'
      + '"posting":{"companyId":1,"settlementId":1,"baseCurrency":"TWD","totalAmount":123456789012345.6789,"receiptUnappliedAmount":0.0001,'
      + '"journalId":77,"allocations":[{"receivableId":10,"amount":1,"receivableOpenAmount":0},{"receivableId":11,"amount":2,"receivableOpenAmount":0}]}}');
    http.expectOne(isList).flush(page(row(1, 'Posted', { version: 3 })));
    fixture.detectChanges();
    expect(by('fi-settle-notice')!.getAttribute('data-type')).toBe('success');
    expect(text('fi-settle-notice')).toBe('Settlement ST20261101 was approved and posted: 123,456,789,012,345.6789 TWD applied to 2 receivable(s). '
      + 'Collection RC20261101 has 0.0001 TWD unapplied left.');
  });

  it('FI-SETL-UI-005 submit to a pending approval, withdraw and delete each name the document; delete asks first', () => {
    const { by, text, http, fixture } = create();
    by('fi-settle-submit-1')!.click();
    http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ id: 1, documentNo: 'ST20261101', status: 'Pending', version: 2,
      approval: { instanceId: 9, status: 'Pending', currentStepNo: 1, stepCount: 1 }, posting: null }));
    http.expectOne(isList).flush(page(row(1, 'Pending', { version: 2 })));
    fixture.detectChanges();
    expect(text('fi-settle-notice')).toBe('Settlement ST20261101 was submitted and is waiting for approval. No receivable has been reduced yet.');
    by('fi-settle-withdraw-1')!.click();
    const withdraw = http.expectOne(`${API}/1/withdraw`);
    expect(withdraw.request.body).toEqual({ version: 2 });
    withdraw.flush(JSON.stringify({ id: 1, documentNo: 'ST20261101', status: 'Draft', version: 3,
      approval: { instanceId: 9, status: 'Cancelled', currentStepNo: null, stepCount: 1 }, posting: null }));
    http.expectOne(isList).flush(page(row(1, 'Draft', { version: 3 })));
    fixture.detectChanges();
    expect(text('fi-settle-notice')).toBe('Settlement ST20261101 was withdrawn and is a draft again.');
    by('fi-settle-delete-1')!.click();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true, params: { no: 'ST20261101' } }));
    const remove = http.expectOne(r => r.url === `${API}/1` && r.method === 'DELETE');
    expect(remove.request.params.get('version')).toBe('3');
    remove.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(isList).flush(page());
    fixture.detectChanges();
    expect(text('fi-settle-notice')).toBe('Settlement ST20261101 was deleted.');
  });

  it('FI-SETL-UI-006 a cancelled delete sends nothing', () => {
    confirm.mockReturnValue(of(false));
    const { by, http } = create();
    by('fi-settle-delete-4')!.click();
    http.expectNone(r => r.method === 'DELETE');
  });

  it('FI-SETL-UI-007 a refused submit names every bad line and its reason, shows no success and re-reads the list', () => {
    const { by, text, http, fixture } = create();
    by('fi-settle-submit-1')!.click();
    http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ status: 422, errorCode: 'FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID', lineErrors: [
      { line: 2, receivableId: 11, reason: 'FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN' },
      { line: 1, receivableId: 10, reason: 'FI.COLLECTION_WRITEOFF.LINE.RECEIVABLE_AFTER_SETTLEMENT' }] }),
    { status: 422, statusText: 'Unprocessable' });
    http.expectOne(isList).flush(seeded());
    fixture.detectChanges();
    expect(by('fi-settle-notice')).toBeNull();
    expect(text('fi-settle-error')).toContain('Some lines cannot be settled. Nothing was settled');
    expect(text('fi-settle-line-error-1')).toBe('Line 1 (receivable #10): the receivable was posted after the settlement date.');
    expect(text('fi-settle-line-error-2')).toContain('more than the receivable\'s current open amount');
    // The next action clears the old line reasons.
    by('fi-settle-submit-1')!.click();
    http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ status: 422, errorCode: 'FI.COLLECTION_WRITEOFF.RECEIPT_INSUFFICIENT' }),
      { status: 422, statusText: 'Unprocessable' });
    http.expectOne(isList).flush(seeded());
    fixture.detectChanges();
    expect(text('fi-settle-error')).toContain('does not have enough unapplied cash');
    expect(by('fi-settle-line-errors')).toBeNull();
  });

  it('FI-SETL-UI-008 a stale row (409) and a status change (422) show the reason and the current list', () => {
    const { by, text, http, fixture } = create();
    by('fi-settle-delete-4')!.click();
    http.expectOne(r => r.url === `${API}/4` && r.method === 'DELETE')
      .flush({ status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(isList).flush(seeded());
    fixture.detectChanges();
    expect(text('fi-settle-error')).toContain('Someone else changed this record');
    by('fi-settle-submit-1')!.click();
    http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ status: 422, errorCode: 'FI.COLLECTION.SETTLEMENT_INVALID_STATUS' }),
      { status: 422, statusText: 'Unprocessable' });
    http.expectOne(isList).flush(page(row(1, 'Pending', { version: 2 })));
    fixture.detectChanges();
    expect(text('fi-settle-error')).toContain('Only draft or rejected settlements can be changed or deleted');
    expect(text('fi-settle-status-1')).toBe('Pending approval');
  });

  it('FI-SETL-UI-009 a 500 on read shows the trace ID; the detail shows lines, open amounts and the receipt balance', () => {
    const { by, text, http, fixture, view } = create();
    view.search();
    http.expectOne(isList).flush(JSON.stringify({ traceId: 'trace-settle-500' }), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('fi-settle-error')).toContain('trace-settle-500');
    expect(text('fi-settle-table')).toContain('The list could not be loaded');
    expect(text('fi-settle-table')).not.toContain('No settlements yet.');
    view.search();
    http.expectOne(isList).flush(page(row(3, 'Posted')));
    fixture.detectChanges();
    expect(by('fi-settle-error')).toBeNull();
    by('fi-settle-view-3')!.click();
    http.expectOne(`${API}/3`).flush('{"id":3,"documentNo":"ST20261103","customerId":7,"businessDate":"2026-11-14","currency":"TWD",'
      + '"receiptId":33,"receiptDocumentNo":"RC20261103","receiptUnappliedAmount":0.5,"totalAmount":999999999999999.9999,"remark":null,'
      + '"status":"Posted","version":3,"createdAt":"2026-11-14T09:00:00","createdBy":1,"updatedAt":null,"updatedBy":null,'
      + '"customerCode":"C001","customerName":"Acme","lines":[{"lineNo":1,"receivableId":10,"amount":999999999999999.9998,"receivableOpenAmount":0},'
      + '{"lineNo":2,"receivableId":11,"amount":0.0001,"receivableOpenAmount":12.3}]}');
    fixture.detectChanges();
    expect(text('fi-settle-state-hint')).toContain('applied to the receivables');
    expect(text('fi-settle-detail-total')).toBe('999,999,999,999,999.9999 TWD');
    expect(text('fi-settle-detail-unapplied')).toBe('0.5 TWD');
    expect(text('fi-settle-line-amount-1')).toBe('999,999,999,999,999.9998');
    expect(text('fi-settle-line-open-1')).toBe('0');
    expect(text('fi-settle-line-open-2')).toBe('12.3');
    by('fi-settle-detail-close')!.click(); fixture.detectChanges();
    expect(by('fi-settle-detail')).toBeNull();
  });

  it('FI-SETL-UI-010 a detail for a deleted settlement (404) shows the reason and re-reads the list', () => {
    const { by, text, http, fixture } = create();
    by('fi-settle-view-1')!.click();
    http.expectOne(`${API}/1`).flush(JSON.stringify({ status: 404, errorCode: 'FI.COLLECTION.SETTLEMENT_NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    http.expectOne(isList).flush(page());
    fixture.detectChanges();
    expect(text('fi-settle-error')).toContain('This settlement no longer exists');
    expect(by('fi-settle-detail')).toBeNull();
  });

  it('FI-SETL-UI-011 a receipt not yet posted shows so in the detail; company switch reloads the list and drops the detail', () => {
    const { by, text, http, fixture, view } = create();
    by('fi-settle-view-1')!.click();
    http.expectOne(`${API}/1`).flush(JSON.stringify({ ...row(1, 'Draft'), receiptUnappliedAmount: null, remark: 'r', createdAt: '2026-11-14T09:00:00',
      createdBy: 1, updatedAt: null, updatedBy: null, lines: [] }));
    fixture.detectChanges();
    expect(text('fi-settle-detail-unapplied')).toBe('Collection not posted');
    company.set({ id: 2 });
    TestBed.tick();
    http.expectOne(isList).flush(page());
    fixture.detectChanges();
    expect(view.detail()).toBeNull();
  });
});
