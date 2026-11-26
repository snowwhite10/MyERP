import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import enErrors from '../../../../public/i18n/en/errors.json';
import enLayout from '../../../../public/i18n/en/layout.json';
import enPur from '../../../../public/i18n/en/pur.json';
import hansErrors from '../../../../public/i18n/zh-Hans/errors.json';
import hansLayout from '../../../../public/i18n/zh-Hans/layout.json';
import hansPur from '../../../../public/i18n/zh-Hans/pur.json';
import hantErrors from '../../../../public/i18n/zh-Hant/errors.json';
import hantLayout from '../../../../public/i18n/zh-Hant/layout.json';
import hantPur from '../../../../public/i18n/zh-Hant/pur.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { MAIN_MENU, filterMenu } from '../../layout/menu';
import { PurchaseInvoicePage } from './purchase-invoice-page';
import { PermissionService } from '../../core/auth/permission.service';
import {
  PURCHASE_INVOICE_ERROR_CODES, PURCHASE_INVOICE_STATUSES, PurchaseInvoiceLine, purchaseInvoiceFilterIssue, purchaseInvoiceResolveIssues,
} from './purchase-invoice.service';
import { PUR_ROUTES } from './pur.routes';

const API = '/api/v1/pur/invoices';
const row = (id: number, status: string, over: object = {}) => ({ id, supplierId: 5, purchaseOrderId: 70, supplierInvoiceNumber: `INV-${id}`,
  businessDate: '2026-11-20', currency: 'TWD', status, totalNet: 100, totalTax: 5, totalGross: 105, lineCount: 1, version: 2, ...over });
/** Response text exactly as the server writes it (digits beyond a JavaScript double stay text). */
const page = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });
const detailText = (over: object = {}) => JSON.stringify({ ...row(1, 'Matched'), createdAt: '2026-11-20T08:00:00Z', createdBy: 3,
  updatedAt: '2026-11-21T09:30:00Z',
  lines: [{ id: 11, lineNo: 1, receiptLineId: 900, quantity: 2.500000, unitPrice: 40.0000, taxRatePercent: 5.0000, net: 100.0000, tax: 5.0000, gross: 105.0000 }],
  resolutions: [], ...over }).replace('"totalGross":105', '"totalGross":999999999999999.9999');
const at = (tree: unknown, path: string) => path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown>)?.[key], tree);
const keys = (node: object, prefix = ''): string[] => Object.entries(node).flatMap(([k, v]) =>
  typeof v === 'object' ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]);

describe('PUR-INV-UI purchase invoice rules', () => {
  it('PUR-INV-UI-U01 filters: reversed range, non-positive or fractional IDs are refused; same-day range and blanks pass', () => {
    expect(purchaseInvoiceFilterIssue({ from: '2026-11-30', to: '2026-11-01' })).toBe('range');
    expect(purchaseInvoiceFilterIssue({ from: '2026-11-30', to: '2026-11-30' })).toBeNull();
    expect(purchaseInvoiceFilterIssue({ from: '', to: '', supplierId: null, purchaseOrderId: null })).toBeNull();
    expect(purchaseInvoiceFilterIssue({ supplierId: 0 })).toBe('supplier');
    expect(purchaseInvoiceFilterIssue({ supplierId: 1.5 })).toBe('supplier');
    expect(purchaseInvoiceFilterIssue({ purchaseOrderId: -1 })).toBe('order');
    expect(purchaseInvoiceFilterIssue({ supplierId: 7, purchaseOrderId: Number.MAX_SAFE_INTEGER })).toBeNull();
  });

  it('PUR-INV-UI-U02 every error code, status, menu and screen text exists in all three languages', () => {
    for (const errors of [enErrors, hantErrors, hansErrors])
      for (const code of PURCHASE_INVOICE_ERROR_CODES) expect(typeof at(errors, code), code).toBe('string');
    const en = keys(enPur.invoice).sort();
    expect(keys(hantPur.invoice).sort()).toEqual(en);
    expect(keys(hansPur.invoice).sort()).toEqual(en);
    for (const status of PURCHASE_INVOICE_STATUSES) {
      expect(en).toContain(`state.${status.toLowerCase()}`);
      expect(en).toContain(`stateHint.${status.toLowerCase()}`);
    }
    for (const layout of [enLayout, hantLayout, hansLayout]) expect(typeof at(layout, 'menu.purchaseInvoices')).toBe('string');
  });

  it('PUR-INV-UI-U04 decisions: missing action, blank or over-long reason is refused; trimmed 500 passes; undisputed lines need none', () => {
    const diff = [{ kind: 'NET', baseline: '1', invoice: '2', difference: '1' }];
    const line = (lineNo: number, differences = diff) => ({ id: lineNo, lineNo, receiptLineId: 1, quantity: '1', unitPrice: '1',
      taxRatePercent: '0', net: '1', tax: '0', gross: '1', differences }) as PurchaseInvoiceLine;
    const lines = [line(1), line(2), line(3), line(4, [])];
    expect(purchaseInvoiceResolveIssues(lines, {
      1: { action: '', reason: 'ok' }, 2: { action: 'Accept', reason: '   ' }, 3: { action: 'Reject', reason: ` ${'x'.repeat(501)} ` },
    })).toEqual({ 1: 'action', 2: 'reason', 3: 'reasonLength' });
    expect(purchaseInvoiceResolveIssues(lines, {
      1: { action: 'Accept', reason: ` ${'x'.repeat(500)} ` }, 2: { action: 'Reject', reason: 'a' }, 3: { action: 'Accept', reason: 'b' },
    })).toEqual({});
    expect(purchaseInvoiceResolveIssues(lines, {})).toEqual({ 1: 'action', 2: 'action', 3: 'action' });
  });

  it('PUR-INV-UI-U03 route and menu need PUR.INVOICE.VIEW; the menu hides the item without it', () => {
    expect(PUR_ROUTES.find(r => r.path === 'purchase-invoices')?.canActivate?.length).toBe(1);
    const visible = (granted: string[]) => filterMenu(MAIN_MENU, code => granted.includes(code))
      .flatMap(item => item.children ?? []).some(item => item.route === '/pur/purchase-invoices');
    expect(visible(['PUR.INVOICE.VIEW'])).toBe(true);
    expect(visible(['PUR.REQUISITION.VIEW'])).toBe(false);
  });
});

describe('PUR-INV-UI purchase invoice list and detail', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  beforeEach(() => {
    company.set({ id: 1 });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: () => false, hasAny: () => false } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function create(body = page(row(1, 'Draft'), row(2, 'Matched'), row(3, 'Disputed', { totalGross: 'BIG' }), row(4, 'Rejected'), row(5, 'Posted'))
    .replace('"totalGross":"BIG"', '"totalGross":999999999999999.9999')) {
    const fixture = TestBed.createComponent(PurchaseInvoicePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne(isList);
    expect(first.request.params.keys()).toEqual(['page', 'pageSize']);
    first.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.trim() ?? null;
    const cells = (id: string) => [...by(id)!.querySelectorAll('td')].map(td => td.textContent!.trim()).join(' ');
    return { fixture, http, element, by, text, cells, view: fixture.componentInstance };
  }

  it('PUR-INV-UI-001 rows show exact gross amounts and a translated tag for each of the five states; no write actions exist', () => {
    const { text, element } = create();
    expect(text('pur-invoice-gross-3')).toBe('999,999,999,999,999.9999 TWD');
    expect(['Draft', 'Matched', 'Disputed', 'Rejected', 'Posted'].map((_, i) => text(`pur-invoice-status-${i + 1}`)))
      .toEqual(['Draft', 'Matched', 'Disputed', 'Rejected', 'Posted']);
    expect(element.querySelector('[data-testid="pur-invoice-status-3"]')!.className).toContain('pending');
    expect(element.querySelectorAll('a[href*="edit"], [data-testid*="match"], [data-testid*="resolve"]').length).toBe(0);
  });

  it('PUR-INV-UI-002 empty list and search-empty have different messages; filters are trimmed and sent as typed', () => {
    const { element, view, fixture, http } = create(page());
    expect(element.textContent).toContain('No purchase invoices yet.');
    view.keyword = '  INV-9  '; view.status = 'Disputed'; view.from = '2026-11-01'; view.to = '2026-11-30';
    view.supplierId = 5; view.purchaseOrderId = 70; view.search();
    const request = http.expectOne(isList);
    expect(Object.fromEntries(request.request.params.keys().map(k => [k, request.request.params.get(k)]))).toEqual({ page: '1', pageSize: '20',
      keyword: 'INV-9', status: 'Disputed', from: '2026-11-01', to: '2026-11-30', supplierId: '5', purchaseOrderId: '70' });
    request.flush(page());
    fixture.detectChanges();
    expect(element.textContent).toContain('No purchase invoices match these filters.');
  });

  it('PUR-INV-UI-003 invalid filters are explained and never sent; fixing them clears the message', () => {
    const { view, fixture, http, text, by } = create();
    view.from = '2026-11-30'; view.to = '2026-11-01'; view.search(); fixture.detectChanges();
    expect(text('pur-invoice-filter-error')).toBe('The end date must not be earlier than the start date.');
    view.from = ''; view.supplierId = 0; view.search(); fixture.detectChanges();
    expect(text('pur-invoice-filter-error')).toBe('Supplier ID must be a positive whole number.');
    http.expectNone(isList);
    view.supplierId = null; view.search();
    http.expectOne(isList).flush(page());
    fixture.detectChanges();
    expect(by('pur-invoice-filter-error')).toBeNull();
  });

  it('PUR-INV-UI-004 server sorting and paging map to sort / page parameters and keep the applied filters', () => {
    const { view, http } = create();
    view.status = 'Matched'; view.search();
    http.expectOne(isList).flush(page());
    view.onQuery({ pageIndex: 2, pageSize: 50, sort: [{ key: 'totalGross', value: 'ascend' }], filter: [] });
    const request = http.expectOne(isList);
    expect(request.request.params.get('sort')).toBe('totalGross');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.get('status')).toBe('Matched');
    request.flush(page());
    view.status = 'Draft'; // typed but not applied
    view.onQuery({ pageIndex: 2, pageSize: 50, sort: [{ key: 'businessDate', value: 'descend' }], filter: [] });
    const next = http.expectOne(isList);
    expect(next.request.params.get('sort')).toBe('-businessDate');
    expect(next.request.params.get('status')).toBe('Matched');
    next.flush(page());
  });

  it('PUR-INV-UI-005 detail shows exact lines, state hint and an honest "not posted" note for Matched with no history', () => {
    const { by, text, cells, http, fixture } = create();
    by('pur-invoice-view-2')!.click();
    http.expectOne(`${API}/2`).flush(detailText());
    fixture.detectChanges();
    expect(text('pur-invoice-detail-gross')).toBe('999,999,999,999,999.9999 TWD');
    expect(text('pur-invoice-state-hint')).toBe('Matched within tolerance. Not yet posted to payables.');
    expect(cells('pur-invoice-line-1')).toBe('1 #900 2.5 40 5% 100 5 105');
    expect(text('pur-invoice-no-history')).toBe('No difference decisions have been made.');
    by('pur-invoice-detail-close')!.click(); fixture.detectChanges();
    expect(by('pur-invoice-detail')).toBeNull();
  });

  it('PUR-INV-UI-006 decision history lists version, decision, reason and signed differences per line', () => {
    const { by, text, cells, http, fixture } = create();
    by('pur-invoice-view-3')!.click();
    http.expectOne(`${API}/3`).flush(detailText({ status: 'Draft', resolutions: [
      { id: 41, invoiceVersion: 3, lineNo: 1, action: 'Accept', reason: 'Agreed surcharge', netDifference: 2.5000, taxDifference: 0.1250,
        grossDifference: 2.6250, decidedBy: 8, decidedAt: '2026-11-22T10:15:00Z' },
      { id: 42, invoiceVersion: 3, lineNo: 2, action: 'Reject', reason: 'Price not agreed', netDifference: -1.0000, taxDifference: 0.0000,
        grossDifference: -1.0000, decidedBy: 8, decidedAt: '2026-11-22T10:15:00Z' }] }));
    fixture.detectChanges();
    expect(text('pur-invoice-state-hint')).toBe('Saved but not yet matched against the order and receipts.');
    expect(cells('pur-invoice-decision-41')).toBe('3 1 Accepted Agreed surcharge +2.5 +0.125 +2.625 #8 2026-11-22 10:15');
    expect(cells('pur-invoice-decision-42')).toBe('3 2 Rejected Price not agreed -1 0 -1 #8 2026-11-22 10:15');
  });

  it('PUR-INV-UI-007 a detail 404 explains the invoice is gone and reloads the list; a stale detail response is ignored', () => {
    const { by, text, http, fixture } = create();
    by('pur-invoice-view-1')!.click();
    const stale = http.expectOne(`${API}/1`);
    by('pur-invoice-view-4')!.click();
    stale.flush(detailText({ id: 1 }));
    http.expectOne(`${API}/4`).flush(JSON.stringify({ errorCode: 'PUR.INVOICE.NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    http.expectOne(isList).flush(page(row(1, 'Draft')));
    fixture.detectChanges();
    expect(by('pur-invoice-detail')).toBeNull();
    expect(text('pur-invoice-error')).toContain('This purchase invoice was not found.');
  });

  it('PUR-INV-UI-008 list 500 shows the trace ID and a load-failed table message; 403 says permission is missing', () => {
    const { view, http, fixture, text, element } = create();
    view.search();
    http.expectOne(isList).flush(JSON.stringify({ traceId: 'trace-500' }), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('pur-invoice-error')).toContain('trace-500');
    expect(element.textContent).toContain('Could not load purchase invoices. Try again.');
    view.search();
    http.expectOne(isList).flush(JSON.stringify({ errorCode: 'COMMON.PERMISSION.DENIED' }), { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(text('pur-invoice-error')).toContain(String(at(enErrors, 'COMMON.PERMISSION.DENIED')));
  });

  it('PUR-INV-UI-009 switching company clears filters and detail and reloads without stale results', () => {
    const { view, http, fixture, by } = create();
    view.keyword = 'X'; view.search();
    const stale = http.expectOne(isList);
    by('pur-invoice-view-1')!.click();
    const detail = http.expectOne(`${API}/1`);
    company.set({ id: 2 }); TestBed.tick();
    const fresh = http.expectOne(r => isList(r) && !r.params.has('keyword'));
    stale.flush(page(row(9, 'Draft'))); detail.flush(detailText());
    fresh.flush(page(row(20, 'Posted')));
    fixture.detectChanges();
    expect(view.keyword).toBe('');
    expect(view.rows().map(r => r.id)).toEqual([20]);
    expect(by('pur-invoice-detail')).toBeNull();
  });
});
