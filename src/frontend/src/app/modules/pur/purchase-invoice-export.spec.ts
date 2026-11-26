import { HttpErrorResponse, HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { TimeoutError } from 'rxjs';
import { vi } from 'vitest';
import enPur from '../../../../public/i18n/en/pur.json';
import hansPur from '../../../../public/i18n/zh-Hans/pur.json';
import hantPur from '../../../../public/i18n/zh-Hant/pur.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { purchaseInvoiceExportFailure } from './purchase-invoice-export';
import { PurchaseInvoicePage } from './purchase-invoice-page';

const API = '/api/v1/pur/invoices';
const row = (id: number) => ({ id, supplierId: 5, purchaseOrderId: 70, supplierInvoiceNumber: `INV-${id}`, businessDate: '2026-11-20',
  currency: 'TWD', status: 'Draft', totalNet: 100, totalTax: 5, totalGross: 105, lineCount: 1, version: 1 });
const page = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });
const csv = (rows: number) => new Blob(['﻿Invoice number,Status\r\n' + Array.from({ length: rows }, (_, i) => `"INV-1, ""A""\r\nB",Draft ${i}\r\n`).join('')]);
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0)); };

describe('PUR-INV-EXPORT-UI-U01 purchaseInvoiceExportFailure', () => {
  const http = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  it('names permission, the 10,000-line limit, invalid filters, timeout, network and rate limit without a trace', () => {
    expect(purchaseInvoiceExportFailure(http(403, { errorCode: 'COMMON.PERMISSION.DENIED' })))
      .toEqual({ key: 'pur.invoice.exportDenied', traceId: null, details: [] });
    expect(purchaseInvoiceExportFailure(http(422, { errorCode: 'PUR.INVOICE.EXPORT_LIMIT' })).key).toBe('errors.PUR.INVOICE.EXPORT_LIMIT');
    const invalid = purchaseInvoiceExportFailure(http(400, { errors: { supplierId: ['Must be positive.'] } }));
    expect(invalid).toEqual({ key: 'pur.invoice.exportInvalid', traceId: null, details: ['supplierId: Must be positive.'] });
    expect(purchaseInvoiceExportFailure(new TimeoutError()).key).toBe('pur.invoice.exportTimeout');
    expect(purchaseInvoiceExportFailure(http(0, null)).key).toBe('errors.http.network');
    expect(purchaseInvoiceExportFailure(http(429, null)).key).toBe('errors.COMMON.REQUEST.RATE_LIMITED');
  });

  it('keeps the 5xx trace ID; an unknown 422 or non-HTTP error is a generic failure', () => {
    expect(purchaseInvoiceExportFailure(http(500, { traceId: 'trace-x' })))
      .toEqual({ key: 'pur.invoice.exportFailed', traceId: 'trace-x', details: [] });
    expect(purchaseInvoiceExportFailure(http(422, { errorCode: 'OTHER' })).key).toBe('pur.invoice.exportFailed');
    expect(purchaseInvoiceExportFailure(new Error('boom')).key).toBe('pur.invoice.exportFailed');
  });

  it('every export text exists in all three languages', () => {
    const exportKeys = (pur: { invoice: Record<string, unknown> }) => Object.keys(pur.invoice).filter(k => k.startsWith('export')).sort();
    expect(exportKeys(enPur).length).toBe(15);
    expect(exportKeys(hantPur)).toEqual(exportKeys(enPur));
    expect(exportKeys(hansPur)).toEqual(exportKeys(enPur));
  });
});

describe('PUR-INV-EXPORT-UI 介面 4.19 export button', () => {
  let granted: string[];
  let clicks: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    granted = ['PUR.INVOICE.VIEW', 'PUR.INVOICE.EXPORT'];
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: signal({ id: 1 }) } },
      { provide: PermissionService, useValue: { has: (c: string) => granted.includes(c), hasAny: (c: string[]) => c.some(x => granted.includes(x)) } }] });
    URL.createObjectURL = vi.fn(() => 'blob:invoice');
    URL.revokeObjectURL = vi.fn();
    clicks = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });
  afterEach(() => { clicks.mockRestore(); TestBed.inject(HttpTestingController).verify(); });

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const isExport = (r: HttpRequest<unknown>) => r.url === `${API}/export`;
  /** The table emits its default header sort (date descending) once after the first render; that reload is flushed too. */
  async function create(body = page(row(1), row(2))) {
    const fixture = TestBed.createComponent(PurchaseInvoicePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(body);
    fixture.detectChanges(); await settle();
    for (const reload of http.match(isList)) reload.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.trim() ?? null;
    const button = () => by('pur-invoice-export') as HTMLButtonElement;
    return { fixture, http, by, text, button, view: fixture.componentInstance };
  }
  const reply = (count: string, rows: number) => ({ headers: { 'X-Export-Row-Count': count } , body: csv(rows) });

  it('PUR-INV-EXPORT-UI-001 sends the applied filters and sort without paging, saves the checked file and reports the line count', async () => {
    const { view, http, fixture, text, button } = await create();
    expect(text('pur-invoice-export-hint')).toContain('applied filters and sort');
    view.keyword = ' INV '; view.status = 'Matched'; view.from = '2026-11-01'; view.to = '2026-11-30'; view.supplierId = 5; view.search();
    http.expectOne(isList).flush(page(row(1)));
    view.keyword = 'typed-not-applied';
    fixture.detectChanges();
    button().click();
    const request = http.expectOne(isExport);
    expect(request.request.responseType).toBe('blob');
    expect(Object.fromEntries(request.request.params.keys().map(k => [k, request.request.params.get(k)]))).toEqual({
      keyword: 'INV', status: 'Matched', from: '2026-11-01', to: '2026-11-30', supplierId: '5', sort: '-businessDate' });
    const { headers, body } = reply('2', 2);
    request.flush(body, { headers });
    await settle(); fixture.detectChanges();
    expect(clicks).toHaveBeenCalledTimes(1);
    expect((clicks.mock.contexts[0] as HTMLAnchorElement).download).toBe('purchase-invoices.csv');
    expect(text('pur-invoice-export-notice')).toBe('Exported 2 invoice lines to purchase-invoices.csv.');
    expect(fixture.nativeElement.querySelector('.ant-alert-success')).not.toBeNull();
  });

  it('PUR-INV-EXPORT-UI-002 zero lines warn without a file; a count mismatch is refused as incomplete', async () => {
    const { http, fixture, text, button, by } = await create();
    button().click();
    const empty = reply('0', 0); http.expectOne(isExport).flush(empty.body, { headers: empty.headers });
    await settle(); fixture.detectChanges();
    expect(text('pur-invoice-export-notice')).toBe('No invoice lines match the applied filters. No file was downloaded.');
    expect(fixture.nativeElement.querySelector('.ant-alert-warning')).not.toBeNull();
    button().click();
    const mismatch = reply('3', 1); http.expectOne(isExport).flush(mismatch.body, { headers: mismatch.headers });
    await settle(); fixture.detectChanges();
    expect(by('pur-invoice-export-notice')).toBeNull();
    expect(text('pur-invoice-export-error')).toContain('row count did not match');
    expect(clicks).not.toHaveBeenCalled();
  });

  it('PUR-INV-EXPORT-UI-003 a second click while exporting sends nothing and the hint says why', async () => {
    const { http, fixture, text, button, view } = await create();
    button().click(); fixture.detectChanges();
    expect(button().disabled).toBe(true);
    expect(text('pur-invoice-export-hint')).toBe('Export in progress. Wait for it to finish.');
    view.exportCsv();
    const request = http.expectOne(isExport);
    const ok = reply('1', 1); request.flush(ok.body, { headers: ok.headers });
    await settle(); fixture.detectChanges();
    expect(button().disabled).toBe(false);
    expect(clicks).toHaveBeenCalledTimes(1);
  });

  it('PUR-INV-EXPORT-UI-004 applying new filters cancels a running export and clears the old result', async () => {
    const { http, fixture, button, view, by } = await create();
    button().click();
    const running = http.expectOne(isExport);
    view.status = 'Draft'; view.search();
    expect(running.cancelled).toBe(true);
    http.expectOne(isList).flush(page(row(1)));
    await settle(); fixture.detectChanges();
    expect(view.exporting()).toBe(false);
    expect(by('pur-invoice-export-notice')).toBeNull();
    expect(clicks).not.toHaveBeenCalled();
  });

  it('PUR-INV-EXPORT-UI-005 without PUR.INVOICE.EXPORT the button, hint and result area are not rendered', async () => {
    granted = ['PUR.INVOICE.VIEW'];
    const { by } = await create();
    expect(by('pur-invoice-export')).toBeNull();
    expect(by('pur-invoice-export-hint')).toBeNull();
  });

  it('PUR-INV-EXPORT-UI-006 export is disabled with a reason for an empty list, no filter results and a failed list', async () => {
    const { button, text, view, http, fixture } = await create(page());
    expect(button().disabled).toBe(true);
    expect(text('pur-invoice-export-hint')).toBe('There are no purchase invoices yet, so there is nothing to export.');
    view.status = 'Posted'; view.search(); fixture.detectChanges();
    expect(text('pur-invoice-export-hint')).toBe('Wait for the invoice list to finish loading.');
    http.expectOne(isList).flush(page()); fixture.detectChanges();
    expect(text('pur-invoice-export-hint')).toBe('No invoices match the applied filters, so there is nothing to export.');
    view.search();
    http.expectOne(isList).flush(JSON.stringify({ traceId: 't' }), { status: 500, statusText: 'Server Error' }); fixture.detectChanges();
    expect(button().disabled).toBe(true);
    expect(text('pur-invoice-export-hint')).toBe('Load the invoice list successfully before exporting.');
    http.expectNone(isExport);
  });

  it('PUR-INV-EXPORT-UI-007 422 limit, 403 and 500 failures read the problem body and are shown; the button is usable again', async () => {
    const { http, fixture, text, button } = await create();
    const fail = async (status: number, body: object) => {
      button().click();
      http.expectOne(isExport).flush(new Blob([JSON.stringify(body)], { type: 'application/problem+json' }), { status, statusText: 'x' });
      await settle(); fixture.detectChanges();
      expect(button().disabled).toBe(false);
      return text('pur-invoice-export-error');
    };
    expect(await fail(422, { errorCode: 'PUR.INVOICE.EXPORT_LIMIT' })).toContain('exceeds 10,000 invoice lines');
    expect(await fail(403, { errorCode: 'COMMON.PERMISSION.DENIED' })).toContain('permission to export purchase invoices');
    expect(await fail(500, { traceId: 'trace-inv-500' })).toContain('trace-inv-500');
    expect(clicks).not.toHaveBeenCalled();
  });
});
