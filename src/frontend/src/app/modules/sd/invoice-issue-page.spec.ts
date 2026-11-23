import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SalesInvoiceIssuePage } from './invoice-issue-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/invoices';
const SOURCE = `${API}/source-orders/9`;
const source = (remaining1 = '0.750001', extra = '') => '{"id":9,"documentNo":"SO1","customerId":3,"customerCode":"C1","customerName":"Acme",' +
  '"businessDate":"2028-02-01","currencyCode":"USD","baseCurrencyCode":"TWD","exchangeRate":31.154321,"version":2,"lines":[' +
  '{"deliveryId":30,"deliveryDocumentNo":"DH1","deliveryDate":"2028-02-10","deliveryLineId":31,"deliveryLineNo":1,"orderLineId":21,' +
  `"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","taxRatePercent":5.0000,"postedQuantity":1,"invoicedQuantity":0.249999,"remainingQuantity":${remaining1}},` +
  '{"deliveryId":32,"deliveryDocumentNo":"DH2","deliveryDate":"2028-02-20","deliveryLineId":33,"deliveryLineNo":1,"orderLineId":22,' +
  '"orderLineNo":2,"itemId":8,"itemCode":"I2","itemName":"Nut","taxRatePercent":5,"postedQuantity":9999999999999.999999,"invoicedQuantity":0,' +
  `"remainingQuantity":9999999999999.999999}${extra}]}`;
const saved = (key: string, lines: string, date = '2028-02-29') => `{"id":5,"documentNo":"SI001","requestKey":"${key}","orderId":9,` +
  `"orderDocumentNo":"SO1","customerId":3,"customerCode":"C1","customerName":"Acme","businessDate":"${date}","currencyCode":"USD",` +
  '"baseCurrencyCode":"TWD","exchangeRate":31.154321,"status":"Posted","postedAt":"2028-02-29T10:00:00","fiSalesSourceId":41,' +
  '"fiReceivableId":42,"fiJournalId":77,"originalNet":100,"originalTax":5,"originalGross":105.0000,"baseNet":3115,"baseTax":156,' +
  `"baseGross":3271,"version":1,"lines":[${lines}]}`;
const savedLine = (id: number, deliveryLineId: number, quantity: string) => `{"id":${id},"lineNo":${id},"deliveryId":30,` +
  `"deliveryDocumentNo":"DH1","deliveryLineId":${deliveryLineId},"orderLineId":21,"orderLineNo":1,"itemId":7,"itemCode":"I1",` +
  `"itemName":"Bolt","taxRateId":2,"taxRatePercent":5,"quantity":${quantity},"originalNet":1,"originalTax":0,"originalGross":1,` +
  '"baseNet":1,"baseTax":0,"baseGross":1}';

describe('SD-INV-ISSUE-UI sales invoice issue page', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let select: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  function setup(codes = ['SAL.INVOICE.ISSUE', 'SAL.INVOICE.VIEW']) {
    company.set({ id: 1 });
    granted = new Set(codes);
    select = vi.fn(); success = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select, success } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(SalesInvoiceIssuePage);
    TestBed.tick();
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = <T extends HTMLElement = HTMLElement>(id: string) => element.querySelector<T>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const type = (id: string, value: string) => {
      const input = by<HTMLInputElement>(id)!; input.value = value; input.dispatchEvent(new Event('input')); fixture.detectChanges(); };
    const toggle = (id: string) => { by<HTMLInputElement>(id)!.click(); fixture.detectChanges(); };
    return { fixture, http, element, by, text, type, toggle, navigate, view: fixture.componentInstance };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function loaded(codes?: string[], body = source()) {
    const context = setup(codes);
    select.mockReturnValueOnce(of([{ id: 9, documentNo: 'SO1', customerCode: 'C1', customerName: 'Acme' }]));
    context.by('sd-invoice-issue-order-pick')!.click();
    context.http.expectOne(SOURCE).flush(body);
    context.fixture.detectChanges();
    await context.fixture.whenStable();
    context.fixture.detectChanges();
    return context;
  }
  const isIssue = (method: string, url: string) => method === 'POST' && url === API;

  it('SD-INV-ISSUE-UI-001 picks a source order from the ISSUE-scoped picker and shows open lines with exact balances', async () => {
    const { text, by, element } = await loaded();
    const options = select.mock.calls[0][0];
    expect(options.titleKey).toBe('sd.invoiceIssue.chooseOrder');
    expect(options.columns.map((column: { key: string }) => column.key)).toEqual(['documentNo', 'customerName', 'businessDate', 'currencyCode', 'openLineCount']);
    expect(options.columns[1].value({ customerCode: 'C1', customerName: 'Acme' })).toBe('C1 · Acme');
    expect(text('sd-invoice-issue-order')).toBe('SO1 · C1 · Acme');
    expect(text('sd-invoice-issue-rate')).toBe('31.154321');
    expect(text('sd-invoice-issue-line-31')).toContain('DH1 #1');
    expect(text('sd-invoice-issue-line-31')).toContain('0.249999');
    expect(text('sd-invoice-issue-remaining-31')).toBe('0.750001');
    expect(text('sd-invoice-issue-remaining-33')).toBe('9,999,999,999,999.999999');
    // Every open line starts selected with its full remaining quantity, kept as exact text.
    expect(by<HTMLInputElement>('sd-invoice-issue-select-31')!.checked).toBe(true);
    expect(by<HTMLInputElement>('sd-invoice-issue-quantity-33')!.value).toBe('9999999999999.999999');
    expect(text('sd-invoice-issue-count')).toBe('2 of 2 lines selected');
    expect(element.textContent).toContain('2028-02-20');
    expect(text('sd-invoice-issue-intro')).toContain('cannot be edited');
  });

  it('SD-INV-ISSUE-UI-002 issues the selected lines with one request key and exact quantities, then opens the invoice in the list', async () => {
    const { by, type, toggle, http, fixture, navigate } = await loaded();
    toggle('sd-invoice-issue-select-33');
    type('sd-invoice-issue-quantity-31', ' 0.5 ');
    type('sd-invoice-issue-date', '2028-02-29');
    by('sd-invoice-issue-submit')!.click();
    const request = http.expectOne(r => isIssue(r.method, r.url));
    const body = request.request.body as string;
    expect(body).toMatch(/^\{"requestKey":"[0-9a-f-]{36}","businessDate":"2028-02-29","lines":\[\{"lineNo":1,"deliveryLineId":31,"quantity":0\.5\}\]\}$/);
    const key = JSON.parse(body).requestKey;
    request.flush(saved(key, savedLine(1, 31, '0.5')), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('sd.invoiceIssue.issued', { number: 'SI001' });
    expect(navigate).toHaveBeenCalledWith(['/sal/invoices'], { queryParams: { open: 5 } });
    expect(fixture.componentInstance.canLeave()).toBe(true);
  });

  it('SD-INV-ISSUE-UI-003 validates before sending: invoice date, quantity format, remaining and empty selection', async () => {
    const { by, type, toggle, text, http, fixture } = await loaded();
    by('sd-invoice-issue-submit')!.click(); fixture.detectChanges();
    expect(text('sd-invoice-issue-date-error')).toBe('Enter a valid invoice date.');
    type('sd-invoice-issue-date', '2028-02-15');
    expect(text('sd-invoice-issue-date-error')).toContain('cannot be earlier than 2028-02-20');
    type('sd-invoice-issue-quantity-31', '0.7500011');
    expect(text('sd-invoice-issue-quantity-error-31')).toContain('6 decimals');
    type('sd-invoice-issue-quantity-31', '0.750002');
    expect(text('sd-invoice-issue-quantity-error-31')).toBe('Cannot exceed the remaining 0.750001.');
    type('sd-invoice-issue-quantity-31', '0');
    expect(text('sd-invoice-issue-quantity-error-31')).toContain('positive');
    // Unselecting the later line moves the date floor to the remaining selected line.
    toggle('sd-invoice-issue-select-33');
    expect(by('sd-invoice-issue-date-error')).toBeNull();
    toggle('sd-invoice-issue-all'); toggle('sd-invoice-issue-all');
    expect(text('sd-invoice-issue-lines-error')).toBe('Select at least one line.');
    expect(text('sd-invoice-issue-errors')).toContain('Fix 1 issue');
    http.expectNone(r => isIssue(r.method, r.url));
  });

  it('SD-INV-ISSUE-UI-004 timeout keeps the request; Resend safely sends the identical body and a 200 replay says nothing was posted again', async () => {
    const { by, type, http, fixture, text, navigate } = await loaded();
    vi.useFakeTimers();
    try {
      type('sd-invoice-issue-date', '2028-02-29');
      by('sd-invoice-issue-submit')!.click();
      const first = http.expectOne(r => isIssue(r.method, r.url));
      const body = first.request.body as string;
      await vi.advanceTimersByTimeAsync(20001);
      expect(first.cancelled).toBe(true);
      fixture.detectChanges();
      expect(text('sd-invoice-issue-uncertain')).toContain('could not be confirmed');
      expect(by<HTMLButtonElement>('sd-invoice-issue-submit')!.disabled).toBe(true);
      expect(by<HTMLInputElement>('sd-invoice-issue-quantity-31')!.disabled).toBe(true);
      const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
      expect(fixture.componentInstance.canLeave()).toBe(false);
      confirm.mockRestore();
      by('sd-invoice-issue-resend')!.click();
      const second = http.expectOne(r => isIssue(r.method, r.url));
      expect(second.request.body).toBe(body);
      const key = JSON.parse(body).requestKey;
      second.flush(saved(key, `${savedLine(1, 31, '0.750001')},${savedLine(2, 33, '9999999999999.999999')}`), { status: 200, statusText: 'OK' });
      fixture.detectChanges();
      expect(success).toHaveBeenCalledWith('sd.invoiceIssue.replayed', { number: 'SI001' });
      expect(navigate).toHaveBeenCalled();
    } finally { vi.useRealTimers(); }
  });

  it('SD-INV-ISSUE-UI-005 network failure and a 500 without a known code are uncertain; a coded FI 500 is a definite failure with a new key', async () => {
    const { by, type, http, fixture, text } = await loaded();
    type('sd-invoice-issue-date', '2028-02-29');
    by('sd-invoice-issue-submit')!.click();
    const first = http.expectOne(r => isIssue(r.method, r.url));
    const key = JSON.parse(first.request.body as string).requestKey;
    first.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    fixture.detectChanges();
    expect(by('sd-invoice-issue-uncertain')).not.toBeNull();
    by('sd-invoice-issue-resend')!.click();
    const second = http.expectOne(r => isIssue(r.method, r.url));
    expect(JSON.parse(second.request.body as string).requestKey).toBe(key);
    second.flush(JSON.stringify({ traceId: 't-500' }), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(by('sd-invoice-issue-uncertain')).not.toBeNull();
    expect(text('sd-invoice-issue-uncertain-trace')).toContain('t-500');
    by('sd-invoice-issue-resend')!.click();
    const third = http.expectOne(r => isIssue(r.method, r.url));
    third.flush(JSON.stringify({ errorCode: 'SAL.INVOICE.FI_POSTING_FAILED', traceId: 't-fi' }), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(by('sd-invoice-issue-uncertain')).toBeNull();
    expect(text('sd-invoice-issue-error')).toContain('everything was rolled back');
    expect(text('sd-invoice-issue-error')).toContain('t-fi');
    expect(by<HTMLButtonElement>('sd-invoice-issue-submit')!.disabled).toBe(false);
    by('sd-invoice-issue-submit')!.click();
    const fourth = http.expectOne(r => isIssue(r.method, r.url));
    expect(JSON.parse(fourth.request.body as string).requestKey).not.toBe(key);
    fourth.flush(saved(JSON.parse(fourth.request.body as string).requestKey,
      `${savedLine(1, 31, '0.750001')},${savedLine(2, 33, '9999999999999.999999')}`), { status: 201, statusText: 'Created' });
  });

  it('SD-INV-ISSUE-UI-006 422 BALANCE_EXCEEDED marks the refused line with the remaining quantity; Reload keeps the selection', async () => {
    const { by, type, toggle, http, fixture, text } = await loaded();
    toggle('sd-invoice-issue-select-31');
    type('sd-invoice-issue-date', '2028-02-29');
    by('sd-invoice-issue-submit')!.click();
    // lineNo 1 is the first selected line: delivery line 33 (31 is unselected).
    http.expectOne(r => isIssue(r.method, r.url)).flush(JSON.stringify({ errorCode: 'SAL.INVOICE.BALANCE_EXCEEDED', lineNo: 1,
      remaining: 12.5 }), { status: 422, statusText: 'Unprocessable Entity' });
    fixture.detectChanges();
    expect(text('sd-invoice-issue-line-error-33')).toBe('Only 12.5 can still be invoiced on this line.');
    expect(text('sd-invoice-issue-error')).toContain('exceeds');
    expect(by('sd-invoice-issue-line-33')!.classList).toContain('failed');
    by('sd-invoice-issue-reload')!.click();
    http.expectOne(SOURCE).flush(source('0.750001').replace('"remainingQuantity":9999999999999.999999', '"remainingQuantity":12.5'));
    fixture.detectChanges();
    await fixture.whenStable(); fixture.detectChanges();
    expect(by('sd-invoice-issue-error')).toBeNull();
    expect(by<HTMLInputElement>('sd-invoice-issue-select-31')!.checked).toBe(false);
    expect(text('sd-invoice-issue-quantity-error-33')).toBe('Cannot exceed the remaining 12.5.');
  });

  it('SD-INV-ISSUE-UI-007 named refusals: DATE_BEFORE_DELIVERY, REQUEST_CONFLICT, FI credit lock, 403', async () => {
    const { by, type, http, fixture, text } = await loaded();
    type('sd-invoice-issue-date', '2028-02-29');
    const refuse = (status: number, body: object) => {
      by('sd-invoice-issue-submit')!.click();
      http.expectOne(r => isIssue(r.method, r.url)).flush(JSON.stringify(body), { status, statusText: 'x' });
      fixture.detectChanges();
    };
    refuse(422, { errorCode: 'SAL.INVOICE.DATE_BEFORE_DELIVERY', lineNo: 2 });
    expect(text('sd-invoice-issue-error')).toContain('cannot be earlier than the delivery date');
    expect(by('sd-invoice-issue-line-error-33')).toBeNull();
    refuse(409, { errorCode: 'SAL.INVOICE.REQUEST_CONFLICT' });
    expect(text('sd-invoice-issue-error')).toContain('different content');
    refuse(409, { errorCode: 'FI.CREDIT.LOCK_TIMEOUT' });
    expect(text('sd-invoice-issue-error')).toContain('being updated by another user');
    refuse(403, { errorCode: 'COMMON.PERMISSION.DENIED' });
    expect(by('sd-invoice-issue-error')).not.toBeNull();
    refuse(422, { errorCode: 'SAL.INVOICE.SOMETHING_NEW' });
    expect(text('sd-invoice-issue-error')).toContain('Nothing was issued');
  });

  it('SD-INV-ISSUE-UI-008 source read failures: 422 ORDER_NOT_APPROVED, 500 with trace and Retry, and an order without open lines', async () => {
    const context = setup();
    select.mockReturnValueOnce(of([{ id: 9 }]));
    context.by('sd-invoice-issue-order-pick')!.click();
    context.http.expectOne(SOURCE).flush(JSON.stringify({ errorCode: 'SAL.INVOICE.ORDER_NOT_APPROVED' }), { status: 422, statusText: 'x' });
    context.fixture.detectChanges();
    expect(context.text('sd-invoice-issue-order-error')).toContain('no longer');
    context.by('sd-invoice-issue-order-retry')!.click();
    context.http.expectOne(SOURCE).flush(JSON.stringify({ traceId: 'trace-9' }), { status: 500, statusText: 'x' });
    context.fixture.detectChanges();
    expect(context.text('sd-invoice-issue-order-error')).toContain('trace-9');
    context.by('sd-invoice-issue-order-retry')!.click();
    context.http.expectOne(SOURCE).flush(source().replace(/"lines":\[.*\]\}$/, '"lines":[]}'));
    context.fixture.detectChanges();
    expect(context.text('sd-invoice-issue-no-lines')).toContain('no outbound lines left');
    expect(context.by('sd-invoice-issue-form')).toBeNull();
  });

  it('SD-INV-ISSUE-UI-009 a receipt that does not match the request is treated as unconfirmed', async () => {
    const { by, type, http, fixture } = await loaded();
    type('sd-invoice-issue-date', '2028-02-29');
    by('sd-invoice-issue-submit')!.click();
    http.expectOne(r => isIssue(r.method, r.url)).flush(saved('another-key', savedLine(1, 31, '0.750001')), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(by('sd-invoice-issue-uncertain')).not.toBeNull();
    expect(success).not.toHaveBeenCalled();
  });

  it('SD-INV-ISSUE-UI-010 without SAL.INVOICE.VIEW the receipt stays on the page with no list links', async () => {
    const { by, type, http, fixture, text, navigate } = await loaded(['SAL.INVOICE.ISSUE']);
    expect(by('sd-invoice-issue-back')).toBeNull();
    type('sd-invoice-issue-date', '2028-02-29');
    by('sd-invoice-issue-submit')!.click();
    const request = http.expectOne(r => isIssue(r.method, r.url));
    const key = JSON.parse(request.request.body as string).requestKey;
    request.flush(saved(key, `${savedLine(1, 31, '0.750001')},${savedLine(2, 33, '9999999999999.999999')}`), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(navigate).not.toHaveBeenCalled();
    expect(text('sd-invoice-issue-success')).toContain('SI001 was issued and posted');
    expect(text('sd-invoice-issue-receipt')).toContain('USD 105.00');
    by('sd-invoice-issue-another')!.click(); fixture.detectChanges();
    expect(text('sd-invoice-issue-order')).toBe('No order selected');
  });

  it('SD-INV-ISSUE-UI-011 leaving with a chosen order asks first; switching company clears the form and ignores the stale reply', async () => {
    const context = setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    expect(context.view.canLeave()).toBe(true);
    select.mockReturnValueOnce(of([{ id: 9 }]));
    context.by('sd-invoice-issue-order-pick')!.click();
    const pending = context.http.expectOne(SOURCE);
    expect(context.view.canLeave()).toBe(false);
    expect(confirm).toHaveBeenCalled();
    company.set({ id: 2 }); TestBed.tick(); context.fixture.detectChanges();
    pending.flush(source());
    context.fixture.detectChanges();
    expect(context.text('sd-invoice-issue-order')).toBe('No order selected');
    expect(context.by('sd-invoice-issue-form')).toBeNull();
    confirm.mockRestore();
  });

  it('SD-INV-ISSUE-UI-012 route invoices/new requires SAL.INVOICE.ISSUE and guards leaving', () => {
    setup();
    const route = SD_ROUTES.find(candidate => candidate.path === 'invoices/new')!;
    expect(route.canActivate?.length).toBe(1);
    expect(route.canDeactivate?.length).toBe(1);
    expect(SD_ROUTES.findIndex(candidate => candidate.path === 'invoices/new'))
      .toBeLessThan(SD_ROUTES.findIndex(candidate => candidate.path === 'invoices'));
  });
});
