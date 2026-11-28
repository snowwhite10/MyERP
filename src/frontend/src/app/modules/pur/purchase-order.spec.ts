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
import { PurchaseOrderPage } from './purchase-order-page';
import { purchaseOrderExportFailure, purchaseOrderFilterIssue } from './purchase-order.service';

const API = '/api/v1/pur/purchase-orders';
const row = (id: number, status = 'Approved') => ({ id, documentNo: `PO-${id}`, businessDate: '2026-11-20', supplierId: 5, status,
  currency: 'USD', baseGross: 1050.5, lineCount: 2, version: 3 });
const list = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });
/** Raw JSON text so the decimal digits are exactly the server's (12345678901.123456 is not representable as a double). */
const detailJson = (id = 1, history = true) => `{"id":${id},"documentNo":"PO-${id}","status":"Approved","version":3,"supplierId":5,"rfqId":9,
  "rfqDocumentNo":"RFQ-0009","businessDate":"2026-11-20","currency":"USD","baseCurrencyCode":"TWD","exchangeRate":31.5,
  "baseNet":12345678901.123456,"baseTax":5,"baseGross":12345678906.123456,"remark":"Keep terms","ownerId":1,"branchId":null,"departmentId":null,
  "lines":[{"id":11,"lineNo":1,"itemId":77,"requisitionLineId":44,"awardId":21,"quantity":1.123456,"unitPrice":10,"taxRateId":3,"taxRatePercent":5,
  "originalNet":11.23456,"originalTax":0.56,"originalGross":11.79456,"baseNet":1,"baseTax":0,"baseGross":1,"requisitionId":4,
  "requisitionDocumentNo":"PR-0004","requisitionLineNo":2,"requisitionQuantity":10,"requisitionOrderedQuantity":3.5,"requisitionRemainingQuantity":6.5},
  {"id":12,"lineNo":2,"itemId":78,"requisitionLineId":null,"awardId":null,"quantity":2,"unitPrice":1,"taxRateId":3,"taxRatePercent":5,
  "originalNet":2,"originalTax":0.1,"originalGross":2.1,"baseNet":63,"baseTax":3.15,"baseGross":66.15,"requisitionId":null,
  "requisitionDocumentNo":null,"requisitionLineNo":null,"requisitionQuantity":null,"requisitionOrderedQuantity":null,"requisitionRemainingQuantity":null}],
  "approvalHistory":${history ? '[{"instanceId":8,"status":"Approved","submittedAt":"2026-11-21T09:00:00","steps":[{"stepNo":1,"name":"Manager","status":"Approved","actedBy":2,"actedAt":"2026-11-21T10:30:00","comment":"OK to buy"}]},{"instanceId":7,"status":"Rejected","submittedAt":"2026-11-20T09:00:00","steps":[{"stepNo":1,"name":"Manager","status":"Rejected","actedBy":2,"actedAt":"2026-11-20T11:00:00","comment":"Price too high"}]}]' : '[]'}}`;
const csv = (rows: number) => new Blob(['﻿Purchase order no.,Status\r\n' + Array.from({ length: rows }, (_, i) => `"PO-1, ""A""\r\nB",Approved ${i}\r\n`).join('')]);
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0)); };

describe('PUR-PO-UI rules', () => {
  const http = (status: number, error: unknown) => new HttpErrorResponse({ status, error });
  const filter = { keyword: '', from: '', to: '', status: '', supplierId: null, requisitionLineId: null };

  it('PUR-PO-UI-002 filter issues mirror the server 400 rules before any request', () => {
    expect(purchaseOrderFilterIssue(filter)).toBeNull();
    expect(purchaseOrderFilterIssue({ ...filter, from: '2026-02-30' })).toBe('date');
    expect(purchaseOrderFilterIssue({ ...filter, from: '2026-12-01', to: '2026-11-30' })).toBe('range');
    expect(purchaseOrderFilterIssue({ ...filter, from: '2026-11-30', to: '2026-11-30' })).toBeNull();
    expect(purchaseOrderFilterIssue({ ...filter, supplierId: 0 })).toBe('supplier');
    expect(purchaseOrderFilterIssue({ ...filter, supplierId: 1.5 })).toBe('supplier');
    expect(purchaseOrderFilterIssue({ ...filter, requisitionLineId: -1 })).toBe('source');
  });

  it('PUR-PO-UI-007 export failures name the limit, permission, filters, timeout, network and rate limit; 5xx keeps the trace', () => {
    expect(purchaseOrderExportFailure(http(422, { errorCode: 'PUR.ORDER.EXPORT_LIMIT' })).key).toBe('errors.PUR.ORDER.EXPORT_LIMIT');
    expect(purchaseOrderExportFailure(http(403, {})).key).toBe('pur.order.exportDenied');
    expect(purchaseOrderExportFailure(http(400, { errors: { sort: ['Choose one sort field.'] } })))
      .toEqual({ key: 'pur.order.exportInvalid', traceId: null, details: ['sort: Choose one sort field.'] });
    expect(purchaseOrderExportFailure(new TimeoutError()).key).toBe('pur.order.exportTimeout');
    expect(purchaseOrderExportFailure(http(0, null)).key).toBe('errors.http.network');
    expect(purchaseOrderExportFailure(http(429, null)).key).toBe('errors.COMMON.REQUEST.RATE_LIMITED');
    expect(purchaseOrderExportFailure(http(500, { traceId: 't-500' }))).toEqual({ key: 'pur.order.exportFailed', traceId: 't-500', details: [] });
  });

  it('every order text and error exists in all three languages', () => {
    const keys = (value: unknown, prefix = ''): string[] => typeof value === 'object' && value !== null
      ? Object.entries(value).flatMap(([k, v]) => keys(v, prefix + k + '.')) : [prefix];
    const en = keys(enPur.order).sort();
    expect(en.length).toBeGreaterThan(60);
    expect(keys(hantPur.order).sort()).toEqual(en);
    expect(keys(hansPur.order).sort()).toEqual(en);
    for (const errors of [enErrors, hantErrors, hansErrors]) expect(Object.keys(errors.PUR.ORDER).sort()).toEqual(['AMOUNT_OVERFLOW', 'BASE_CURRENCY_INACTIVE', 'CURRENCY_UNAVAILABLE', 'EMPTY', 'EXPORT_LIMIT', 'HAS_RECEIPTS', 'INVALID_RATE', 'INVALID_REQUISITION_SOURCE', 'NOT_CANCELLABLE', 'NOT_DELETABLE', 'NOT_FOUND', 'NOT_SUBMITTABLE', 'NO_BASE_CURRENCY', 'REQUISITION_INSUFFICIENT']);
  });
});

describe('PUR-PO-UI 介面 4.1 purchase order page', () => {
  let granted: string[];
  let clicks: ReturnType<typeof vi.spyOn>;
  let printSpy: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    granted = ['PUR.ORDER.VIEW', 'PUR.ORDER.EXPORT'];
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: signal({ id: 1 }) } },
      { provide: PermissionService, useValue: { has: (c: string) => granted.includes(c), hasAny: (c: string[]) => c.some(x => granted.includes(x)) } }] });
    URL.createObjectURL = vi.fn(() => 'blob:order');
    URL.revokeObjectURL = vi.fn();
    clicks = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });
  afterEach(() => { clicks.mockRestore(); printSpy.mockRestore(); TestBed.inject(HttpTestingController).verify(); });

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const isExport = (r: HttpRequest<unknown>) => r.url === `${API}/export`;
  const isDetail = (id: number) => (r: HttpRequest<unknown>) => r.url === `${API}/${id}`;
  async function create(body = list(row(1), row(2, 'PendingApproval'))) {
    const fixture = TestBed.createComponent(PurchaseOrderPage);
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
    ctx.view.view(row(1) as never);
    ctx.http.expectOne(isDetail(1)).flush(body);
    ctx.fixture.detectChanges(); await settle(); ctx.fixture.detectChanges();
  }

  it('PUR-PO-UI-001 lists saved orders and opens the saved detail with sources, exact decimals and every approval round', async () => {
    const ctx = await create();
    expect(ctx.text('pur-order-row-1')).toContain('PO-1');
    expect(ctx.text('pur-order-row-1')).toContain('1,050.5');
    expect(ctx.text('pur-order-row-2')).toContain('Pending approval');
    await open(ctx);
    const detail = ctx.text('pur-order-detail')!;
    expect(ctx.text('pur-order-detail-gross')).toBe('12,345,678,906.123456');
    expect(ctx.text('pur-order-detail-rfq')).toBe('RFQ-0009');
    expect(ctx.text('pur-order-line-1')).toContain('1.123456');
    expect(ctx.text('pur-order-line-1')).toContain('PR-0004 #2');
    expect(ctx.text('pur-order-line-1')).toContain('6.5');
    expect(ctx.text('pur-order-line-2')).toContain('—');
    expect(ctx.text('pur-order-round-7')).toContain('Price too high');
    expect(ctx.text('pur-order-round-8')).toContain('OK to buy');
    expect(detail.indexOf('OK to buy')).toBeLessThan(detail.indexOf('Price too high'));
    expect(ctx.element.querySelector('[data-testid="pur-order-submit"],[data-testid="pur-order-cancel"]')).toBeNull();
  });

  it('PUR-PO-UI-002 manual orders without history and the three empty states are distinct', async () => {
    const ctx = await create(list());
    expect(ctx.text('pur-order-table')).toContain('No purchase orders yet.');
    ctx.view.filter.status = 'Cancelled'; ctx.view.search();
    ctx.http.expectOne(isList).flush(list()); ctx.fixture.detectChanges();
    expect(ctx.text('pur-order-table')).toContain('No purchase orders match these filters.');
    ctx.view.search();
    ctx.http.expectOne(isList).flush(JSON.stringify({ traceId: 'list-500' }), { status: 500, statusText: 'x' }); ctx.fixture.detectChanges();
    expect(ctx.text('pur-order-table')).toContain('Could not load purchase orders.');
    expect(ctx.text('pur-order-error')).toContain('list-500');
    ctx.view.filter.from = '2026-12-01'; ctx.view.filter.to = '2026-11-01'; ctx.view.search(); ctx.fixture.detectChanges();
    expect(ctx.text('pur-order-filter-error')).toBe('The end date must not be earlier than the start date.');
    ctx.http.expectNone(isList);
    ctx.view.filter.from = ''; ctx.view.filter.to = ''; ctx.view.search();
    ctx.http.expectOne(isList).flush(list(row(1))); ctx.fixture.detectChanges();
    expect(ctx.by('pur-order-filter-error')).toBeNull();
    await open(ctx, detailJson(1, false).replace('"rfqDocumentNo":"RFQ-0009"', '"rfqDocumentNo":null'));
    expect(ctx.text('pur-order-detail-rfq')).toBe('Manual order (no RFQ)');
    expect(ctx.text('pur-order-no-history')).toBe('Not submitted for approval yet.');
  });

  it('PUR-PO-UI-003 export sends applied filters and sort without paging, saves only a checked file and reports its line count', async () => {
    const ctx = await create();
    expect(ctx.text('pur-order-export-hint')).toContain('applied filters and sort');
    Object.assign(ctx.view.filter, { keyword: ' PO ', status: 'Approved', supplierId: 5, requisitionLineId: 44 }); ctx.view.sort = 'documentNo';
    ctx.view.search(); ctx.http.expectOne(isList).flush(list(row(1)));
    ctx.view.filter.keyword = 'typed-not-applied'; ctx.fixture.detectChanges();
    ctx.by('pur-order-export')!.click();
    const request = ctx.http.expectOne(isExport);
    expect(Object.fromEntries(request.request.params.keys().map(k => [k, request.request.params.get(k)]))).toEqual({
      keyword: 'PO', status: 'Approved', supplierId: '5', requisitionLineId: '44', sort: 'documentNo' });
    request.flush(csv(2), { headers: { 'X-Export-Row-Count': '2' } });
    await settle(); ctx.fixture.detectChanges();
    expect(clicks).toHaveBeenCalledTimes(1);
    expect((clicks.mock.contexts[0] as HTMLAnchorElement).download).toBe('purchase-orders.csv');
    expect(ctx.text('pur-order-export-notice')).toBe('Exported 2 order lines to purchase-orders.csv.');
    expect(ctx.element.querySelector('.ant-alert-success')).not.toBeNull();
  });

  it('PUR-PO-UI-003 zero lines warn without a file; a row-count mismatch is refused', async () => {
    const ctx = await create();
    ctx.by('pur-order-export')!.click();
    ctx.http.expectOne(isExport).flush(csv(0), { headers: { 'X-Export-Row-Count': '0' } });
    await settle(); ctx.fixture.detectChanges();
    expect(ctx.text('pur-order-export-notice')).toBe('No order lines match the applied filters. No file was downloaded.');
    expect(ctx.element.querySelector('.ant-alert-warning')).not.toBeNull();
    ctx.by('pur-order-export')!.click();
    ctx.http.expectOne(isExport).flush(csv(1), { headers: { 'X-Export-Row-Count': '3' } });
    await settle(); ctx.fixture.detectChanges();
    expect(ctx.by('pur-order-export-notice')).toBeNull();
    expect(ctx.text('pur-order-export-error')).toContain('row count did not match');
    expect(clicks).not.toHaveBeenCalled();
  });

  it('PUR-PO-UI-004 print re-reads the saved detail and prints the fresh version; a failed read never prints', async () => {
    const ctx = await create();
    await open(ctx);
    ctx.by('pur-order-print')!.click(); ctx.fixture.detectChanges();
    expect(ctx.text('pur-order-print')).toBe('Loading saved order…');
    ctx.http.expectOne(isDetail(1)).flush(detailJson().replace('"version":3', '"version":4').replace('"remark":"Keep terms"', '"remark":"Fresh"'));
    await settle(); ctx.fixture.detectChanges();
    expect(printSpy).toHaveBeenCalledTimes(1);
    expect(ctx.text('pur-order-detail')).toContain('Fresh');
    ctx.by('pur-order-print')!.click();
    ctx.http.expectOne(isDetail(1)).flush(JSON.stringify({ traceId: 'print-500' }), { status: 500, statusText: 'x' });
    await settle(); ctx.fixture.detectChanges();
    expect(printSpy).toHaveBeenCalledTimes(1);
    expect(ctx.text('pur-order-error')).toContain('print-500');
    ctx.by('pur-order-print')!.click();
    ctx.http.expectOne(isDetail(1)).flush(JSON.stringify({ errorCode: 'PUR.ORDER.NOT_FOUND' }), { status: 404, statusText: 'x' });
    await settle();
    ctx.http.expectOne(isList).flush(list(row(2))); ctx.fixture.detectChanges();
    expect(printSpy).toHaveBeenCalledTimes(1);
    expect(ctx.text('pur-order-error')).toContain('This purchase order was not found');
    expect(ctx.by('pur-order-detail')).toBeNull();
    expect(ctx.by('pur-order-row-1')).toBeNull();
  });

  it('PUR-PO-UI-005 without PUR.ORDER.EXPORT the export button and hint are not rendered', async () => {
    granted = ['PUR.ORDER.VIEW'];
    const ctx = await create();
    expect(ctx.by('pur-order-export')).toBeNull();
    expect(ctx.by('pur-order-export-hint')).toBeNull();
  });

  it('PUR-PO-UI-006 five statuses have their own labels', async () => {
    const ctx = await create(list(row(1, 'Draft'), row(2, 'PendingApproval'), row(3, 'Approved'), row(4, 'Rejected'), row(5, 'Cancelled')));
    expect([1, 2, 3, 4, 5].map(id => ctx.element.querySelector(`[data-testid="pur-order-row-${id}"] .erp-status-tag`)?.textContent?.trim()))
      .toEqual(['Draft', 'Pending approval', 'Approved', 'Rejected', 'Cancelled']);
  });

  it('PUR-PO-UI-007 export failures are shown, busy and empty states explain the disabled button, and a new search cancels the export', async () => {
    const ctx = await create();
    const button = () => ctx.by('pur-order-export') as HTMLButtonElement;
    const fail = async (status: number, body: object) => {
      button().click();
      ctx.http.expectOne(isExport).flush(new Blob([JSON.stringify(body)], { type: 'application/problem+json' }), { status, statusText: 'x' });
      await settle(); ctx.fixture.detectChanges();
      expect(button().disabled).toBe(false);
      return ctx.text('pur-order-export-error');
    };
    expect(await fail(422, { errorCode: 'PUR.ORDER.EXPORT_LIMIT' })).toContain('exceeds 10,000 purchase order lines');
    expect(await fail(403, { errorCode: 'COMMON.PERMISSION.DENIED' })).toContain('permission to export purchase orders');
    expect(await fail(500, { traceId: 'trace-po-500' })).toContain('trace-po-500');
    button().click(); ctx.fixture.detectChanges();
    expect(button().disabled).toBe(true);
    expect(ctx.text('pur-order-export-hint')).toBe('Export in progress. Wait for it to finish.');
    const running = ctx.http.expectOne(isExport);
    ctx.view.filter.status = 'Draft'; ctx.view.search();
    expect(running.cancelled).toBe(true);
    ctx.http.expectOne(isList).flush(list()); ctx.fixture.detectChanges();
    expect(button().disabled).toBe(true);
    expect(ctx.text('pur-order-export-hint')).toBe('No orders match the applied filters, so there is nothing to export.');
    expect(clicks).not.toHaveBeenCalled();
  });
});
