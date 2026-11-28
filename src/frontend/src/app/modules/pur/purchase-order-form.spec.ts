import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, ChangeDetectorRef, signal } from '@angular/core';
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
import { PurchaseOrderForm, purchaseOrderFieldErrors } from './purchase-order-form';
import { PurchaseOrderPage } from './purchase-order-page';
import { PurchaseOrderDraft } from './purchase-order.service';

const API = '/api/v1/pur/purchase-orders';
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0)); };
const draft = (patch: Partial<PurchaseOrderDraft> = {}): PurchaseOrderDraft => ({ supplierId: 5, businessDate: '2028-02-29',
  currencyCode: 'TWD', remark: '', lines: [{ itemId: 7, requisitionLineId: null, quantity: '1', unitPrice: '0', taxRateId: 3 }], ...patch });
const context = (rates: { id: number; ratePercent: string }[], currencies = [{ code: 'TWD', name: 'New Taiwan Dollar' }]) =>
  JSON.stringify({ businessDate: '2028-02-29', currencies,
    taxRates: rates.map(r => ({ id: r.id, code: `VAT${r.id}`, name: `Input ${r.ratePercent}%`, ratePercent: r.ratePercent })) });
const supplier = { id: 5, code: 'SUP5', name: 'Acme', isActive: true, isApproved: true };
const item = (id: number) => ({ id, code: `ITM${id}`, name: `Item ${id}`, isActive: true });
const source = { id: 44, requisitionId: 4, documentNo: 'PR-0004', lineNo: 2, itemId: 7, requiredDate: '2028-03-10',
  quantity: '5.123456', orderedQuantity: '2', remainingQuantity: '3.123456' };

describe('PUR-PO-UI 介面 4.2 rules (WU-PUR-006f2b)', () => {
  it('PUR-PO-UI-014 reports every invalid header and line field in display order without sending anything', () => {
    expect(purchaseOrderFieldErrors(draft({ supplierId: null, businessDate: '2027-02-29', currencyCode: '', remark: 'x'.repeat(501),
      lines: [{ itemId: null, requisitionLineId: null, quantity: '0', unitPrice: '-1', taxRateId: null },
        { itemId: 7, requisitionLineId: null, quantity: '1.1234567', unitPrice: '10000000000000', taxRateId: 3 }] })))
      .toEqual(['supplierId', 'businessDate', 'currencyCode', 'remark', 'lines.0.itemId', 'lines.0.quantity', 'lines.0.unitPrice',
        'lines.0.taxRateId', 'lines.1.quantity', 'lines.1.unitPrice']);
    expect(purchaseOrderFieldErrors(draft({ lines: [] }))).toEqual(['lines']);
  });

  it('PUR-PO-UI-014 accepts leap day, zero price, the 13+6 digit maxima and 500-character remarks', () => {
    expect(purchaseOrderFieldErrors(draft({ remark: 'x'.repeat(500), lines: [
      { itemId: 7, requisitionLineId: 44, quantity: '0.000001', unitPrice: '0', taxRateId: 3 },
      { itemId: 7, requisitionLineId: null, quantity: '9999999999999.999999', unitPrice: '9999999999999.999999', taxRateId: 3 }] })))
      .toEqual([]);
  });
});

describe('PUR-PO-UI 介面 4.2 create form (WU-PUR-006f2b)', () => {
  let granted: string[];
  let select: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let picks: unknown[][];
  beforeEach(() => {
    granted = ['PUR.ORDER.CREATE', 'PUR.ORDER.VIEW'];
    picks = [];
    select = vi.fn(() => of(picks.shift() ?? null)); success = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { select, success, confirm: vi.fn(() => of(true)) } },
      { provide: CompanyContextService, useValue: { current: signal({ id: 1 }) } },
      { provide: PermissionService, useValue: { has: (c: string) => granted.includes(c), hasAny: (c: string[]) => c.some(x => granted.includes(x)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isContext = (r: HttpRequest<unknown>) => r.url === `${API}/create-context` && r.method === 'GET';
  async function open() {
    const fixture = TestBed.createComponent(PurchaseOrderForm);
    fixture.detectChanges(); await settle(); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const view = fixture.componentRef.injector.get(ChangeDetectorRef);
    const refresh = async () => { view.markForCheck(); fixture.detectChanges(); await settle(); view.markForCheck(); fixture.detectChanges(); };
    return { fixture, http, by, text, element, refresh, form: fixture.componentInstance };
  }
  /** Fills a valid one-line draft through the screen's own choosers. */
  async function filled(rates = [{ id: 3, ratePercent: '5.000000' }]) {
    const ctx = await open();
    picks.push([supplier]); ctx.form.chooseSupplier();
    ctx.form.changeDate('2028-02-29');
    const req = ctx.http.expectOne(isContext);
    expect(req.request.params.get('businessDate')).toBe('2028-02-29');
    req.flush(context(rates));
    picks.push([item(7)]); ctx.form.chooseItem(ctx.form.draft.lines[0]);
    Object.assign(ctx.form.draft.lines[0], { quantity: '2.5', unitPrice: '100.123456', taxRateId: 3 });
    await ctx.refresh();
    return ctx;
  }

  it('PUR-PO-UI-015 currency and tax wait for the business date; a new date re-reads the context and clears rates no longer valid', async () => {
    const ctx = await open();
    expect((ctx.by('pur-po-form-currency') as HTMLSelectElement).disabled).toBe(true);
    expect(ctx.text('pur-po-form-context-hint')).toBe('Choose the business date first to load currencies and input tax rates.');
    ctx.form.changeDate('2028-02-29');
    ctx.http.expectOne(isContext).flush(context([{ id: 3, ratePercent: '5.000000' }, { id: 4, ratePercent: '0.000000' }]));
    await ctx.refresh();
    // A single active currency is chosen for the user; rates keep the server's exact digits.
    expect(ctx.form.draft.currencyCode).toBe('TWD');
    expect([...(ctx.by('pur-po-form-tax-0') as HTMLSelectElement).options].map(o => o.textContent!.trim()))
      .toEqual(['—', 'VAT3 · Input 5.000000% (5%)', 'VAT4 · Input 0.000000% (0%)']);
    ctx.form.draft.lines[0].taxRateId = 4;
    ctx.form.changeDate('2028-03-01');
    ctx.http.expectOne(r => isContext(r) && r.params.get('businessDate') === '2028-03-01').flush(context([{ id: 3, ratePercent: '5.000000' }]));
    await ctx.refresh();
    expect(ctx.form.draft.lines[0].taxRateId).toBeNull();
    expect(ctx.text('pur-po-form-stale')).toBe('The input tax rate of line 1 is not valid on the new business date and was cleared. Choose a rate again.');

    // An invalid date sends nothing; a failed context read shows the reason and trace ID and leaves the choices empty.
    ctx.form.changeDate('2028-02-30');
    ctx.http.expectNone(isContext);
    ctx.form.changeDate('2028-03-02');
    ctx.http.expectOne(isContext).flush(JSON.stringify({ traceId: 'ctx-500' }), { status: 500, statusText: 'Error' });
    await ctx.refresh();
    expect(ctx.text('pur-po-form-context-error')).toContain('ctx-500');
    expect(ctx.form.context()).toBeNull();
  });

  it('PUR-PO-UI-016 requisition source needs an item, shows quantities exactly, can be cleared and is dropped when the item changes', async () => {
    const ctx = await filled();
    ctx.form.draft.lines[0].itemId = null; await ctx.refresh();
    expect((ctx.by('pur-po-form-source-0') as HTMLButtonElement).disabled).toBe(true);
    expect(ctx.by('pur-po-form-source-0')!.getAttribute('title')).toBe('Choose the item first.');
    ctx.form.draft.lines[0].itemId = 7; await ctx.refresh();
    picks.push([source]); ctx.form.chooseSource(ctx.form.draft.lines[0]);
    expect(select.mock.calls.at(-1)![0].titleKey).toBe('pur.order.form.chooseSource');
    // The chooser asks the server only for the line's item.
    const load = select.mock.calls.at(-1)![0].load as (q: object) => { subscribe: (f: () => void) => void };
    load({ keyword: '', page: 1, pageSize: 10 }).subscribe(() => undefined);
    const options = ctx.http.expectOne(r => r.url === `${API}/requisition-options`);
    expect(options.request.params.get('itemId')).toBe('7');
    options.flush(JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 10 }));
    await ctx.refresh();
    expect(ctx.form.draft.lines[0].requisitionLineId).toBe(44);
    expect(ctx.text('pur-po-form-source-detail-0')).toBe('PR-0004 / line 2 · Requested 5.123456, converted 2, remaining 3.123456');
    ctx.form.draft.lines[0].quantity = '3.123457'; await ctx.refresh();
    expect(ctx.text('pur-po-form-source-over-0')).toBe('Above the remaining requisition quantity 3.123456. The draft can be saved, but submission will be refused.');
    ctx.form.draft.lines[0].quantity = '3.123456'; await ctx.refresh();
    expect(ctx.by('pur-po-form-source-over-0')).toBeNull();
    ctx.by('pur-po-form-source-clear-0')!.click(); await ctx.refresh();
    expect(ctx.form.draft.lines[0].requisitionLineId).toBeNull();
    picks.push([source]); ctx.form.chooseSource(ctx.form.draft.lines[0]);
    picks.push([item(8)]); ctx.form.chooseItem(ctx.form.draft.lines[0]); await ctx.refresh();
    expect(ctx.form.draft.lines[0].requisitionLineId).toBeNull();
    expect(ctx.text('pur-po-form-source-dropped-0')).toBe('The requisition source was removed because it is for a different item.');
  });

  it('PUR-PO-UI-017 local errors block saving and focus the first field; a valid draft posts exact text and opens the saved detail', async () => {
    const ctx = await open();
    ctx.form.save(); await ctx.refresh();
    ctx.http.expectNone(API);
    expect(ctx.text('pur-po-form-error')).toContain('Some fields need attention');
    expect(ctx.text('pur-po-form-supplier-error')).toBe('Choose a supplier.');
    expect(ctx.text('pur-po-form-quantity-error-0')).toBe('Enter a quantity above 0 with up to 13 digits and 6 decimal places.');
    expect(document.activeElement?.getAttribute('data-testid') ?? 'pur-po-form-supplier').toBe('pur-po-form-supplier');
    ctx.fixture.destroy();

    const ok = await filled();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    ok.form.draft.remark = '  rush  ';
    expect(ok.form.hasUnsavedChanges()).toBe(true);
    ok.form.save(); await ok.refresh();
    expect((ok.by('pur-po-form-save') as HTMLButtonElement).disabled).toBe(true);
    const post = ok.http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ supplierId: 5, businessDate: '2028-02-29', remark: 'rush', lines: [{ lineNo: 1, itemId: 7,
      requisitionLineId: null, quantity: '2.5', unitPrice: '100.123456', currencyCode: 'TWD', taxRateId: 3 }] });
    post.flush(`{"id":9,"documentNo":"PO-202802-0001","status":"Draft","version":1,"supplierId":5,"businessDate":"2028-02-29",
      "currency":"TWD","baseCurrencyCode":"TWD","exchangeRate":1,"baseNet":250.3086,"baseTax":12.5154,"baseGross":262.824,"remark":"rush",
      "lines":[{"id":90,"lineNo":1,"itemId":7,"requisitionLineId":null,"quantity":2.5,"unitPrice":100.123456,"taxRateId":3,
      "taxRatePercent":5,"baseNet":250.3086,"baseTax":12.5154,"baseGross":262.824}]}`, { status: 201, statusText: 'Created' });
    await ok.refresh();
    expect(success).toHaveBeenCalledWith('pur.order.form.saved', { number: 'PO-202802-0001', count: 1 });
    expect(navigate).toHaveBeenCalledWith('/pur/purchase-orders?id=9');
    expect(ok.form.hasUnsavedChanges()).toBe(false);
  });

  it('PUR-PO-UI-017 without VIEW the saved number stays on screen and a blank form is ready for the next order', async () => {
    granted = ['PUR.ORDER.CREATE'];
    const ctx = await filled();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    ctx.form.save();
    ctx.http.expectOne(r => r.url === API && r.method === 'POST').flush(JSON.stringify({ id: 9, documentNo: 'PO-9', status: 'Draft',
      version: 1, lines: [{}, {}] }), { status: 201, statusText: 'Created' });
    await ctx.refresh();
    expect(navigate).not.toHaveBeenCalled();
    expect(ctx.text('pur-po-form-success')).toBe('Purchase order PO-9 saved as a draft (lines: 2).');
    expect(ctx.form.draft.supplierId).toBeNull();
    expect(ctx.form.hasUnsavedChanges()).toBe(false);
  });

  it('PUR-PO-UI-018 server refusals keep the input: 400 per field, 422 master data on the field, invalid source, 500 trace ID', async () => {
    const ctx = await filled();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const post = () => ctx.http.expectOne(r => r.url === API && r.method === 'POST');
    ctx.form.save();
    post().flush(JSON.stringify({ errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { 'lines[0].quantity': ['bad'],
      'lines[0].currencyCode': ['bad'], remark: ['too long'] } }), { status: 400, statusText: 'Bad Request' });
    await ctx.refresh();
    expect(ctx.text('pur-po-form-server-quantity-0')).toBe('The server rejected this value. Check it and save again.');
    expect(ctx.text('pur-po-form-server-currencyCode')).toBe('The server rejected this value. Check it and save again.');
    expect(ctx.text('pur-po-form-error')).toContain('Line 1 · Quantity');
    expect(ctx.text('pur-po-form-error')).toContain('The purchase order was not saved; nothing changed.');

    ctx.form.save();
    post().flush(JSON.stringify({ errorCode: 'MD.SUPPLIER.NOT_APPROVED' }), { status: 422, statusText: 'Unprocessable' });
    await ctx.refresh();
    expect(ctx.text('pur-po-form-error')).toContain('This supplier must be active and approved for transactions.');
    expect(ctx.text('pur-po-form-server-supplierId')).toBe('This supplier must be active and approved for transactions.');

    ctx.form.save();
    post().flush(JSON.stringify({ errorCode: 'PUR.ORDER.INVALID_REQUISITION_SOURCE' }), { status: 422, statusText: 'Unprocessable' });
    await ctx.refresh();
    expect(ctx.text('pur-po-form-error')).toContain('is no longer an approved requisition line for the same item');

    ctx.form.save();
    post().flush(JSON.stringify({ errorCode: 'PUR.ORDER.CURRENCY_UNAVAILABLE' }), { status: 422, statusText: 'Unprocessable' });
    await ctx.refresh();
    expect(ctx.text('pur-po-form-error')).toContain('The order currency is not active.');

    ctx.form.save();
    post().flush(JSON.stringify({ traceId: 'po-500' }), { status: 500, statusText: 'Error' });
    await ctx.refresh();
    expect(ctx.text('pur-po-form-error')).toContain('po-500');
    expect(navigate).not.toHaveBeenCalled();
    expect(success).not.toHaveBeenCalled();
    expect(ctx.form.draft.lines[0].quantity).toBe('2.5');
    expect(ctx.form.hasUnsavedChanges()).toBe(true);
  });
});

describe('PUR-PO-UI 介面 4.1 entry to 4.2 (WU-PUR-006f2b)', () => {
  let granted: string[];
  beforeEach(() => {
    granted = ['PUR.ORDER.VIEW', 'PUR.ORDER.CREATE'];
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
  const detail = `{"id":9,"documentNo":"PO-9","status":"Draft","version":1,"supplierId":5,"rfqId":null,"rfqDocumentNo":null,
    "businessDate":"2028-02-29","currency":"TWD","baseCurrencyCode":"TWD","exchangeRate":1,"baseNet":100,"baseTax":5,"baseGross":105,
    "remark":null,"lines":[],"approvalHistory":[]}`;

  it('PUR-PO-UI-019 the list offers New only with CREATE, and ?id= opens the saved detail after the list loads', async () => {
    const harness = await RouterTestingHarness.create('/pur/purchase-orders?id=9');
    const http = TestBed.inject(HttpTestingController);
    for (const req of http.match(r => r.url === API && r.method === 'GET')) req.flush(list);
    await settle();
    http.expectOne(`${API}/9`).flush(detail);
    for (const req of http.match(r => r.url === API && r.method === 'GET')) req.flush(list);
    await settle(); harness.detectChanges();
    const element = harness.routeNativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="pur-order-new"]')?.getAttribute('href')).toBe('/pur/purchase-orders/new');
    expect(harness.routeDebugElement!.componentInstance.detail()?.documentNo).toBe('PO-9');
  });

  it('PUR-PO-UI-019 without CREATE the list hides New and opens no detail without ?id=', async () => {
    const http = TestBed.inject(HttpTestingController);
    granted = ['PUR.ORDER.VIEW'];
    const viewOnly = await RouterTestingHarness.create('/pur/purchase-orders');
    for (const req of http.match(r => r.url === API && r.method === 'GET')) req.flush(list);
    await settle(); viewOnly.detectChanges();
    expect((viewOnly.routeNativeElement as HTMLElement).querySelector('[data-testid="pur-order-new"]')).toBeNull();
    http.expectNone(`${API}/9`);
  });
});
