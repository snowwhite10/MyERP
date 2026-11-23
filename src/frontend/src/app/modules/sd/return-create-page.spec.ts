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
import { SalesReturnCreatePage } from './return-create-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/returns';
const SOURCE = `${API}/source-orders/9`;
const source = (extra = '') => '{"id":9,"documentNo":"SO1","customerId":3,"customerCode":"C1","customerName":"Acme",' +
  '"businessDate":"2028-02-01","currencyCode":"USD","baseCurrencyCode":"TWD","exchangeRate":31.154321,"version":2,"lines":[' +
  '{"deliveryId":30,"deliveryDocumentNo":"DH1","deliveryDate":"2028-02-10","deliveryLineId":31,"deliveryLineNo":1,"orderLineId":21,' +
  '"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":51,"locationId":9,"trackingUnsupported":false,' +
  '"postedQuantity":2,"returnedQuantity":0,"returnableQuantity":2.000000,"invoicedQuantity":1,"unbilledRemainingQuantity":1.000000,' +
  '"invoiceLines":[{"invoiceLineId":61,"invoiceId":60,"invoiceDocumentNo":"SI1","invoiceDate":"2028-02-15","quantity":1,' +
  '"creditedQuantity":0,"remainingQuantity":1.000000}],"warehouseCode":"WH51","warehouseName":"Main","warehouseActive":true,' +
  '"locationCode":"L9","locationName":"Bin","locationActive":false},' +
  '{"deliveryId":32,"deliveryDocumentNo":"DH2","deliveryDate":"2028-02-12","deliveryLineId":33,"deliveryLineNo":1,"orderLineId":22,' +
  '"orderLineNo":2,"itemId":8,"itemCode":"I2","itemName":"Lot item","warehouseId":52,"locationId":null,"trackingUnsupported":true,' +
  '"postedQuantity":1,"returnedQuantity":0,"returnableQuantity":1,"invoicedQuantity":0,"unbilledRemainingQuantity":1,"invoiceLines":[],' +
  `"warehouseCode":null,"warehouseName":null,"warehouseActive":null}${extra}]}`;
const saved = (key: string, unbilled = '0.5', billed = '[{"id":1,"invoiceId":60,"invoiceDocumentNo":"SI1","invoiceLineId":61,"quantity":1,' +
  '"originalNet":1,"originalTax":0,"originalGross":1,"baseNet":1,"baseTax":0,"baseGross":1}]') => `{"id":5,"documentNo":"SR001",` +
  `"requestKey":"${key}","orderId":9,"orderDocumentNo":"SO1","customerId":3,"customerCode":"C1","customerName":"Acme",` +
  '"businessDate":"2028-03-01","currencyCode":"USD","baseCurrencyCode":"TWD","exchangeRate":31,"status":"Draft","remark":null,' +
  '"approvalInstanceId":null,"postedAt":null,"fiCreditNoteId":null,"originalNet":100,"originalTax":5,"originalGross":105.0000,' +
  '"baseNet":3100,"baseTax":155,"baseGross":3255,"version":1,"lines":[{"id":1,"lineNo":1,"deliveryId":30,"deliveryDocumentNo":"DH1",' +
  '"deliveryLineId":31,"orderLineId":21,"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":51,"locationId":9,' +
  `"quantity":1.5,"unbilledQuantity":${unbilled},"billedQuantity":1,"originalOutboundMovementId":500,"invMovementId":null,` +
  `"originalNet":1,"originalTax":0,"originalGross":1,"baseNet":1,"baseTax":0,"baseGross":1,"allocations":${billed}}]}`;

describe('SD-RETURN-CREATE-UI sales return create page', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let select: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  function setup(codes = ['SAL.RETURN.CREATE', 'SAL.RETURN.VIEW']) {
    company.set({ id: 1 });
    granted = new Set(codes);
    select = vi.fn(); success = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select, success } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(SalesReturnCreatePage);
    TestBed.tick();
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = <T extends HTMLElement = HTMLElement>(id: string) => element.querySelector<T>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const type = (id: string, value: string) => {
      const input = by<HTMLInputElement>(id)!; input.value = value; input.dispatchEvent(new Event('input')); fixture.detectChanges(); };
    const toggle = (id: string) => { by<HTMLInputElement>(id)!.click(); fixture.detectChanges(); };
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    return { fixture, http, element, by, text, type, toggle, navigate, settle, view: fixture.componentInstance };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function loaded(codes?: string[], body = source()) {
    const context = setup(codes);
    select.mockReturnValueOnce(of([{ id: 9, documentNo: 'SO1', customerCode: 'C1', customerName: 'Acme' }]));
    context.by('sd-return-create-order-pick')!.click();
    context.http.expectOne(SOURCE).flush(body);
    await context.settle();
    return context;
  }
  /** Line 31: 0.5 not invoiced + 1 credited against SI1, return date 2028-03-01. */
  async function filled(codes?: string[]) {
    const context = await loaded(codes);
    context.type('sd-return-create-date', '2028-03-01');
    context.toggle('sd-return-create-select-31');
    await context.settle();
    context.type('sd-return-create-unbilled-31', '0.5');
    context.type('sd-return-create-billed-31-61', '1');
    return context;
  }
  const isCreate = (method: string, url: string) => method === 'POST' && url === API;

  it('SD-RETURN-CREATE-UI-001 picks an order from the CREATE-scoped picker and shows returnable lines, splits and destinations', async () => {
    const { text, by } = await loaded();
    const options = select.mock.calls[0][0];
    expect(options.titleKey).toBe('sd.returnCreate.chooseOrder');
    expect(options.columns.map((column: { key: string }) => column.key)).toEqual(['documentNo', 'customerName', 'businessDate', 'currencyCode', 'openLineCount']);
    expect(text('sd-return-create-order')).toBe('SO1 · C1 · Acme');
    expect(text('sd-return-create-rate')).toBe('31.154321');
    expect(text('sd-return-create-returnable-31')).toBe('2');
    expect(text('sd-return-create-destination-31')).toContain('WH51 · Main / L9 · Bin (inactive)');
    expect(text('sd-return-create-destination-warning-31')).toContain('inactive or deleted');
    expect(text('sd-return-create-destination-33')).toContain('Deleted warehouse (ID 52)');
    expect(text('sd-return-create-line-31')).toContain('Not invoiced (up to 1)');
    expect(text('sd-return-create-line-31')).toContain('Credit SI1 (2028-02-15, up to 1)');
    expect(by<HTMLInputElement>('sd-return-create-unbilled-31')!.disabled).toBe(true);
    expect(text('sd-return-create-count')).toBe('0 of 2 lines selected');
  });

  it('SD-RETURN-CREATE-UI-002 a lot / serial line cannot be selected and says why', async () => {
    const { by, text } = await loaded();
    const box = by<HTMLInputElement>('sd-return-create-select-33')!;
    expect(box.disabled).toBe(true);
    expect(box.getAttribute('aria-describedby')).toBe('sd-return-create-tracking-33');
    expect(text('sd-return-create-tracking-33')).toBe('Lot / serial items cannot be returned here yet.');
  });

  it('SD-RETURN-CREATE-UI-003 saving with nothing selected shows inline reasons and sends nothing', async () => {
    const { by, text, fixture, http } = await loaded();
    by('sd-return-create-submit')!.click();
    fixture.detectChanges();
    expect(text('sd-return-create-date-error')).toBe('Enter a valid return date.');
    expect(text('sd-return-create-lines-error')).toBe('Select at least one line.');
    expect(text('sd-return-create-errors')).toBe('Correct the 2 highlighted item(s) before saving.');
    http.expectNone(r => isCreate(r.method, r.url));
  });

  it('SD-RETURN-CREATE-UI-004 split over a balance and a date before the credited invoice are explained per field', async () => {
    const { by, text, type, fixture, http, element } = await filled();
    type('sd-return-create-unbilled-31', '1.000001');
    type('sd-return-create-date', '2028-02-14');
    by('sd-return-create-submit')!.click();
    await fixture.whenStable(); fixture.detectChanges();
    expect(text('sd-return-create-unbilled-error-31')).toBe('At most 1 is not yet invoiced.');
    expect(text('sd-return-create-date-error')).toBe('The return date cannot be earlier than 2028-02-15.');
    expect(text('sd-return-create-date-hint')).toContain('2028-02-15');
    expect(text('sd-return-create-total-31')).toContain('2.000001');
    expect(element.ownerDocument.activeElement?.getAttribute('data-testid')).toBe('sd-return-create-date');
    http.expectNone(r => isCreate(r.method, r.url));
    type('sd-return-create-unbilled-31', '0.5');
    expect(text('sd-return-create-unbilled-error-31')).toBeNull();
  });

  it('SD-RETURN-CREATE-UI-005 saves a Draft with an exact body, confirms and opens it in the list', async () => {
    const { by, http, fixture, navigate, text } = await filled();
    expect(text('sd-return-create-total-31')).toBe('1.5');
    by('sd-return-create-submit')!.click();
    const request = http.expectOne(r => isCreate(r.method, r.url));
    const body = request.request.body as string;
    const key = /"requestKey":"([^"]+)"/.exec(body)![1];
    expect(body).toBe(`{"requestKey":"${key}","businessDate":"2028-03-01","remark":null,"lines":[{"lineNo":1,"deliveryLineId":31,` +
      '"warehouseId":51,"locationId":9,"unbilledQuantity":0.5,"billed":[{"invoiceLineId":61,"quantity":1}]}]}');
    request.flush(saved(key), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('sd.returnCreate.saved', { number: 'SR001' });
    expect(navigate).toHaveBeenCalledWith(['/sal/returns'], { queryParams: { open: 5 } });
    expect(text('sd-return-create-success')).toContain('Sales return SR001 was saved as a Draft.');
    expect(fixture.componentInstance.canLeave()).toBe(true);
  });

  it('SD-RETURN-CREATE-UI-006 a 200 replay says nothing new was created; without VIEW it stays on the page', async () => {
    const { by, http, fixture, navigate, text } = await filled(['SAL.RETURN.CREATE']);
    by('sd-return-create-submit')!.click();
    const request = http.expectOne(r => isCreate(r.method, r.url));
    const key = /"requestKey":"([^"]+)"/.exec(request.request.body as string)![1];
    request.flush(saved(key), { status: 200, statusText: 'OK' });
    fixture.detectChanges();
    expect(text('sd-return-create-success')).toContain('had already been saved; nothing new was created');
    expect(navigate).not.toHaveBeenCalled();
    expect(by('sd-return-create-back')).toBeNull();
  });

  it('SD-RETURN-CREATE-UI-007 an unknown outcome (500 without code) locks the form and resends the identical body', async () => {
    const { by, http, fixture, text } = await filled();
    by('sd-return-create-submit')!.click();
    const first = http.expectOne(r => isCreate(r.method, r.url));
    const body = first.request.body as string;
    first.flush(JSON.stringify({ traceId: 'T-1' }), { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text('sd-return-create-uncertain')).toContain('The save result is unknown.');
    expect(text('sd-return-create-uncertain-trace')).toBe('Trace ID: T-1');
    expect(by<HTMLInputElement>('sd-return-create-unbilled-31')!.disabled).toBe(true);
    by('sd-return-create-resend')!.click();
    const second = http.expectOne(r => isCreate(r.method, r.url));
    expect(second.request.body).toBe(body);
    const key = /"requestKey":"([^"]+)"/.exec(body)![1];
    second.flush(saved(key), { status: 200, statusText: 'OK' });
    fixture.detectChanges();
    expect(text('sd-return-create-success')).toContain('SR001');
  });

  it('SD-RETURN-CREATE-UI-008 a receipt that does not match the request is treated as unknown, not as success', async () => {
    const { by, http, fixture, text } = await filled();
    by('sd-return-create-submit')!.click();
    const request = http.expectOne(r => isCreate(r.method, r.url));
    const key = /"requestKey":"([^"]+)"/.exec(request.request.body as string)![1];
    request.flush(saved(key, '0.4'), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(success).not.toHaveBeenCalled();
    expect(text('sd-return-create-uncertain')).toContain('unknown');
  });

  it('SD-RETURN-CREATE-UI-009 a 422 billed refusal marks the line, focuses that invoice portion, offers reload and uses a new key', async () => {
    const { by, http, fixture, text, element } = await filled();
    by('sd-return-create-submit')!.click();
    const first = http.expectOne(r => isCreate(r.method, r.url));
    const firstKey = /"requestKey":"([^"]+)"/.exec(first.request.body as string)![1];
    first.flush(JSON.stringify({ errorCode: 'SAL.RETURN.BILLED_EXCEEDED', lineNo: 1, invoiceLineId: 61, remaining: 0.25 }),
      { status: 422, statusText: 'Unprocessable' });
    await fixture.whenStable(); fixture.detectChanges();
    expect(text('sd-return-create-error')).toContain("exceeds the invoice line's uncredited quantity");
    expect(text('sd-return-create-line-error-31')).toBe('Exceeds the current balance (0.25 left). Reload and adjust.');
    expect(element.ownerDocument.activeElement?.getAttribute('data-testid')).toBe('sd-return-create-billed-31-61');
    by('sd-return-create-reload')!.click();
    http.expectOne(SOURCE).flush(source());
    await fixture.whenStable(); fixture.detectChanges();
    expect(by<HTMLInputElement>('sd-return-create-billed-31-61')!.value).toBe('1');
    expect(text('sd-return-create-error')).toBeNull();
    by('sd-return-create-submit')!.click();
    const second = http.expectOne(r => isCreate(r.method, r.url));
    expect(second.request.body).not.toContain(firstKey);
    second.flush(JSON.stringify({ errorCode: 'SAL.RETURN.DATE_BEFORE_INVOICE', lineNo: 1, invoiceLineId: 61 }), { status: 422, statusText: 'Unprocessable' });
    await fixture.whenStable(); fixture.detectChanges();
    expect(element.ownerDocument.activeElement?.getAttribute('data-testid')).toBe('sd-return-create-date');
    expect(text('sd-return-create-line-error-31')).toBeNull();
  });

  it('SD-RETURN-CREATE-UI-010 source read failures: 404 with retry, timeout-free 500 trace, and an order with no returnable line', async () => {
    const context = setup();
    select.mockReturnValueOnce(of([{ id: 9 }]));
    context.by('sd-return-create-order-pick')!.click();
    context.http.expectOne(SOURCE).flush(JSON.stringify({ errorCode: 'SAL.RETURN.SOURCE_NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    await context.settle();
    expect(context.text('sd-return-create-order-error')).toContain('A delivery line was not found');
    context.by('sd-return-create-order-retry')!.click();
    context.http.expectOne(SOURCE).flush(JSON.stringify({ traceId: 'T-9' }), { status: 500, statusText: 'Server Error' });
    await context.settle();
    expect(context.text('sd-return-create-order-error')).toContain('T-9');
    context.by('sd-return-create-order-retry')!.click();
    context.http.expectOne(SOURCE).flush(source().replace(/"lines":\[.*\]\}$/, '"lines":[]}'));
    await context.settle();
    expect(context.text('sd-return-create-no-lines')).toContain('already been returned');
    expect(context.by('sd-return-create-form')).toBeNull();
  });

  it('SD-RETURN-CREATE-UI-011 an unsaved pick asks before leaving; a company switch clears the form', async () => {
    const { view, fixture, text } = await loaded();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    expect(view.canLeave()).toBe(false);
    expect(confirm).toHaveBeenCalledWith('Leave this page? The return has not been saved.');
    company.set({ id: 2 });
    TestBed.tick(); fixture.detectChanges();
    expect(text('sd-return-create-order')).toBe('No order selected');
    expect(view.canLeave()).toBe(true);
    confirm.mockRestore();
  });
});

describe('SD-RETURN-CREATE-UI route', () => {
  it('SD-RETURN-CREATE-UI-012 /sal/returns/new needs SAL.RETURN.CREATE and asks before leaving', () => {
    const route = SD_ROUTES.find(candidate => candidate.path === 'returns/new')!;
    expect(route.canActivate).toHaveLength(1);
    expect(route.canDeactivate).toHaveLength(1);
    expect(SD_ROUTES.indexOf(route)).toBeLessThan(SD_ROUTES.findIndex(candidate => candidate.path === 'returns'));
  });
});
