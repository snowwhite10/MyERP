import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import enErrors from '../../../../public/i18n/en/errors.json';
import enPur from '../../../../public/i18n/en/pur.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { PurchaseInvoicePage } from './purchase-invoice-page';

const API = '/api/v1/pur/invoices';
const row = (id: number, status: string, over: object = {}) => ({ id, supplierId: 5, purchaseOrderId: 70, supplierInvoiceNumber: `INV-${id}`,
  businessDate: '2026-11-20', currency: 'TWD', status, totalNet: 100, totalTax: 5, totalGross: 105, lineCount: 1, version: 2, ...over });
const page = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });
const detailText = (over: object = {}) => JSON.stringify({ ...row(1, 'Matched'), createdAt: '2026-11-20T08:00:00Z', createdBy: 3,
  updatedAt: '2026-11-21T09:30:00Z',
  lines: [{ id: 11, lineNo: 1, receiptLineId: 900, quantity: 2.5, unitPrice: 40, taxRatePercent: 5, net: 100, tax: 5, gross: 105, differences: [] }],
  resolutions: [], ...over });
const at = (tree: unknown, path: string) => path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown>)?.[key], tree);

describe('PUR-INV-UI purchase invoice MATCH and RESOLVE actions', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let confirm: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    company.set({ id: 1 });
    granted = new Set(['PUR.INVOICE.VIEW', 'PUR.INVOICE.MATCH', 'PUR.INVOICE.RESOLVE']);
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
  const isMatch = (r: HttpRequest<unknown>) => r.method === 'POST' && r.url === `${API}/1/match`;
  const isResolve = (r: HttpRequest<unknown>) => r.method === 'POST' && r.url === `${API}/3/resolve`;
  const differences = [
    { kind: 'UNIT_PRICE', baseline: 2.000000, invoice: 3.000000, difference: 1.000000 },
    { kind: 'GROSS', baseline: 9.6000, invoice: 'BIG', difference: 4.8000 }];
  /** Disputed detail: lines 1 and 3 differ (line 1 with an exact 19-digit invoice gross); line 2 does not. */
  const disputedText = (over: object = {}) => detailText({ id: 3, supplierInvoiceNumber: 'INV-3', status: 'Disputed', version: 4, lines: [
    { id: 11, lineNo: 1, receiptLineId: 900, quantity: 4, unitPrice: 3, taxRatePercent: 20, net: 12, tax: 2.4, gross: 14.4, differences },
    { id: 12, lineNo: 2, receiptLineId: 901, quantity: 1, unitPrice: 2, taxRatePercent: 20, net: 2, tax: 0.4, gross: 2.4, differences: [] },
    { id: 13, lineNo: 3, receiptLineId: 902, quantity: 1, unitPrice: 5, taxRatePercent: 5, net: 5, tax: 0.25, gross: 5.25,
      differences: [{ kind: 'TAX_RATE', baseline: 0, invoice: 5, difference: 5 }] }], ...over })
    .replace('"invoice":"BIG"', '"invoice":999999999999999.9999');
  const withoutLine3Difference = (text: string) => text.replace(/"differences":\[\{"kind":"TAX_RATE"[^\]]*\]/, '"differences":[]');

  function open(id: number, detail: string) {
    const fixture = TestBed.createComponent(PurchaseInvoicePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page(row(1, 'Draft'), row(3, 'Disputed', { version: 4 })));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (testId: string) => element.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
    const text = (testId: string) => by(testId)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    by(`pur-invoice-view-${id}`)!.click();
    http.expectOne(`${API}/${id}`).flush(detail);
    fixture.detectChanges();
    const type = (testId: string, value: string) => {
      const input = by(testId) as HTMLTextAreaElement;
      input.value = value; input.dispatchEvent(new Event('input')); fixture.detectChanges();
    };
    const choose = (testId: string) => { (by(testId) as HTMLInputElement).click(); fixture.detectChanges(); };
    return { fixture, http, by, text, type, choose, view: fixture.componentInstance };
  }

  it('PUR-INV-UI-010 MATCH shows only for Draft with MATCH permission; a Draft has no RESOLVE form', () => {
    const { by } = open(1, detailText({ status: 'Draft' }));
    expect(by('pur-invoice-match')).not.toBeNull();
    expect(by('pur-invoice-resolve')).toBeNull();
  });

  it('PUR-INV-UI-010b no MATCH without permission; Disputed without RESOLVE shows the differences but no decision inputs', () => {
    granted.delete('PUR.INVOICE.MATCH'); granted.delete('PUR.INVOICE.RESOLVE');
    const { by, http, fixture, text } = open(1, detailText({ status: 'Draft' }));
    expect(by('pur-invoice-match')).toBeNull();
    by('pur-invoice-view-3')!.click();
    http.expectOne(`${API}/3`).flush(disputedText());
    fixture.detectChanges();
    expect(text('pur-invoice-dispute-1')).toContain('Unit price 2 → 3 (+1)');
    expect(by('pur-invoice-accept-1')).toBeNull();
    expect(by('pur-invoice-resolve-submit')).toBeNull();
    expect(text('pur-invoice-resolve-permission')).toBe(String(at(enPur, 'invoice.resolvePermission')));
  });

  it('PUR-INV-UI-010c a Matched invoice offers no MATCH', () => {
    const { by } = open(1, detailText({ status: 'Matched' }));
    expect(by('pur-invoice-match')).toBeNull();
  });

  it('PUR-INV-UI-011 MATCH confirms, posts the version with an empty body, reports Disputed and reloads the detail and row', () => {
    const { by, http, fixture, view } = open(1, detailText({ status: 'Draft', version: 2 }));
    confirm.mockReturnValueOnce(of(false));
    by('pur-invoice-match')!.click();
    http.expectNone(isMatch);
    by('pur-invoice-match')!.click();
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ titleKey: 'pur.invoice.confirmMatch',
      contentKey: 'pur.invoice.matchExplanation' }));
    const request = http.expectOne(isMatch);
    expect(request.request.params.get('version')).toBe('2');
    expect(request.request.body).toBeNull();
    request.flush({ id: 1, version: 3, status: 'Disputed', amount: {} });
    http.expectOne(`${API}/1`).flush(disputedText({ id: 1, version: 3 }));
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('pur.invoice.matchedDisputed', { number: 'INV-1' });
    expect(view.rows().find(r => r.id === 1)).toEqual(expect.objectContaining({ status: 'Disputed', version: 3 }));
    expect(by('pur-invoice-detail-status')!.textContent).toContain('Disputed');
    expect(by('pur-invoice-match')).toBeNull();
  });

  it('PUR-INV-UI-012 MATCH returning Matched says it is not yet posted to payables', () => {
    const { by, http } = open(1, detailText({ status: 'Draft' }));
    by('pur-invoice-match')!.click();
    http.expectOne(isMatch).flush({ id: 1, version: 3, status: 'Matched', amount: {} });
    http.expectOne(`${API}/1`).flush(detailText({ id: 1, status: 'Matched', version: 3 }));
    expect(success).toHaveBeenCalledWith('pur.invoice.matchedMatched', { number: 'INV-1' });
    expect(String(at(enPur, 'invoice.matchedMatched'))).toContain('not yet posted');
  });

  it('PUR-INV-UI-013 MATCH 422 insufficient quantity explains without reloading; 409 conflict explains and reloads', () => {
    const { by, http, fixture, text } = open(1, detailText({ status: 'Draft' }));
    by('pur-invoice-match')!.click();
    http.expectOne(isMatch).flush(JSON.stringify({ errorCode: 'PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY' }),
      { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY')));
    http.expectNone(`${API}/1`);
    by('pur-invoice-match')!.click();
    http.expectOne(isMatch).flush(JSON.stringify({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }), { status: 409, statusText: 'Conflict' });
    http.expectOne(`${API}/1`).flush(detailText({ id: 1, status: 'Matched', version: 5 }));
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'COMMON.CONCURRENCY.CONFLICT')));
    expect(by('pur-invoice-match')).toBeNull();
  });

  it('PUR-INV-UI-014 Disputed detail lists each difference exactly and asks a decision only for differing lines', () => {
    const { by, text } = open(3, disputedText());
    expect(text('pur-invoice-dispute-1')).toContain('Unit price 2 → 3 (+1)');
    expect(text('pur-invoice-dispute-1')).toContain('Gross 9.6 → 999,999,999,999,999.9999 (+4.8)');
    expect(text('pur-invoice-dispute-3')).toContain('Tax rate 0% → 5% (+5%)');
    expect(by('pur-invoice-dispute-2')).toBeNull();
    expect(by('pur-invoice-accept-1')).not.toBeNull();
    expect(by('pur-invoice-reject-3')).not.toBeNull();
    expect(by('pur-invoice-reason-1')!.getAttribute('maxlength')).toBe('500');
  });

  it('PUR-INV-UI-015 incomplete decisions are explained per line and not sent; one reject sends trimmed reasons and returns to Draft', () => {
    const { by, http, fixture, text, type, choose } = open(3, disputedText());
    by('pur-invoice-resolve-submit')!.click(); fixture.detectChanges();
    expect(text('pur-invoice-issue-1')).toBe('Choose accept or reject.');
    expect(text('pur-invoice-issue-3')).toBe('Choose accept or reject.');
    expect(document.activeElement).toBe(by('pur-invoice-accept-1'));
    expect(by('pur-invoice-dispute-1')!.querySelector('[role=radiogroup]')!.getAttribute('aria-invalid')).toBe('true');
    choose('pur-invoice-accept-1'); type('pur-invoice-reason-1', '  Agreed surcharge  ');
    choose('pur-invoice-reject-3'); type('pur-invoice-reason-3', '   ');
    by('pur-invoice-resolve-submit')!.click(); fixture.detectChanges();
    expect(by('pur-invoice-issue-1')).toBeNull();
    expect(text('pur-invoice-issue-3')).toBe('Enter a reason of 1 to 500 characters.');
    expect(document.activeElement).toBe(by('pur-invoice-reason-3'));
    expect(by('pur-invoice-reason-3')!.getAttribute('aria-invalid')).toBe('true');
    http.expectNone(isResolve);
    type('pur-invoice-reason-3', ' Rate not agreed ');
    by('pur-invoice-resolve-submit')!.click();
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ titleKey: 'pur.invoice.confirmResolve',
      params: { accepted: 1, rejected: 1 } }));
    const request = http.expectOne(isResolve);
    expect(request.request.params.get('version')).toBe('4');
    expect(request.request.body).toEqual({ lines: [{ lineNo: 1, action: 'Accept', reason: 'Agreed surcharge' },
      { lineNo: 3, action: 'Reject', reason: 'Rate not agreed' }] });
    request.flush({ id: 3, version: 5, status: 'Draft', decisions: [] });
    http.expectOne(`${API}/3`).flush(detailText({ id: 3, supplierInvoiceNumber: 'INV-3', status: 'Draft', version: 5 }));
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('pur.invoice.resolvedDraft', { number: 'INV-3' });
    expect(by('pur-invoice-resolve')).toBeNull();
  });

  it('PUR-INV-UI-016 lock timeout keeps input; INVALID_RESOLUTION reloads keeping reasons; 500 shows trace; all accepted reports Matched', () => {
    const { by, http, fixture, text, type, choose } = open(3, disputedText());
    choose('pur-invoice-accept-1'); type('pur-invoice-reason-1', 'ok 1');
    choose('pur-invoice-accept-3'); type('pur-invoice-reason-3', 'ok 3');
    by('pur-invoice-resolve-submit')!.click();
    http.expectOne(isResolve).flush(JSON.stringify({ errorCode: 'PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE' }), { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE')));
    http.expectNone(`${API}/3`);
    expect((by('pur-invoice-reason-1') as HTMLTextAreaElement).value).toBe('ok 1');
    by('pur-invoice-resolve-submit')!.click();
    http.expectOne(isResolve).flush(JSON.stringify({ errorCode: 'PUR.INVOICE.INVALID_RESOLUTION' }), { status: 422, statusText: 'Unprocessable' });
    // After the reload line 3 no longer differs; line 1 keeps its decision and reason.
    http.expectOne(`${API}/3`).flush(withoutLine3Difference(disputedText()));
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain(String(at(enErrors, 'PUR.INVOICE.INVALID_RESOLUTION')));
    expect(by('pur-invoice-dispute-3')).toBeNull();
    expect((by('pur-invoice-reason-1') as HTMLTextAreaElement).value).toBe('ok 1');
    expect((by('pur-invoice-accept-1') as HTMLInputElement).checked).toBe(true);
    by('pur-invoice-resolve-submit')!.click();
    http.expectOne(isResolve).flush(JSON.stringify({ traceId: 'trace-77' }), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('pur-invoice-action-error')).toContain('trace-77');
    by('pur-invoice-resolve-submit')!.click();
    const ok = http.expectOne(isResolve);
    expect(ok.request.body).toEqual({ lines: [{ lineNo: 1, action: 'Accept', reason: 'ok 1' }] });
    ok.flush({ id: 3, version: 5, status: 'Matched', decisions: [] });
    http.expectOne(`${API}/3`).flush(detailText({ id: 3, supplierInvoiceNumber: 'INV-3', status: 'Matched', version: 5 }));
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('pur.invoice.resolvedMatched', { number: 'INV-3' });
    expect(by('pur-invoice-action-error')).toBeNull();
  });

  it('PUR-INV-UI-017 a Disputed invoice without any computable difference explains why and offers no submit', () => {
    const { by, text } = open(3, disputedText().replace(/"differences":\[\{[^\]]*\]/g, '"differences":[]'));
    expect(text('pur-invoice-no-differences')).toBe(String(at(enPur, 'invoice.noDifferences')));
    expect(by('pur-invoice-resolve-submit')).toBeNull();
  });

  it('PUR-INV-UI-018 busy blocks double submit; a stale action reply after switching invoice is ignored; 404 reloads the list', () => {
    const { by, http, fixture, view, text, type, choose } = open(1, detailText({ status: 'Draft' }));
    by('pur-invoice-match')!.click(); fixture.detectChanges();
    expect((by('pur-invoice-match') as HTMLButtonElement).disabled).toBe(true);
    by('pur-invoice-match')!.click();
    const pending = http.expectOne(isMatch);
    by('pur-invoice-view-3')!.click();
    http.expectOne(`${API}/3`).flush(disputedText());
    pending.flush({ id: 1, version: 3, status: 'Matched', amount: {} });
    fixture.detectChanges();
    expect(success).not.toHaveBeenCalled();
    expect(view.detail()!.id).toBe(3);
    for (const n of [1, 3]) { choose(`pur-invoice-accept-${n}`); type(`pur-invoice-reason-${n}`, 'r'); }
    by('pur-invoice-resolve-submit')!.click();
    http.expectOne(isResolve).flush(JSON.stringify({ errorCode: 'PUR.INVOICE.NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    http.expectOne(isList).flush(page(row(1, 'Draft')));
    fixture.detectChanges();
    expect(by('pur-invoice-detail')).toBeNull();
    expect(text('pur-invoice-error')).toContain('This purchase invoice was not found.');
  });
});
