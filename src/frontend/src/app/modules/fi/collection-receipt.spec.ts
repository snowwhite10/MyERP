import { HttpErrorResponse, HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import enErrors from '../../../../public/i18n/en/errors.json';
import enFi from '../../../../public/i18n/en/fi.json';
import hansErrors from '../../../../public/i18n/zh-Hans/errors.json';
import hansFi from '../../../../public/i18n/zh-Hans/fi.json';
import hantErrors from '../../../../public/i18n/zh-Hant/errors.json';
import hantFi from '../../../../public/i18n/zh-Hant/fi.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { CollectionReceiptForm } from './collection-receipt-form';
import { CollectionReceiptPage } from './collection-receipt-page';
import {
  COLLECTION_RECEIPT_ERROR_CODES, CollectionReceiptDraft, receiptDraftIssues, receiptFieldErrors, receiptJson,
} from './collection-receipt.service';

const API = '/api/v1/ar/sales-collections';
const draft = (over: Partial<CollectionReceiptDraft> = {}): CollectionReceiptDraft => ({ customerId: 7, businessDate: '2026-11-14',
  amount: '1200.5', bankAccountId: 3, bankReference: ' TT-001 ', remark: '', ...over });
const row = (id: number, status: string, over: object = {}) => ({ id, documentNo: `RC2026110${id}`, customerId: 7, businessDate: '2026-11-14',
  currency: 'TWD', amount: 1200.5, bankAccountId: 3, bankReference: `TT-00${id}`, status, version: 1, unappliedAmount: null,
  customerCode: 'C001', customerName: 'Acme', bankAccountCode: 'BK01', bankAccountName: 'Mega TWD', ...over });
/** Response text exactly as the server writes it (amount digits beyond a JavaScript double). */
const page = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });

describe('FI-COLL-UI collection receipt rules', () => {
  it('FI-COLL-UI-U01 required fields, amount format and positive, reference length and control characters, remark length', () => {
    expect(receiptDraftIssues(draft())).toEqual({});
    expect(receiptDraftIssues(draft({ customerId: null, businessDate: '', amount: ' ', bankAccountId: null, bankReference: '  ' })))
      .toEqual({ customerId: 'required', businessDate: 'required', amount: 'required', bankAccountId: 'required', bankReference: 'required' });
    expect(receiptDraftIssues(draft({ amount: '0.0000' })).amount).toBe('amountPositive');
    expect(receiptDraftIssues(draft({ amount: '1.12345' })).amount).toBe('amountFormat');
    expect(receiptDraftIssues(draft({ amount: '-5' })).amount).toBe('amountFormat');
    expect(receiptDraftIssues(draft({ amount: '1234567890123456' })).amount).toBe('amountFormat');
    expect(receiptDraftIssues(draft({ amount: '999,999,999,999,999.9999' })).amount).toBeUndefined();
    expect(receiptDraftIssues(draft({ bankReference: 'x'.repeat(50) })).bankReference).toBeUndefined();
    expect(receiptDraftIssues(draft({ bankReference: 'x'.repeat(51) })).bankReference).toBe('tooLong');
    expect(receiptDraftIssues(draft({ bankReference: 'TT\n01' })).bankReference).toBe('control');
    expect(receiptDraftIssues(draft({ remark: 'r'.repeat(501) })).remark).toBe('tooLong');
  });

  it('FI-COLL-UI-U02 request JSON keeps every amount digit, trims the reference and sends a blank remark as null', () => {
    const json = receiptJson(draft({ amount: '999,999,999,999,999.9999' }), 4);
    expect(json).toContain('"amount":999999999999999.9999}');
    expect(JSON.parse(json)).toMatchObject({ version: 4, customerId: 7, bankAccountId: 3, bankReference: 'TT-001', remark: null });
    expect(() => receiptJson(draft({ amount: '1e3' }))).toThrow();
  });

  it('FI-COLL-UI-U03 400 field names map to form fields case-insensitively; other statuses map to none', () => {
    expect(receiptFieldErrors(new HttpErrorResponse({ status: 400, error: { errors: { Amount: ['x'], '$.bankReference': ['y'] } } })))
      .toEqual(['amount', 'bankReference']);
    expect(receiptFieldErrors(new HttpErrorResponse({ status: 422, error: { errors: { amount: ['x'] } } }))).toEqual([]);
  });

  it('FI-COLL-UI-U04 every whitelisted error code and screen text exists in all three languages', () => {
    const at = (tree: unknown, path: string) => path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown>)?.[key], tree);
    for (const errors of [enErrors, hantErrors, hansErrors]) {
      for (const code of COLLECTION_RECEIPT_ERROR_CODES) expect(typeof at(errors, code), code).toBe('string');
    }
    const keys = (node: object, prefix = ''): string[] => Object.entries(node).flatMap(([k, v]) =>
      typeof v === 'object' ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]);
    const en = keys(enFi.collectionReceipt).sort();
    expect(keys(hantFi.collectionReceipt).sort()).toEqual(en);
    expect(keys(hansFi.collectionReceipt).sort()).toEqual(en);
  });
});

describe('FI-COLL-UI collection receipt list', () => {
  let granted: Set<string>;
  const company = signal<{ id: number } | null>({ id: 1 });
  let confirm: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    granted = new Set(['AR.COLLECTION.VIEW', 'AR.COLLECTION.CREATE', 'AR.COLLECTION.EDIT', 'AR.COLLECTION.DELETE', 'AR.COLLECTION.SUBMIT']);
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
  function create(body = page(row(1, 'Draft'), row(2, 'Pending'), row(3, 'Posted', { amount: 'BIG', unappliedAmount: 99.5 }), row(4, 'Rejected')).replace('"amount":"BIG"', '"amount":999999999999999.9999')) {
    const fixture = TestBed.createComponent(CollectionReceiptPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.trim() ?? null;
    return { fixture, http, element, by, text, view: fixture.componentInstance };
  }

  it('FI-COLL-UI-001 rows show exact amounts, unapplied only once posted, and only the actions each state allows', () => {
    const { by, text } = create();
    expect(text('fi-receipt-amount-3')).toBe('999,999,999,999,999.9999 TWD');
    expect(text('fi-receipt-unapplied-3')).toBe('99.5');
    expect(text('fi-receipt-unapplied-1')).toBe('—');
    expect(text('fi-receipt-status-2')).toBe('Pending approval');
    for (const id of ['fi-receipt-edit-1', 'fi-receipt-submit-1', 'fi-receipt-delete-1', 'fi-receipt-withdraw-2', 'fi-receipt-edit-4', 'fi-receipt-delete-4'])
      expect(by(id), id).not.toBeNull();
    for (const id of ['fi-receipt-submit-2', 'fi-receipt-edit-2', 'fi-receipt-delete-2', 'fi-receipt-edit-3', 'fi-receipt-submit-3',
      'fi-receipt-withdraw-3', 'fi-receipt-delete-3', 'fi-receipt-submit-4'])
      expect(by(id), id).toBeNull();
  });

  it('FI-COLL-UI-002 view-only users see no create or row actions', () => {
    granted = new Set(['AR.COLLECTION.VIEW']);
    const { by, element } = create();
    expect(by('fi-receipt-new')).toBeNull();
    expect(element.querySelectorAll('td.actions button, td.actions a').length).toBe(0);
  });

  it('FI-COLL-UI-003 empty list and search-empty have different messages; a reversed range is refused without a request', () => {
    const { element, view, fixture, http } = create(page());
    expect(element.textContent).toContain('No collections yet.');
    view.status = 'Posted'; view.search();
    http.expectOne(r => isList(r) && r.params.get('status') === 'Posted').flush(page());
    fixture.detectChanges();
    expect(element.textContent).toContain('No collections match these filters.');
    view.from = '2026-11-30'; view.to = '2026-11-01'; view.search(); fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-receipt-range-error"]')).not.toBeNull();
    http.expectNone(isList);
  });

  it('FI-COLL-UI-004 submit that posts at once says unapplied cash was posted, not that an invoice was paid', () => {
    const { by, text, http, fixture } = create();
    by('fi-receipt-submit-1')!.click();
    http.expectOne(r => r.url === `${API}/1/submit`).flush(JSON.stringify({ id: 1, documentNo: 'RC20261101', status: 'Posted', version: 3,
      approval: { instanceId: 9, status: 'Approved', currentStepNo: null, stepCount: 0 },
      posting: { amount: 1200.5, unappliedAmount: 1200.5, journalId: 77, baseCurrency: 'TWD' } }));
    http.expectOne(isList).flush(page(row(1, 'Posted', { unappliedAmount: 1200.5, version: 3 })));
    fixture.detectChanges();
    expect(by('fi-receipt-notice')!.getAttribute('data-type')).toBe('success');
    expect(text('fi-receipt-notice')).toContain('approved and posted: unapplied cash 1,200.5 TWD');
    expect(text('fi-receipt-notice')).toContain('No invoice has been settled yet');
    expect(text('fi-receipt-unapplied-1')).toBe('1,200.5');
  });

  it('FI-COLL-UI-005 submit to a pending approval, withdraw and delete each name the document; delete asks first', () => {
    const { by, text, http, fixture } = create();
    by('fi-receipt-submit-1')!.click();
    http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ id: 1, documentNo: 'RC20261101', status: 'Pending', version: 2,
      approval: { instanceId: 9, status: 'Pending', currentStepNo: 1, stepCount: 1 }, posting: null }));
    http.expectOne(isList).flush(page(row(1, 'Pending', { version: 2 })));
    fixture.detectChanges();
    expect(text('fi-receipt-notice')).toBe('Collection RC20261101 was submitted and is waiting for approval. Nothing has been posted yet.');
    by('fi-receipt-withdraw-1')!.click();
    const withdraw = http.expectOne(`${API}/1/withdraw`);
    expect(withdraw.request.body).toEqual({ version: 2 });
    withdraw.flush(JSON.stringify({ id: 1, documentNo: 'RC20261101', status: 'Draft', version: 3,
      approval: { instanceId: 9, status: 'Cancelled', currentStepNo: null, stepCount: 1 }, posting: null }));
    http.expectOne(isList).flush(page(row(1, 'Draft', { version: 3 })));
    fixture.detectChanges();
    expect(text('fi-receipt-notice')).toContain('withdrawn and is a draft again');
    by('fi-receipt-delete-1')!.click();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true, params: { no: 'RC20261101' } }));
    const remove = http.expectOne(r => r.url === `${API}/1` && r.method === 'DELETE');
    expect(remove.request.params.get('version')).toBe('3');
    remove.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(isList).flush(page());
    fixture.detectChanges();
    expect(text('fi-receipt-notice')).toBe('Collection RC20261101 was deleted.');
  });

  it('FI-COLL-UI-006 a stale row (409) and a posting refusal show the reason and the current list, with no success', () => {
    const { by, text, http, fixture } = create();
    by('fi-receipt-submit-1')!.click();
    http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' }), { status: 409, statusText: 'Conflict' });
    http.expectOne(isList).flush(page(row(1, 'Draft', { version: 2 })));
    fixture.detectChanges();
    expect(by('fi-receipt-notice')).toBeNull();
    expect(text('fi-receipt-error')).toContain('Someone else changed this record');
    by('fi-receipt-submit-1')!.click();
    http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ status: 422, errorCode: 'FI.COLLECTION_POSTING.MAPPING_MISSING' }), { status: 422, statusText: 'Unprocessable' });
    http.expectOne(isList).flush(page(row(1, 'Draft', { version: 2 })));
    fixture.detectChanges();
    expect(text('fi-receipt-error')).toContain('Collection accounts are not set up');
  });

  it('FI-COLL-UI-007 a 500 or network loss on read shows the reason and a load-failed table, not an empty list; the detail shows the state hint and journal', () => {
    const { by, text, http, fixture, view } = create();
    view.search();
    http.expectOne(isList).flush(JSON.stringify({ traceId: 'trace-receipt-500' }), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('fi-receipt-error')).toContain('trace-receipt-500');
    expect(text('fi-receipt-table')).toContain('The list could not be loaded');
    expect(text('fi-receipt-table')).not.toContain('No collections yet.');
    view.search();
    http.expectOne(isList).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();
    expect(by('fi-receipt-error')).not.toBeNull();
    expect(text('fi-receipt-table')).toContain('The list could not be loaded');
    expect(text('fi-receipt-table')).not.toContain('No collections yet.');
    view.search();
    http.expectOne(isList).flush(page(row(3, 'Posted', { unappliedAmount: 99.5 })));
    fixture.detectChanges();
    by('fi-receipt-view-3')!.click();
    http.expectOne(`${API}/3`).flush(JSON.stringify({ ...row(3, 'Posted', { unappliedAmount: 99.5 }), remark: null,
      createdAt: '2026-11-14T09:00:00', createdBy: 1, updatedAt: '2026-11-14T10:30:00', updatedBy: 1, journalId: 77 }));
    fixture.detectChanges();
    expect(text('fi-receipt-state-hint')).toContain('Posted as unapplied cash');
    expect(text('fi-receipt-detail-unapplied')).toBe('99.5 TWD');
    expect(text('fi-receipt-detail-journal')).toBe('#77');
  });

  it('FI-COLL-UI-008 company switch reloads the list and drops the detail', () => {
    const { http, fixture, view } = create();
    view.detail.set({ ...row(1, 'Draft'), amount: '1', unappliedAmount: null, remark: null, createdAt: '', createdBy: 1, updatedAt: null,
      updatedBy: null, journalId: null } as never);
    company.set({ id: 2 });
    TestBed.tick();
    http.expectOne(isList).flush(page());
    fixture.detectChanges();
    expect(view.detail()).toBeNull();
  });
});

describe('FI-COLL-UI collection receipt form', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let select: ReturnType<typeof vi.fn>;
  function setup(id: string | null) {
    company.set({ id: 1 });
    select = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }] });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(CollectionReceiptForm);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = (testId: string) => element.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
    const text = (testId: string) => by(testId)?.textContent?.trim() ?? null;
    return { fixture, http, element, by, text, view: fixture.componentInstance, navigate };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('FI-COLL-UI-009 create: empty save shows every field reason without a request; then picks and saves the exact amount', () => {
    const { fixture, http, view, text, navigate } = setup(null);
    fixture.detectChanges();
    view.draft.businessDate = '';
    view.save(); fixture.detectChanges();
    expect(text('fi-receipt-issue-customerId')).toBe('This field is required.');
    expect(text('fi-receipt-issue-amount')).toBe('This field is required.');
    expect(text('fi-receipt-issue-bankReference')).toBe('This field is required.');
    http.expectNone(API);
    select.mockReturnValueOnce(of([{ id: 7, code: 'C001', name: 'Acme' }])).mockReturnValueOnce(of([{ id: 3, code: 'BK01', name: 'Mega TWD' }]));
    view.pickCustomer(); view.pickFund(); fixture.detectChanges();
    expect(text('fi-receipt-customer')).toBe('C001 · Acme');
    expect(text('fi-receipt-fund')).toBe('BK01 · Mega TWD');
    Object.assign(view.draft, { businessDate: '2026-11-14', amount: '123456789012345.1234', bankReference: ' TT-9 ' });
    expect(view.hasUnsavedChanges()).toBe(true);
    view.save();
    const post = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body as string).toContain('"amount":123456789012345.1234}');
    expect(post.request.headers.get('Content-Type')).toBe('application/json');
    post.flush(JSON.stringify({ ...row(5, 'Draft'), documentNo: 'RC202611005', remark: null, createdAt: '', createdBy: 1, updatedAt: null, updatedBy: null, journalId: null }));
    expect(navigate).toHaveBeenCalledWith('/ar/sales-collections', { state: { collectionReceiptSaved: { no: 'RC202611005', created: true } } });
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-COLL-UI-010 edit loads exact text; duplicate reference and inactive fund account are shown under their fields', () => {
    const { fixture, http, view, text, by } = setup('5');
    http.expectOne(`${API}/5`).flush(`{"id":5,"documentNo":"RC202611005","customerId":7,"businessDate":"2026-11-14","currency":"TWD","amount":999999999999999.9999,`
      + `"bankAccountId":3,"bankReference":"TT-5","remark":"x","status":"Rejected","version":2,"unappliedAmount":null,"customerCode":"C001","customerName":"Acme",`
      + `"bankAccountCode":"BK01","bankAccountName":"Mega TWD","createdAt":"","createdBy":1,"updatedAt":null,"updatedBy":null,"journalId":null}`);
    fixture.detectChanges();
    expect(view.draft.amount).toBe('999999999999999.9999');
    expect(by('fi-receipt-form-rejected')).not.toBeNull();
    expect(text('fi-receipt-form-no')).toBe('RC202611005');
    view.save();
    const put = http.expectOne(r => r.url === `${API}/5` && r.method === 'PUT');
    expect(JSON.parse(put.request.body as string).version).toBe(2);
    put.flush(JSON.stringify({ status: 409, errorCode: 'FI.COLLECTION.BANK_REFERENCE_DUPLICATE' }), { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(text('fi-receipt-issue-bankReference')).toContain('already used by another collection');
    expect(by('fi-receipt-form-reload')).toBeNull();
    view.save();
    http.expectOne(`${API}/5`).flush(JSON.stringify({ status: 422, errorCode: 'FI.COLLECTION.FUND_ACCOUNT_INACTIVE' }), { status: 422, statusText: 'x' });
    fixture.detectChanges();
    expect(text('fi-receipt-issue-bankAccountId')).toContain('inactive');
    expect(text('fi-receipt-issue-bankReference')).toBeNull();
  });

  it('FI-COLL-UI-011 a pending receipt opens read-only with the reason; a stale save offers reload', () => {
    const { fixture, http, view, by, text } = setup('6');
    http.expectOne(`${API}/6`).flush(JSON.stringify({ ...row(6, 'Pending'), remark: null, createdAt: '', createdBy: 1, updatedAt: null, updatedBy: null, journalId: null }));
    fixture.detectChanges();
    expect(text('fi-receipt-form-locked')).toContain('waiting for approval');
    expect((by('fi-receipt-save') as HTMLButtonElement).disabled).toBe(true);
    expect(view.hasUnsavedChanges()).toBe(false);
    view.save(); http.expectNone(`${API}/6`);
    view.lockedKey.set(null);
    view.save();
    http.expectOne(r => r.method === 'PUT').flush(JSON.stringify({ status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' }), { status: 409, statusText: 'x' });
    fixture.detectChanges();
    expect(by('fi-receipt-form-reload')).not.toBeNull();
    by('fi-receipt-form-reload')!.click();
    http.expectOne(`${API}/6`).flush(JSON.stringify({ ...row(6, 'Draft', { version: 3 }), remark: null, createdAt: '', createdBy: 1, updatedAt: null, updatedBy: null, journalId: null }));
    fixture.detectChanges();
    expect(by('fi-receipt-form-error')).toBeNull();
    expect(by('fi-receipt-form-locked')).toBeNull();
  });

  it('FI-COLL-UI-012 a company switch leaves the form for the list without asking to keep foreign IDs', () => {
    const { navigate, view } = setup(null);
    view.draft.amount = '5';
    company.set({ id: 2 });
    TestBed.tick();
    expect(view.hasUnsavedChanges()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/ar/sales-collections');
  });
});
