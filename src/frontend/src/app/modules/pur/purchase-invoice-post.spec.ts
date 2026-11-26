import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import enErrors from '../../../../public/i18n/en/errors.json';
import enPur from '../../../../public/i18n/en/pur.json';
import hansErrors from '../../../../public/i18n/zh-Hans/errors.json';
import hansPur from '../../../../public/i18n/zh-Hans/pur.json';
import hantErrors from '../../../../public/i18n/zh-Hant/errors.json';
import hantPur from '../../../../public/i18n/zh-Hant/pur.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { PurchaseInvoicePage } from './purchase-invoice-page';
import { PURCHASE_INVOICE_ERROR_CODES, PURCHASE_INVOICE_POST_ERROR_CODES } from './purchase-invoice.service';

const API = '/api/v1/pur/invoices';
const row = (id: number, status: string, over: object = {}) => ({ id, supplierId: 5, purchaseOrderId: 70, supplierInvoiceNumber: `INV-${id}`,
  businessDate: '2026-11-20', currency: 'USD', status, totalNet: 100, totalTax: 5, totalGross: 105, lineCount: 1, version: 2, ...over });
const page = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });
const detailText = (over: object = {}) => JSON.stringify({ ...row(1, 'Matched'), createdAt: '2026-11-20T08:00:00Z', createdBy: 3,
  updatedAt: '2026-11-21T09:30:00Z',
  lines: [{ id: 11, lineNo: 1, receiptLineId: 900, quantity: 2.5, unitPrice: 40, taxRatePercent: 5, net: 100, tax: 5, gross: 105, differences: [] }],
  resolutions: [], postedAt: null, fiPurchaseSourceId: null, fiPayableId: null, fiJournalId: null, ...over });
const postedText = (over: object = {}) => detailText({ status: 'Posted', version: 3, postedAt: '2026-11-21T10:15:00',
  fiPurchaseSourceId: 41, fiPayableId: 52, fiJournalId: 63, ...over });
const posted = (over: object = {}) => ({ id: 1, version: 3, status: 'Posted', replayed: false, fiPurchaseSourceId: 41, fiPayableId: 52,
  fiJournalId: 63, postedAt: '2026-11-21T10:15:00', ...over });
const at = (tree: unknown, path: string) => path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown>)?.[key], tree);
const problem = (errorCode: string, extra: object = {}) => JSON.stringify({ errorCode, ...extra });

describe('PUR-INV-UI purchase invoice POST to FI payables (WU-PUR-008b6)', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let confirm: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    company.set({ id: 1 });
    granted = new Set(['PUR.INVOICE.VIEW', 'PUR.INVOICE.MATCH', 'PUR.INVOICE.POST']);
    confirm = vi.fn(() => of(true)); success = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { confirm, success } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const isPost = (r: HttpRequest<unknown>) => r.method === 'POST' && r.url === `${API}/1/post`;

  function open(detail: string) {
    const fixture = TestBed.createComponent(PurchaseInvoicePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page(row(1, 'Matched')));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (testId: string) => element.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
    const text = (testId: string) => by(testId)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    by('pur-invoice-view-1')!.click();
    http.expectOne(`${API}/1`).flush(detail);
    fixture.detectChanges();
    return { fixture, http, by, text, view: fixture.componentInstance };
  }

  it('PUR-INV-UI-020 a Matched invoice with PUR.INVOICE.POST offers POST and shows no FI references yet', () => {
    const { by, text } = open(detailText());
    expect(text('pur-invoice-post')).toBe(String(at(enPur, 'invoice.post')));
    expect(by('pur-invoice-fi-refs')).toBeNull();
    expect(by('pur-invoice-posted-at')).toBeNull();
  });

  it('PUR-INV-UI-020b no POST button for Draft or Posted, nor for Matched without the permission', () => {
    const draft = open(detailText({ status: 'Draft' }));
    expect(draft.by('pur-invoice-post')).toBeNull();
    draft.by('pur-invoice-view-1')!.click();
    draft.http.expectOne(`${API}/1`).flush(postedText());
    draft.fixture.detectChanges();
    expect(draft.by('pur-invoice-post')).toBeNull();
    granted.delete('PUR.INVOICE.POST');
    draft.by('pur-invoice-view-1')!.click();
    draft.http.expectOne(`${API}/1`).flush(detailText());
    draft.fixture.detectChanges();
    expect(draft.by('pur-invoice-post')).toBeNull();
    expect(draft.by('pur-invoice-fi-refs')).toBeNull();
  });

  it('PUR-INV-UI-021 POST confirms with amount, sends the version with an empty body, names the FI payable and journal, then shows the Posted detail', () => {
    const { by, http, fixture, text, view } = open(detailText());
    confirm.mockReturnValueOnce(of(false));
    by('pur-invoice-post')!.click();
    http.expectNone(isPost);
    by('pur-invoice-post')!.click();
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ titleKey: 'pur.invoice.confirmPost',
      contentKey: 'pur.invoice.postExplanation', okKey: 'pur.invoice.post',
      params: { number: 'INV-1', amount: '105', currency: 'USD' } }));
    fixture.detectChanges();
    expect((by('pur-invoice-post') as HTMLButtonElement).disabled).toBe(true);
    expect(by('pur-invoice-post')!.getAttribute('aria-busy')).toBe('true');
    const request = http.expectOne(isPost);
    expect(request.request.params.get('version')).toBe('2');
    expect(request.request.body).toBeNull();
    request.flush(JSON.stringify(posted()));
    http.expectOne(`${API}/1`).flush(postedText());
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('pur.invoice.posted', { number: 'INV-1', payable: 52, journal: 63 });
    expect(view.rows()[0]).toEqual(expect.objectContaining({ status: 'Posted', version: 3 }));
    expect(by('pur-invoice-post')).toBeNull();
    expect(by('pur-invoice-detail-status')!.textContent).toContain('Posted');
    expect(text('pur-invoice-fi-refs')).toBe('Payable #52 · Journal #63 · Source #41');
    expect(text('pur-invoice-posted-at')).toContain('2026');
  });

  it('PUR-INV-UI-022 a replayed POST says nothing was written again', () => {
    const { by, http } = open(detailText());
    by('pur-invoice-post')!.click();
    http.expectOne(isPost).flush(JSON.stringify(posted({ replayed: true })));
    http.expectOne(`${API}/1`).flush(postedText());
    expect(success).toHaveBeenCalledWith('pur.invoice.postedReplayed', { number: 'INV-1', payable: 52, journal: 63 });
    expect(String(at(enPur, 'invoice.postedReplayed'))).toContain('nothing was written again');
  });

  it('PUR-INV-UI-023 FI refusal shows the FI reason and keeps the detail; lock timeout keeps it; status change and version conflict reload', () => {
    const { by, http, fixture, text } = open(detailText());
    by('pur-invoice-post')!.click();
    http.expectOne(isPost).flush(problem('FI.PURCHASE_POSTING.MAPPING_MISSING'), { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'FI.PURCHASE_POSTING.MAPPING_MISSING')));
    http.expectNone(`${API}/1`);
    expect((by('pur-invoice-post') as HTMLButtonElement).disabled).toBe(false);

    by('pur-invoice-post')!.click();
    http.expectOne(isPost).flush(problem('PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE'), { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE')));
    http.expectNone(`${API}/1`);

    by('pur-invoice-post')!.click();
    http.expectOne(isPost).flush(problem('COMMON.CONCURRENCY.CONFLICT'), { status: 409, statusText: 'Conflict' });
    http.expectOne(`${API}/1`).flush(postedText());
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'COMMON.CONCURRENCY.CONFLICT')));
    expect(by('pur-invoice-post')).toBeNull();
    expect(text('pur-invoice-fi-refs')).toContain('#52');
  });

  it('PUR-INV-UI-024 INVALID_POST_STATUS reloads; RATE_SNAPSHOT_MISSING explains without reload; FI 500 shows the reason with a trace', () => {
    const { by, http, fixture, text } = open(detailText());
    by('pur-invoice-post')!.click();
    http.expectOne(isPost).flush(problem('PUR.INVOICE.RATE_SNAPSHOT_MISSING'), { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'PUR.INVOICE.RATE_SNAPSHOT_MISSING')));
    http.expectNone(`${API}/1`);

    by('pur-invoice-post')!.click();
    http.expectOne(isPost).flush(problem('PUR.INVOICE.FI_POSTING_FAILED', { traceId: 'trace-91' }),
      { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'PUR.INVOICE.FI_POSTING_FAILED')));
    expect(text('pur-invoice-action-error')).toContain('trace-91');

    by('pur-invoice-post')!.click();
    http.expectOne(isPost).flush(problem('PUR.INVOICE.INVALID_POST_STATUS'), { status: 422, statusText: 'Unprocessable' });
    http.expectOne(`${API}/1`).flush(detailText({ status: 'Draft', version: 4 }));
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'PUR.INVOICE.INVALID_POST_STATUS')));
    expect(by('pur-invoice-post')).toBeNull();
    expect(by('pur-invoice-match')).not.toBeNull();
  });

  it('PUR-INV-UI-025 every POST error code and every new label has text in all three languages', () => {
    for (const code of PURCHASE_INVOICE_POST_ERROR_CODES) {
      expect(PURCHASE_INVOICE_ERROR_CODES as readonly string[]).toContain(code);
      for (const errors of [enErrors, hantErrors, hansErrors]) expect(typeof at(errors, code), code).toBe('string');
    }
    for (const key of ['post', 'confirmPost', 'postExplanation', 'posted', 'postedReplayed', 'postedAt', 'fiReferences', 'fiReferenceText'])
      for (const pur of [enPur, hantPur, hansPur]) expect(typeof at(pur, `invoice.${key}`), key).toBe('string');
    // A Matched invoice is still described as not posted; only the Posted hint speaks of payables as done.
    expect(String(at(enPur, 'invoice.stateHint.matched')).toLowerCase()).toContain('not yet posted');
  });
});
