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
import enErrors from '../../../../public/i18n/en/errors.json';
import hansErrors from '../../../../public/i18n/zh-Hans/errors.json';
import hantErrors from '../../../../public/i18n/zh-Hant/errors.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { PurchaseReceiptPage } from './purchase-receipt-page';
import { purchaseReceiptExportFailure, purchaseReceiptFilterIssue } from './purchase-receipt.service';

const API = '/api/v1/pur/purchase-receipts';
const row = (id: number, status = 'Posted', order: string | null = `PO-${id}`) => `{"id":${id},"documentNo":"GR-${id}","businessDate":"2026-11-20",
  "purchaseOrderId":${40 + id},"purchaseOrderDocumentNo":${order === null ? 'null' : `"${order}"`},"supplierId":5,"status":"${status}",
  "currency":"USD","lineCount":2,"totalQuantity":12345.123456,"version":3}`;
const list = (...rows: string[]) => `{"items":[${rows.join(',')}],"totalCount":${rows.length},"page":1,"pageSize":20}`;
/** Raw JSON text so the decimal digits are exactly the server's (1234567890123.123456 is not representable as a double). */
const detailJson = (id = 1, status = 'Posted', lines = true) => `{"id":${id},"documentNo":"GR-${id}","status":"${status}","version":3,
  "purchaseOrderId":${40 + id},"supplierId":5,"currency":"USD","businessDate":"2026-11-20","lines":${lines ? `[
  {"id":11,"lineNumber":1,"purchaseOrderLineId":71,"materialId":77,"quantity":1234567890123.123456,"warehouseId":3,"unitCost":10.5,
   "tracking":[{"seqNo":1,"lotNo":"L-01","serialNo":null,"quantity":1.5,"manufactureDate":"2026-10-01","expiryDate":"2027-10-01"},
               {"seqNo":2,"lotNo":"L-02","serialNo":null,"quantity":2,"manufactureDate":null,"expiryDate":null}]},
  {"id":12,"lineNumber":2,"purchaseOrderLineId":72,"materialId":78,"quantity":1,"warehouseId":4,"unitCost":0.000001,
   "tracking":[{"seqNo":1,"lotNo":null,"serialNo":"SN-9","quantity":1,"manufactureDate":null,"expiryDate":null}]},
  {"id":13,"lineNumber":3,"purchaseOrderLineId":73,"materialId":79,"quantity":4,"warehouseId":4,"unitCost":2,"tracking":[]}]` : '[]'}}`;
const csv = (rows: number) => new Blob(['﻿Receipt no.,Status\r\n' + Array.from({ length: rows }, (_, i) => `"GR-1, ""A""",Posted ${i}\r\n`).join('')]);
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0)); };

describe('PUR-RECV-UI rules', () => {
  const http = (status: number, error: unknown) => new HttpErrorResponse({ status, error });
  const filter = { keyword: '', from: '', to: '', status: '', purchaseOrderId: null };

  it('PUR-RECV-UI-002 filter issues mirror the server 400 rules before any request', () => {
    expect(purchaseReceiptFilterIssue(filter)).toBeNull();
    expect(purchaseReceiptFilterIssue({ ...filter, to: '2026-02-30' })).toBe('date');
    expect(purchaseReceiptFilterIssue({ ...filter, from: '2028-02-29', to: '2028-02-28' })).toBe('range');
    expect(purchaseReceiptFilterIssue({ ...filter, from: '2026-12-31', to: '2026-12-31' })).toBeNull();
    expect(purchaseReceiptFilterIssue({ ...filter, purchaseOrderId: 0 })).toBe('order');
    expect(purchaseReceiptFilterIssue({ ...filter, purchaseOrderId: 2.5 })).toBe('order');
    expect(purchaseReceiptFilterIssue({ ...filter, purchaseOrderId: 7 })).toBeNull();
  });

  it('PUR-RECV-UI-007 export failures name the limit, permission, filters, timeout, network and rate limit; 5xx keeps the trace', () => {
    expect(purchaseReceiptExportFailure(http(422, { errorCode: 'PUR.RECEIPT.EXPORT_LIMIT' })).key).toBe('errors.PUR.RECEIPT.EXPORT_LIMIT');
    expect(purchaseReceiptExportFailure(http(403, {})).key).toBe('pur.receipt.exportDenied');
    expect(purchaseReceiptExportFailure(http(400, { errors: { status: ['Unknown purchase receipt status.'] } })))
      .toEqual({ key: 'pur.receipt.exportInvalid', traceId: null, details: ['status: Unknown purchase receipt status.'] });
    expect(purchaseReceiptExportFailure(new TimeoutError()).key).toBe('pur.receipt.exportTimeout');
    expect(purchaseReceiptExportFailure(http(0, null)).key).toBe('errors.http.network');
    expect(purchaseReceiptExportFailure(http(429, null)).key).toBe('errors.COMMON.REQUEST.RATE_LIMITED');
    expect(purchaseReceiptExportFailure(http(500, { traceId: 't-500' }))).toEqual({ key: 'pur.receipt.exportFailed', traceId: 't-500', details: [] });
  });

  it('PUR-RECV-UI-008 every receipt text and error exists in all three languages', () => {
    const keys = (value: unknown, prefix = ''): string[] => typeof value === 'object' && value !== null
      ? Object.entries(value).flatMap(([k, v]) => keys(v, prefix + k + '.')) : [prefix];
    const en = keys(enPur.receipt).sort();
    expect(en.length).toBeGreaterThan(50);
    expect(keys(hantPur.receipt).sort()).toEqual(en);
    expect(keys(hansPur.receipt).sort()).toEqual(en);
    for (const errors of [enErrors, hantErrors, hansErrors]) expect(Object.keys(errors.PUR.RECEIPT).sort()).toEqual(['EXPORT_LIMIT', 'NOT_FOUND']);
  });
});

describe('PUR-RECV-UI 介面 4.4 purchase receipt page', () => {
  let granted: string[];
  let clicks: ReturnType<typeof vi.spyOn>;
  let printSpy: ReturnType<typeof vi.spyOn>;
  let company: ReturnType<typeof signal<{ id: number }>>;
  beforeEach(() => {
    granted = ['PUR.RECEIPT.VIEW', 'PUR.RECEIPT.EXPORT'];
    company = signal({ id: 1 });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (c: string) => granted.includes(c), hasAny: (c: string[]) => c.some(x => granted.includes(x)) } }] });
    URL.createObjectURL = vi.fn(() => 'blob:receipt');
    URL.revokeObjectURL = vi.fn();
    clicks = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });
  afterEach(() => { clicks.mockRestore(); printSpy.mockRestore(); TestBed.inject(HttpTestingController).verify(); });

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const isExport = (r: HttpRequest<unknown>) => r.url === `${API}/export`;
  const isDetail = (id: number) => (r: HttpRequest<unknown>) => r.url === `${API}/${id}`;
  async function create(body = list(row(1), row(2, 'PendingApproval', null))) {
    const fixture = TestBed.createComponent(PurchaseReceiptPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(body);
    fixture.detectChanges(); await settle();
    for (const reload of http.match(isList)) reload.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, http, by, text, element, view: fixture.componentInstance };
  }
  async function open(ctx: Awaited<ReturnType<typeof create>>, body = detailJson()) {
    ctx.view.view({ id: 1 } as never);
    ctx.http.expectOne(isDetail(1)).flush(body);
    ctx.fixture.detectChanges(); await settle(); ctx.fixture.detectChanges();
  }

  it('PUR-RECV-UI-001 lists saved receipts and opens the saved detail with exact quantities, costs and lot / serial capture', async () => {
    const ctx = await create();
    expect(ctx.text('pur-receipt-row-1')).toContain('GR-1');
    expect(ctx.text('pur-receipt-row-1')).toContain('PO-1');
    expect(ctx.text('pur-receipt-row-1')).toContain('12,345.123456');
    expect(ctx.text('pur-receipt-row-2')).toContain('Order ID 42');
    expect(ctx.text('pur-receipt-row-2')).toContain('Pending approval');
    await open(ctx);
    expect(ctx.text('pur-receipt-detail-order')).toBe('PO-1');
    expect(ctx.text('pur-receipt-line-1')).toContain('1,234,567,890,123.123456');
    expect(ctx.text('pur-receipt-line-1')).toContain('Lot L-01 · 1.5 · made 2026-10-01 · expires 2027-10-01');
    expect(ctx.text('pur-receipt-line-1')).toContain('Lot L-02 · 2');
    expect(ctx.text('pur-receipt-line-2')).toContain('0.000001');
    expect(ctx.text('pur-receipt-line-2')).toContain('Serial SN-9');
    expect(ctx.text('pur-receipt-line-3')).toContain('—');
    expect(ctx.text('pur-receipt-stock-hint')).toBe('Posted: the goods were added to stock at the saved unit cost.');
  });

  it('PUR-RECV-UI-002 the three empty states, a list 500 with trace ID and an empty saved receipt are distinct', async () => {
    const ctx = await create(list());
    expect(ctx.text('pur-receipt-table')).toContain('No purchase receipts yet.');
    ctx.view.filter.status = 'Rejected'; ctx.view.search();
    ctx.http.expectOne(isList).flush(list()); ctx.fixture.detectChanges();
    expect(ctx.text('pur-receipt-table')).toContain('No purchase receipts match these filters.');
    ctx.view.search();
    ctx.http.expectOne(isList).flush(JSON.stringify({ traceId: 'list-500' }), { status: 500, statusText: 'x' }); ctx.fixture.detectChanges();
    expect(ctx.text('pur-receipt-table')).toContain('Could not load purchase receipts.');
    expect(ctx.text('pur-receipt-error')).toContain('list-500');
    ctx.view.filter.purchaseOrderId = 0; ctx.view.search(); ctx.fixture.detectChanges();
    expect(ctx.text('pur-receipt-filter-error')).toBe('Purchase order ID must be a positive whole number.');
    ctx.http.expectNone(isList);
    ctx.view.filter.purchaseOrderId = null; ctx.view.filter.status = ''; ctx.view.search();
    ctx.http.expectOne(isList).flush(list(row(1, 'Draft'))); ctx.fixture.detectChanges();
    expect(ctx.by('pur-receipt-filter-error')).toBeNull();
    await open(ctx, detailJson(1, 'Draft', false));
    expect(ctx.text('pur-receipt-no-lines')).toBe('This receipt has no lines.');
    expect(ctx.text('pur-receipt-stock-hint')).toContain('Stock and the order\'s received quantity are unchanged.');
  });

  it('PUR-RECV-UI-003 export sends applied filters and sort without paging, saves only a checked file and reports its line count', async () => {
    const ctx = await create();
    expect(ctx.text('pur-receipt-export-hint')).toContain('applied filters and sort');
    Object.assign(ctx.view.filter, { keyword: ' PO-4 ', status: 'Posted', from: '2026-11-01', to: '2026-11-30', purchaseOrderId: 41 });
    ctx.view.sort = 'documentNo';
    ctx.view.search(); ctx.http.expectOne(isList).flush(list(row(1)));
    ctx.view.filter.keyword = 'typed-not-applied'; ctx.fixture.detectChanges();
    ctx.by('pur-receipt-export')!.click();
    const request = ctx.http.expectOne(isExport);
    expect(Object.fromEntries(request.request.params.keys().map(k => [k, request.request.params.get(k)]))).toEqual({
      keyword: 'PO-4', status: 'Posted', from: '2026-11-01', to: '2026-11-30', purchaseOrderId: '41', sort: 'documentNo' });
    request.flush(csv(2), { headers: { 'X-Export-Row-Count': '2' } });
    await settle(); ctx.fixture.detectChanges();
    expect(clicks).toHaveBeenCalledTimes(1);
    expect((clicks.mock.contexts[0] as HTMLAnchorElement).download).toBe('purchase-receipts.csv');
    expect(ctx.text('pur-receipt-export-notice')).toBe('Exported 2 receipt lines to purchase-receipts.csv.');
    expect(ctx.element.querySelector('.ant-alert-success')).not.toBeNull();
  });

  it('PUR-RECV-UI-003 zero lines warn without a file; a row-count mismatch is refused', async () => {
    const ctx = await create();
    ctx.by('pur-receipt-export')!.click();
    ctx.http.expectOne(isExport).flush(csv(0), { headers: { 'X-Export-Row-Count': '0' } });
    await settle(); ctx.fixture.detectChanges();
    expect(ctx.text('pur-receipt-export-notice')).toBe('No receipt lines match the applied filters. No file was downloaded.');
    expect(ctx.element.querySelector('.ant-alert-warning')).not.toBeNull();
    ctx.by('pur-receipt-export')!.click();
    ctx.http.expectOne(isExport).flush(csv(1), { headers: { 'X-Export-Row-Count': '3' } });
    await settle(); ctx.fixture.detectChanges();
    expect(ctx.by('pur-receipt-export-notice')).toBeNull();
    expect(ctx.text('pur-receipt-export-error')).toContain('row count did not match');
    expect(clicks).not.toHaveBeenCalled();
  });

  it('PUR-RECV-UI-004 print re-reads the saved detail and prints the fresh version; a failed read never prints; 404 reloads the list', async () => {
    const ctx = await create();
    await open(ctx);
    ctx.by('pur-receipt-print')!.click(); ctx.fixture.detectChanges();
    expect(ctx.text('pur-receipt-print')).toBe('Loading saved receipt…');
    ctx.http.expectOne(isDetail(1)).flush(detailJson().replace('"version":3', '"version":4').replace('"lotNo":"L-02"', '"lotNo":"L-FRESH"'));
    await settle(); ctx.fixture.detectChanges();
    expect(printSpy).toHaveBeenCalledTimes(1);
    expect(ctx.text('pur-receipt-detail')).toContain('L-FRESH');
    ctx.by('pur-receipt-print')!.click();
    ctx.http.expectOne(isDetail(1)).flush(JSON.stringify({ traceId: 'print-500' }), { status: 500, statusText: 'x' });
    await settle(); ctx.fixture.detectChanges();
    expect(printSpy).toHaveBeenCalledTimes(1);
    expect(ctx.text('pur-receipt-error')).toContain('print-500');
    ctx.by('pur-receipt-print')!.click();
    ctx.http.expectOne(isDetail(1)).flush(JSON.stringify({ errorCode: 'PUR.RECEIPT.NOT_FOUND' }), { status: 404, statusText: 'x' });
    await settle();
    ctx.http.expectOne(isList).flush(list(row(2))); ctx.fixture.detectChanges();
    expect(printSpy).toHaveBeenCalledTimes(1);
    expect(ctx.text('pur-receipt-error')).toContain('This purchase receipt was not found');
    expect(ctx.by('pur-receipt-detail')).toBeNull();
    expect(ctx.by('pur-receipt-row-1')).toBeNull();
  });

  it('PUR-RECV-UI-005 without PUR.RECEIPT.EXPORT the export button and hint are not rendered', async () => {
    granted = ['PUR.RECEIPT.VIEW'];
    const ctx = await create();
    expect(ctx.by('pur-receipt-export')).toBeNull();
    expect(ctx.by('pur-receipt-export-hint')).toBeNull();
  });

  it('PUR-RECV-UI-006 four statuses have their own labels and stock explanations', async () => {
    const ctx = await create(list(row(1, 'Draft'), row(2, 'PendingApproval'), row(3, 'Rejected'), row(4, 'Posted')));
    expect([1, 2, 3, 4].map(id => ctx.element.querySelector(`[data-testid="pur-receipt-row-${id}"] .erp-status-tag`)?.textContent?.trim()))
      .toEqual(['Draft', 'Pending approval', 'Rejected', 'Posted']);
    await open(ctx, detailJson(1, 'PendingApproval'));
    expect(ctx.text('pur-receipt-stock-hint')).toContain('Stock is added only after final approval.');
    await open(ctx, detailJson(1, 'Rejected'));
    expect(ctx.text('pur-receipt-stock-hint')).toContain('reserved quantity was released and stock is unchanged');
  });

  it('PUR-RECV-UI-007 export failures are shown, busy and empty states explain the disabled button, and a new search cancels the export', async () => {
    const ctx = await create();
    const button = () => ctx.by('pur-receipt-export') as HTMLButtonElement;
    const fail = async (status: number, body: object) => {
      button().click();
      ctx.http.expectOne(isExport).flush(new Blob([JSON.stringify(body)], { type: 'application/problem+json' }), { status, statusText: 'x' });
      await settle(); ctx.fixture.detectChanges();
      expect(button().disabled).toBe(false);
      return ctx.text('pur-receipt-export-error');
    };
    expect(await fail(422, { errorCode: 'PUR.RECEIPT.EXPORT_LIMIT' })).toContain('exceeds 10,000 purchase receipt lines');
    expect(await fail(403, { errorCode: 'COMMON.PERMISSION.DENIED' })).toContain('permission to export purchase receipts');
    expect(await fail(500, { traceId: 'trace-gr-500' })).toContain('trace-gr-500');
    button().click(); ctx.fixture.detectChanges();
    expect(button().disabled).toBe(true);
    expect(ctx.text('pur-receipt-export-hint')).toBe('Export in progress. Wait for it to finish.');
    const running = ctx.http.expectOne(isExport);
    ctx.view.filter.status = 'Draft'; ctx.view.search();
    expect(running.cancelled).toBe(true);
    ctx.http.expectOne(isList).flush(list()); ctx.fixture.detectChanges();
    expect(button().disabled).toBe(true);
    expect(ctx.text('pur-receipt-export-hint')).toBe('No receipts match the applied filters, so there is nothing to export.');
    expect(clicks).not.toHaveBeenCalled();
  });

  it('PUR-RECV-UI-009 switching company re-reads the list and clears the open detail', async () => {
    const ctx = await create();
    await open(ctx);
    expect(ctx.by('pur-receipt-detail')).not.toBeNull();
    company.set({ id: 2 }); TestBed.tick();
    ctx.http.expectOne(isList).flush(list(row(9))); ctx.fixture.detectChanges();
    expect(ctx.by('pur-receipt-detail')).toBeNull();
    expect(ctx.by('pur-receipt-row-1')).toBeNull();
    expect(ctx.text('pur-receipt-row-9')).toContain('GR-9');
  });
});
