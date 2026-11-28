import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { PurchaseOrderForm } from './purchase-order-form';
import { PurchaseOrderPage } from './purchase-order-page';
import { purchaseOrderBody } from './purchase-order.service';

const API = '/api/v1/pur/purchase-orders';
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0)); };
const manualLine = `{"id":91,"lineNo":1,"itemId":7,"requisitionLineId":44,"awardId":null,"quantity":2.5,"unitPrice":100.123456,
  "taxRateId":3,"taxRatePercent":5,"originalNet":250.3086,"originalTax":12.5154,"originalGross":262.824,"baseNet":250.3086,
  "baseTax":12.5154,"baseGross":262.824,"requisitionId":4,"requisitionDocumentNo":"PR-0004","requisitionLineNo":2,
  "requisitionQuantity":5.123456,"requisitionOrderedQuantity":2,"requisitionRemainingQuantity":3.123456}`;
const awardLine = `{"id":92,"lineNo":2,"itemId":8,"requisitionLineId":null,"awardId":70,"quantity":4,"unitPrice":12.5,
  "taxRateId":6,"taxRatePercent":10,"originalNet":50,"originalTax":5,"originalGross":55,"baseNet":50,"baseTax":5,"baseGross":55,
  "requisitionId":null,"requisitionDocumentNo":null,"requisitionLineNo":null,"requisitionQuantity":null,
  "requisitionOrderedQuantity":null,"requisitionRemainingQuantity":null}`;
const detail = (status = 'Rejected', version = 3, lines = [manualLine, awardLine]) => `{"id":9,"documentNo":"PO-9","status":"${status}",
  "version":${version},"supplierId":5,"rfqId":12,"rfqDocumentNo":"RFQ-12","businessDate":"2028-02-29","currency":"USD",
  "baseCurrencyCode":"TWD","exchangeRate":31.5,"baseNet":300.3086,"baseTax":17.5154,"baseGross":317.824,"remark":"first",
  "lines":[${lines.join(',')}],"approvalHistory":[]}`;
const context = JSON.stringify({ businessDate: '2028-02-29', currencies: [{ code: 'TWD', name: 'New Taiwan Dollar' }],
  taxRates: [{ id: 3, code: 'VAT3', name: 'Input 5%', ratePercent: '5.000000' }] });
const saved = (status = 'Draft') => JSON.stringify({ id: 9, documentNo: 'PO-9', status, version: 4, lines: [{}, {}] });

describe('PUR-PO-UI 介面 4.3 body (WU-PUR-006f3)', () => {
  it('PUR-PO-UI-020 awarded lines keep their saved line number; other lines take the next unused numbers in screen order', () => {
    const body = purchaseOrderBody({ supplierId: 5, businessDate: '2028-02-29', currencyCode: 'usd', remark: '', lines: [
      { itemId: 8, requisitionLineId: null, quantity: '4', unitPrice: '12.5', taxRateId: 6, lineNo: 2, awardId: 70 },
      { itemId: 7, requisitionLineId: null, quantity: '1', unitPrice: '0', taxRateId: 3 },
      { itemId: 9, requisitionLineId: null, quantity: '2', unitPrice: '1', taxRateId: 3 }] }) as { lines: { lineNo: number }[] };
    expect(body.lines.map(l => l.lineNo)).toEqual([2, 1, 3]);
    expect(Object.keys(body.lines[0])).toEqual(['lineNo', 'itemId', 'requisitionLineId', 'quantity', 'unitPrice', 'currencyCode', 'taxRateId']);
  });
});

describe('PUR-PO-UI 介面 4.3 revision form (WU-PUR-006f3)', () => {
  let granted: string[];
  let success: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    granted = ['PUR.ORDER.EDIT', 'PUR.ORDER.VIEW'];
    success = vi.fn(); confirm = vi.fn(() => of(true));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([{ path: 'pur/purchase-orders/:id/edit', component: PurchaseOrderForm }]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { select: vi.fn(() => of(null)), success, confirm } },
      { provide: CompanyContextService, useValue: { current: signal({ id: 1 }) } },
      { provide: PermissionService, useValue: { has: (c: string) => granted.includes(c), hasAny: (c: string[]) => c.some(x => granted.includes(x)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isEdit = (r: HttpRequest<unknown>) => r.url === `${API}/9/edit` && r.method === 'GET';
  const isContext = (r: HttpRequest<unknown>) => r.url === `${API}/create-context` && r.method === 'GET';
  const isPut = (r: HttpRequest<unknown>) => r.url === `${API}/9` && r.method === 'PUT';
  async function open(body = detail()) {
    const harness = await RouterTestingHarness.create('/pur/purchase-orders/9/edit');
    const http = TestBed.inject(HttpTestingController);
    const form = harness.routeDebugElement!.componentInstance as PurchaseOrderForm;
    const element = harness.routeNativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const refresh = async () => { harness.detectChanges(); await settle(); harness.detectChanges(); };
    await refresh();
    expect(text('pur-po-form-loading')).toBe('Loading the saved purchase order…');
    http.expectOne(isEdit).flush(body);
    await refresh();
    for (const req of http.match(isContext)) { expect(req.request.params.get('businessDate')).toBe('2028-02-29'); req.flush(context); }
    await refresh();
    return { harness, http, form, by, text, refresh };
  }

  it('PUR-PO-UI-021 loads the saved order: labels fall back to #ID, the source shows exact quantities and awarded data is read-only', async () => {
    const ctx = await open();
    expect(ctx.text('pur-po-form-title')).toBe('Revise purchase order PO-9');
    expect(ctx.text('pur-po-form-rejected')).toBe('This order was rejected. Saving the revision returns it to Draft; submit it again for approval.');
    expect(ctx.text('pur-po-form-supplier')).toBe('#5');
    expect(ctx.text('pur-po-form-item-0')).toBe('#7');
    expect(ctx.text('pur-po-form-source-detail-0')).toBe('PR-0004 / line 2 · Requested 5.123456, converted 2, remaining 3.123456');
    expect(ctx.form.draft.lines[0].quantity).toBe('2.5');
    // Award snapshot: supplier and currency locked, the awarded line shown as text and never cleared by the new tax list.
    expect((ctx.by('pur-po-form-supplier') as HTMLButtonElement).disabled).toBe(true);
    expect(ctx.text('pur-po-form-award-locked')).toBe('This order was converted from quotation awards: the supplier and currency keep the award snapshot and cannot change.');
    expect(ctx.text('pur-po-form-currency-locked')).toBe('USD');
    expect(ctx.text('pur-po-form-award-1')).toBe('Awarded line (item, source, quantity, unit price and tax rate cannot change): item #8 · quantity 4 · unit price 12.5 · tax rate 10%');
    expect(ctx.by('pur-po-form-quantity-1')).toBeNull();
    expect((ctx.by('pur-po-form-remove-1') as HTMLButtonElement).disabled).toBe(true);
    expect(ctx.by('pur-po-form-remove-1')!.getAttribute('title')).toBe('An awarded line cannot be removed.');
    expect(ctx.form.draft.lines[1].taxRateId).toBe(6);
    expect(ctx.form.draft.currencyCode).toBe('USD');
    expect(ctx.by('pur-po-form-stale')).toBeNull();
    expect(ctx.form.hasUnsavedChanges()).toBe(false);
    expect(ctx.by('pur-po-form-back')!.getAttribute('href')).toBe('/pur/purchase-orders?id=9');
  });

  it('PUR-PO-UI-022 saves the revision with the loaded version, keeps the award line number and explains Rejected → Draft', async () => {
    const ctx = await open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    ctx.form.removeLine(0); ctx.form.addLine();
    Object.assign(ctx.form.draft.lines[1], { itemId: 7, quantity: '1.000001', unitPrice: '3', taxRateId: 3 });
    ctx.form.draft.remark = ' second ';
    expect(ctx.form.hasUnsavedChanges()).toBe(true);
    ctx.form.save(); await ctx.refresh();
    const put = ctx.http.expectOne(isPut);
    expect(put.request.params.get('version')).toBe('3');
    expect(put.request.body).toEqual({ supplierId: 5, businessDate: '2028-02-29', remark: 'second', lines: [
      { lineNo: 2, itemId: 8, requisitionLineId: null, quantity: '4', unitPrice: '12.5', currencyCode: 'USD', taxRateId: 6 },
      { lineNo: 1, itemId: 7, requisitionLineId: null, quantity: '1.000001', unitPrice: '3', currencyCode: 'USD', taxRateId: 3 }] });
    put.flush(saved());
    await ctx.refresh();
    expect(success).toHaveBeenCalledWith('pur.order.form.updatedFromRejected', { number: 'PO-9', count: 2 });
    expect(navigate).toHaveBeenCalledWith('/pur/purchase-orders?id=9');
    expect(ctx.form.hasUnsavedChanges()).toBe(false);
  });

  it('PUR-PO-UI-022 without VIEW a Draft revision stays on the screen, shows the saved result and re-reads the saved order', async () => {
    granted = ['PUR.ORDER.EDIT'];
    const ctx = await open(detail('Draft', 5, [manualLine]));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    expect(ctx.by('pur-po-form-rejected')).toBeNull();
    expect(ctx.by('pur-po-form-back')).toBeNull();
    expect((ctx.by('pur-po-form-supplier') as HTMLButtonElement).disabled).toBe(false);
    ctx.form.draft.lines[0].quantity = '3';
    ctx.form.save();
    const put = ctx.http.expectOne(isPut);
    expect(put.request.params.get('version')).toBe('5');
    put.flush(JSON.stringify({ id: 9, documentNo: 'PO-9', status: 'Draft', version: 6, lines: [{}] }));
    await ctx.refresh();
    expect(success).toHaveBeenCalledWith('pur.order.form.updated', { number: 'PO-9', count: 1 });
    expect(navigate).not.toHaveBeenCalled();
    ctx.http.expectOne(isEdit).flush(detail('Draft', 6, [manualLine.replace('"quantity":2.5', '"quantity":3')]));
    await ctx.refresh();
    for (const req of ctx.http.match(isContext)) req.flush(context);
    await ctx.refresh();
    expect(ctx.text('pur-po-form-success')).toBe('Purchase order PO-9 revision saved (lines: 1).');
    expect(ctx.form.draft.lines[0].quantity).toBe('3');
    ctx.form.save();
    expect(ctx.http.expectOne(isPut).request.params.get('version')).toBe('6');
  });

  it('PUR-PO-UI-023 pending, approved or cancelled orders are read-only with the reason; a missing order shows not found', async () => {
    const ctx = await open(detail('PendingApproval'));
    expect(ctx.text('pur-po-form-not-editable')).toBe('This order is Pending approval. Only a draft or rejected purchase order can be revised, so nothing can be edited here.');
    expect(ctx.by('pur-po-form-save')).toBeNull();
    expect(ctx.by('pur-po-form-supplier')).toBeNull();
    ctx.form.save();
    ctx.http.expectNone(isPut);
  });

  it('PUR-PO-UI-023 a missing or out-of-scope order shows not found and no form', async () => {
    const harness = await RouterTestingHarness.create('/pur/purchase-orders/9/edit');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isEdit).flush(JSON.stringify({ errorCode: 'PUR.ORDER.NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    await settle(); harness.detectChanges();
    const element = harness.routeNativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="pur-po-form-load-error"]')?.textContent).toContain('This purchase order was not found.');
    expect(element.querySelector('[data-testid="pur-po-form-save"]')).toBeNull();
    http.expectNone(isContext);
  });

  it('PUR-PO-UI-024 409 keeps the input and offers a confirmed reload; 422 NOT_EDITABLE / AWARD_LINE_LOCKED explain and re-read; 500 keeps trace ID', async () => {
    const ctx = await open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    ctx.form.draft.lines[0].quantity = '9';
    ctx.form.save();
    ctx.http.expectOne(isPut).flush(JSON.stringify({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }), { status: 409, statusText: 'Conflict' });
    await ctx.refresh();
    expect(ctx.text('pur-po-form-error')).toContain('Someone else changed this record.');
    expect(ctx.text('pur-po-form-error')).toContain('The purchase order was not saved; nothing changed.');
    expect(ctx.form.draft.lines[0].quantity).toBe('9');
    ctx.http.expectNone(isEdit);
    ctx.by('pur-po-form-reload')!.click(); await ctx.refresh();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'pur.order.form.reloadConfirm' }));
    ctx.http.expectOne(isEdit).flush(detail('Rejected', 4));
    await ctx.refresh();
    for (const req of ctx.http.match(isContext)) req.flush(context);
    await ctx.refresh();
    expect(ctx.form.draft.lines[0].quantity).toBe('2.5');
    expect(ctx.by('pur-po-form-error')).toBeNull();
    expect(ctx.by('pur-po-form-reload')).toBeNull();

    // The reloaded version is sent next; 422 AWARD_LINE_LOCKED explains the reason and re-reads the saved order.
    ctx.form.draft.remark = 'x'; ctx.form.save();
    const locked = ctx.http.expectOne(isPut);
    expect(locked.request.params.get('version')).toBe('4');
    locked.flush(JSON.stringify({ errorCode: 'PUR.ORDER.AWARD_LINE_LOCKED' }), { status: 422, statusText: 'Unprocessable' });
    await ctx.refresh();
    ctx.http.expectOne(isEdit).flush(detail('Rejected', 4));
    await ctx.refresh();
    for (const req of ctx.http.match(isContext)) req.flush(context);
    await ctx.refresh();
    expect(ctx.text('pur-po-form-error')).toContain('Awarded lines keep their item, source, quantity, unit price and tax rate');
    expect(ctx.form.draft.remark).toBe('first');

    ctx.form.save();
    ctx.http.expectOne(isPut).flush(JSON.stringify({ errorCode: 'PUR.ORDER.NOT_EDITABLE' }), { status: 422, statusText: 'Unprocessable' });
    await ctx.refresh();
    ctx.http.expectOne(isEdit).flush(detail('Approved', 5));
    await ctx.refresh();
    expect(ctx.text('pur-po-form-error')).toContain('Only a draft or rejected purchase order can be revised.');
    expect(ctx.text('pur-po-form-not-editable')).toContain('This order is Approved.');
    expect(navigate).not.toHaveBeenCalled();
    expect(success).not.toHaveBeenCalled();
  });

  it('PUR-PO-UI-024 500 keeps the input and shows the trace ID without a reload offer', async () => {
    const again = await open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    again.form.draft.lines[0].quantity = '8';
    again.form.save();
    again.http.expectOne(isPut).flush(JSON.stringify({ traceId: 'po-edit-500' }), { status: 500, statusText: 'Error' });
    await again.refresh();
    expect(again.text('pur-po-form-error')).toContain('po-edit-500');
    expect(again.form.draft.lines[0].quantity).toBe('8');
    expect(again.by('pur-po-form-reload')).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
    expect(success).not.toHaveBeenCalled();
  });
});

describe('PUR-PO-UI 介面 4.1 entry to 4.3 (WU-PUR-006f3)', () => {
  let granted: string[];
  beforeEach(() => {
    granted = ['PUR.ORDER.VIEW', 'PUR.ORDER.EDIT'];
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([{ path: 'pur/purchase-orders', component: PurchaseOrderPage }]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm: vi.fn(() => of(true)), success: vi.fn() } },
      { provide: CompanyContextService, useValue: { current: signal({ id: 1 }) } },
      { provide: PermissionService, useValue: { has: (c: string) => granted.includes(c), hasAny: (c: string[]) => c.some(x => granted.includes(x)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  const list = JSON.stringify({ items: [{ id: 9, documentNo: 'PO-9', businessDate: '2028-02-29', supplierId: 5, status: 'Draft',
    currency: 'TWD', baseGross: 105, lineCount: 1, version: 1 }], totalCount: 1, page: 1, pageSize: 20 });
  async function openDetail(status: string) {
    const harness = await RouterTestingHarness.create('/pur/purchase-orders?id=9');
    const http = TestBed.inject(HttpTestingController);
    for (const req of http.match(r => r.url === API && r.method === 'GET')) req.flush(list);
    await settle();
    http.expectOne(`${API}/9`).flush(detail(status, 1, [manualLine]));
    for (const req of http.match(r => r.url === API && r.method === 'GET')) req.flush(list);
    await settle(); harness.detectChanges();
    return (harness.routeNativeElement as HTMLElement).querySelector('[data-testid="pur-order-edit"]');
  }

  it('PUR-PO-UI-025 Revise links to /:id/edit for Draft and Rejected orders with EDIT', async () => {
    expect((await openDetail('Rejected'))?.getAttribute('href')).toBe('/pur/purchase-orders/9/edit');
  });
  it('PUR-PO-UI-025 Revise is hidden for approved orders and without EDIT', async () => {
    expect(await openDetail('Approved')).toBeNull();
  });
  it('PUR-PO-UI-025 Revise is hidden without EDIT', async () => {
    granted = ['PUR.ORDER.VIEW'];
    expect(await openDetail('Draft')).toBeNull();
  });
});
