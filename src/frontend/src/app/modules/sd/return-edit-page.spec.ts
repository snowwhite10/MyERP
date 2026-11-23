import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
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
const RETURN = `${API}/5`;
const SOURCE = `${API}/source-orders/9`;
/** Line 31 (2 returnable: 1 unbilled, 1 on SI1 line 61) and line 33 (1 returnable, unbilled only). */
const source = () => '{"id":9,"documentNo":"SO1","customerId":3,"customerCode":"C1","customerName":"Acme",' +
  '"businessDate":"2028-02-01","currencyCode":"USD","baseCurrencyCode":"TWD","exchangeRate":31,"version":2,"lines":[' +
  '{"deliveryId":30,"deliveryDocumentNo":"DH1","deliveryDate":"2028-02-10","deliveryLineId":31,"deliveryLineNo":1,"orderLineId":21,' +
  '"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":51,"locationId":9,"trackingUnsupported":false,' +
  '"postedQuantity":2,"returnedQuantity":0,"returnableQuantity":2,"invoicedQuantity":1,"unbilledRemainingQuantity":1,' +
  '"invoiceLines":[{"invoiceLineId":61,"invoiceId":60,"invoiceDocumentNo":"SI1","invoiceDate":"2028-02-15","quantity":1,' +
  '"creditedQuantity":0,"remainingQuantity":1}],"warehouseCode":"WH51","warehouseName":"Main","warehouseActive":true,' +
  '"locationCode":"L9","locationName":"Bin","locationActive":true},' +
  '{"deliveryId":32,"deliveryDocumentNo":"DH2","deliveryDate":"2028-02-12","deliveryLineId":33,"deliveryLineNo":1,"orderLineId":22,' +
  '"orderLineNo":2,"itemId":8,"itemCode":"I2","itemName":"Nut","warehouseId":52,"locationId":null,"trackingUnsupported":false,' +
  '"postedQuantity":1,"returnedQuantity":0,"returnableQuantity":1,"invoicedQuantity":0,"unbilledRemainingQuantity":1,"invoiceLines":[],' +
  '"warehouseCode":"WH52","warehouseName":"Side","warehouseActive":true}]}';
const allocation = (invoiceLineId = 61, invoice = 'SI1', quantity = '1') => `{"id":1,"invoiceId":60,"invoiceDocumentNo":"${invoice}",` +
  `"invoiceLineId":${invoiceLineId},"quantity":${quantity},"originalNet":1,"originalTax":0,"originalGross":1,"baseNet":1,"baseTax":0,"baseGross":1}`;
const line = (lineNo: number, deliveryLineId: number, unbilled: string, allocations: string[], document = 'DH1') =>
  `{"id":${lineNo},"lineNo":${lineNo},"deliveryId":30,"deliveryDocumentNo":"${document}","deliveryLineId":${deliveryLineId},` +
  '"orderLineId":21,"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":51,"locationId":9,' +
  `"quantity":${Number(unbilled) + allocations.length},"unbilledQuantity":${unbilled},"billedQuantity":${allocations.length},` +
  '"originalOutboundMovementId":500,"invMovementId":null,"originalNet":1,"originalTax":0,"originalGross":1,"baseNet":1,' +
  `"baseTax":0,"baseGross":1,"allocations":[${allocations.join(',')}]}`;
const saved = ({ status = 'Draft', version = 1, remark = 'old', date = '2028-03-01', lines = [line(1, 31, '0.5', [allocation()])] } = {}) =>
  '{"id":5,"documentNo":"SR001","requestKey":"K1","orderId":9,"orderDocumentNo":"SO1","customerId":3,"customerCode":"C1",' +
  `"customerName":"Acme","businessDate":"${date}","currencyCode":"USD","baseCurrencyCode":"TWD","exchangeRate":31,` +
  `"status":"${status}","remark":${remark == null ? 'null' : JSON.stringify(remark)},"approvalInstanceId":null,"postedAt":null,` +
  '"fiCreditNoteId":null,"originalNet":100,"originalTax":5,"originalGross":105,"baseNet":3100,"baseTax":155,"baseGross":3255,' +
  `"version":${version},"lines":[${lines.join(',')}]}`;

describe('SD-RETURN-EDIT-UI sales return edit page (interface 2.17, WU-SD-008b3b2a)', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let success: ReturnType<typeof vi.fn>;
  function setup(id = '5', codes = ['SAL.RETURN.VIEW', 'SAL.RETURN.EDIT', 'SAL.RETURN.CREATE']) {
    company.set({ id: 1 });
    granted = new Set(codes);
    success = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select: vi.fn(), success } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(SalesReturnCreatePage);
    TestBed.tick();
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = <T extends HTMLElement = HTMLElement>(testId: string) => element.querySelector<T>(`[data-testid="${testId}"]`);
    const text = (testId: string) => by(testId)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const type = (testId: string, value: string) => {
      const input = by<HTMLInputElement>(testId)!; input.value = value; input.dispatchEvent(new Event('input')); fixture.detectChanges(); };
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    const save = async () => { by<HTMLFormElement>('sd-return-create-form')!.dispatchEvent(new Event('submit')); await settle(); };
    return { fixture, http, element, by, text, type, settle, save, navigate, view: fixture.componentInstance };
  }
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); vi.restoreAllMocks(); });

  async function loaded(body = saved(), codes?: string[]) {
    const context = setup('5', codes);
    context.http.expectOne(RETURN).flush(body);
    await context.settle();
    context.http.expectOne(SOURCE).flush(source());
    await context.settle();
    return context;
  }
  const isPut = (method: string, url: string) => method === 'PUT' && url === RETURN;

  it('SD-RETURN-EDIT-UI-001 loads the saved Draft into the form: fixed order, saved selection and split', async () => {
    const { text, by, element } = await loaded();
    expect(text('sd-return-create-intro')).toContain('The source order cannot change');
    expect(text('sd-return-edit-identity')).toBe('SR001 · Version 1');
    expect(text('sd-return-create-order')).toBe('SO1 · C1 · Acme');
    expect(by('sd-return-create-order-pick')).toBeNull();
    expect(by<HTMLInputElement>('sd-return-create-select-31')!.checked).toBe(true);
    expect(by<HTMLInputElement>('sd-return-create-select-33')!.checked).toBe(false);
    expect(by<HTMLInputElement>('sd-return-create-unbilled-31')!.value).toBe('0.5');
    expect(by<HTMLInputElement>('sd-return-create-billed-31-61')!.value).toBe('1');
    expect(by<HTMLInputElement>('sd-return-create-date')!.value).toBe('2028-03-01');
    expect(by<HTMLTextAreaElement>('sd-return-create-remark')!.value).toBe('old');
    expect(text('sd-return-create-total-31')).toBe('1.5');
    expect(text('sd-return-create-submit')).toBe('Save changes');
    expect(by('sd-return-edit-dropped')).toBeNull();
    expect(element.querySelector('h1')!.textContent).toContain('Edit sales return');
    expect(by<HTMLAnchorElement>('sd-return-create-back')!.getAttribute('href')).toBe('/sal/returns?open=5');
  });

  it('SD-RETURN-EDIT-UI-014 shows the saved destination (not the delivery default) and an EDIT-only user can re-pick it', async () => {
    const other = line(1, 31, '0.5', [allocation()]).replace('"warehouseId":51,"locationId":9,',
      '"warehouseId":53,"locationId":null,"warehouseCode":"WH53","warehouseName":"Returns","warehouseActive":false,');
    const { text, by, http, save, settle, view } = await loaded(saved({ lines: [other] }), ['SAL.RETURN.VIEW', 'SAL.RETURN.EDIT']);
    expect(text('sd-return-create-destination-text-31')).toBe('WH53 · Returns (inactive)');
    expect(by('sd-return-create-destination-warning-31')).not.toBeNull();
    expect(text('sd-return-create-destination-text-33')).toBe('WH52 · Side');
    const select = TestBed.inject(ErpDialogService).select as ReturnType<typeof vi.fn>;
    select.mockReturnValueOnce(of([{ id: 51, code: 'WH51', name: 'Main' }]));
    by('sd-return-create-warehouse-pick-31')!.click();
    await settle();
    expect(text('sd-return-create-destination-text-31')).toBe('WH51 · Main');
    expect(by('sd-return-create-destination-warning-31')).toBeNull();
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    expect(view.canLeave()).toBe(false);
    await save();
    const request = http.expectOne(request => isPut(request.method, request.url));
    expect(request.request.body).toContain('"deliveryLineId":31,"warehouseId":51,"locationId":null,');
    request.flush(saved({ version: 2 }));
    expect(success).toHaveBeenCalledWith('sd.returnEdit.saved', { number: 'SR001' });
  });

  it('SD-RETURN-EDIT-UI-002 an unchanged Draft sends nothing and says so with a warning', async () => {
    const { http, save, text, view } = await loaded();
    expect(view.canLeave()).toBe(true);
    await save();
    expect(http.match(request => isPut(request.method, request.url))).toHaveLength(0);
    expect(text('sd-return-edit-no-change')).toBe('No data changed. Nothing was sent.');
    expect(success).not.toHaveBeenCalled();
  });

  it('SD-RETURN-EDIT-UI-003 saves the changed split by the loaded version with the saved request key and opens the detail', async () => {
    const { http, save, type, navigate, view } = await loaded();
    type('sd-return-create-unbilled-31', '1');
    type('sd-return-create-remark', ' new ');
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    expect(view.canLeave()).toBe(false);
    expect(confirm).toHaveBeenCalledWith('Leave this page? The return has not been saved.');
    await save();
    const request = http.expectOne(request => isPut(request.method, request.url));
    expect(request.request.body).toBe('{"version":1,"draft":{"requestKey":"K1","businessDate":"2028-03-01","remark":"new","lines":[' +
      '{"lineNo":1,"deliveryLineId":31,"warehouseId":51,"locationId":9,"unbilledQuantity":1,"billed":[{"invoiceLineId":61,"quantity":1}]}]}}');
    request.flush(saved({ version: 2, remark: 'new', lines: [line(1, 31, '1', [allocation()])] }));
    expect(success).toHaveBeenCalledWith('sd.returnEdit.saved', { number: 'SR001' });
    expect(navigate).toHaveBeenCalledWith(['/sal/returns'], { queryParams: { open: 5 } });
    expect(view.canLeave()).toBe(true);
  });

  it('SD-RETURN-EDIT-UI-004 a Rejected return explains the revision, sends even unchanged and is a Draft again', async () => {
    const { http, save, text, fixture } = await loaded(saved({ status: 'Rejected', version: 3 }));
    expect(text('sd-return-edit-rejected')).toContain('Saving your changes returns it to Draft');
    await save();
    const request = http.expectOne(request => isPut(request.method, request.url));
    expect(request.request.body).toContain('{"version":3,');
    request.flush(saved({ version: 5 }));
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('sd.returnEdit.revised', { number: 'SR001' });
    expect(text('sd-return-create-success')).toContain('was revised and is a Draft again');
    expect(text('sd-return-edit-rejected')).toBeNull();
  });

  it('SD-RETURN-EDIT-UI-005 Pending and Posted returns are not editable and do not read balances', async () => {
    for (const [status, label] of [['Pending', 'Pending approval'], ['Posted', 'Posted']]) {
      TestBed.resetTestingModule();
      const { http, text, by, settle } = setup();
      http.expectOne(RETURN).flush(saved({ status }));
      await settle();
      expect(text('sd-return-edit-blocked')).toBe(`This return is ${label} and can no longer be edited.`);
      expect(by('sd-return-create-form')).toBeNull();
      http.expectNone(SOURCE);
      http.verify();
    }
  });

  it('SD-RETURN-EDIT-UI-006 a stale version keeps the input; refreshing asks before discarding it', async () => {
    const { http, save, type, text, by, settle, view } = await loaded();
    type('sd-return-create-unbilled-31', '1');
    await save();
    http.expectOne(request => isPut(request.method, request.url)).flush(JSON.stringify({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }), { status: 409, statusText: 'Conflict' });
    await settle();
    expect(text('sd-return-edit-conflict')).toContain('Someone else changed this return. Your input is kept');
    expect(by<HTMLInputElement>('sd-return-create-unbilled-31')!.value).toBe('1');
    expect(by<HTMLInputElement>('sd-return-create-unbilled-31')!.disabled).toBe(false);
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    by('sd-return-edit-refresh')!.click();
    expect(confirm).toHaveBeenLastCalledWith('Discard your changes and load the latest saved return?');
    http.expectNone(RETURN);
    by('sd-return-edit-refresh')!.click();
    http.expectOne(RETURN).flush(saved({ version: 2, remark: 'theirs' }));
    await settle();
    http.expectOne(SOURCE).flush(source());
    await settle();
    expect(text('sd-return-edit-identity')).toBe('SR001 · Version 2');
    expect(by<HTMLTextAreaElement>('sd-return-create-remark')!.value).toBe('theirs');
    expect(by('sd-return-edit-conflict')).toBeNull();
    expect(view.canLeave()).toBe(true);
  });

  it('SD-RETURN-EDIT-UI-007 a vanished return on save offers only a refresh', async () => {
    const { http, save, type, text, settle } = await loaded();
    type('sd-return-create-unbilled-31', '1');
    await save();
    http.expectOne(request => isPut(request.method, request.url))
      .flush(JSON.stringify({ errorCode: 'SAL.RETURN.NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text('sd-return-edit-conflict')).toContain('no longer exists or is outside your edit scope');
  });

  it('SD-RETURN-EDIT-UI-008 status and request-key refusals show their reason; a line refusal marks the line', async () => {
    const { http, save, type, text, settle } = await loaded();
    type('sd-return-create-unbilled-31', '1');
    await save();
    http.expectOne(request => isPut(request.method, request.url))
      .flush(JSON.stringify({ errorCode: 'SAL.RETURN.INVALID_STATUS' }), { status: 422, statusText: 'Unprocessable' });
    await settle();
    expect(text('sd-return-create-error')).toContain('The return status changed');
    await save();
    http.expectOne(request => isPut(request.method, request.url))
      .flush(JSON.stringify({ errorCode: 'SAL.RETURN.REQUEST_KEY_IMMUTABLE' }), { status: 422, statusText: 'Unprocessable' });
    await settle();
    expect(text('sd-return-create-error')).toContain('The request key of a saved return cannot be changed');
    await save();
    http.expectOne(request => isPut(request.method, request.url))
      .flush(JSON.stringify({ errorCode: 'SAL.RETURN.BILLED_EXCEEDED', lineNo: 1, invoiceLineId: 61, remaining: 0 }), { status: 422, statusText: 'Unprocessable' });
    await settle();
    expect(text('sd-return-create-line-error-31')).toBe('Exceeds the current balance (0 left). Reload and adjust.');
    expect(text('sd-return-create-reload')).toBeTruthy();
  });

  it('SD-RETURN-EDIT-UI-009 an unknown outcome locks the form; checking finds the change saved', async () => {
    const { http, save, type, text, by, settle, navigate } = await loaded();
    type('sd-return-create-unbilled-31', '1');
    await save();
    http.expectOne(request => isPut(request.method, request.url)).flush(null, { status: 502, statusText: 'Bad Gateway' });
    await settle();
    expect(text('sd-return-create-uncertain')).toContain('Check the saved return before changing anything');
    expect(by<HTMLInputElement>('sd-return-create-unbilled-31')!.disabled).toBe(true);
    expect(by('sd-return-create-resend')).toBeNull();
    by('sd-return-edit-check')!.click();
    http.expectOne(RETURN).flush(saved({ version: 2, remark: 'old', lines: [line(1, 31, '1', [allocation()])] }));
    await settle();
    expect(success).toHaveBeenCalledWith('sd.returnEdit.saved', { number: 'SR001' });
    expect(navigate).toHaveBeenCalledWith(['/sal/returns'], { queryParams: { open: 5 } });
  });

  it('SD-RETURN-EDIT-UI-010 checking finds the loaded version unchanged: not saved, the form unlocks; changed elsewhere is a conflict', async () => {
    const { http, save, type, text, by, settle } = await loaded();
    type('sd-return-create-unbilled-31', '1');
    await save();
    http.expectOne(request => isPut(request.method, request.url)).flush(null, { status: 0, statusText: 'Unknown Error' });
    await settle();
    by('sd-return-edit-check')!.click();
    http.expectOne(RETURN).flush(saved());
    await settle();
    expect(text('sd-return-create-error')).toContain('The change was not saved; the return is unchanged');
    expect(by<HTMLInputElement>('sd-return-create-unbilled-31')!.disabled).toBe(false);
    await save();
    http.expectOne(request => isPut(request.method, request.url)).flush(null, { status: 0, statusText: 'Unknown Error' });
    await settle();
    by('sd-return-edit-check')!.click();
    http.expectOne(RETURN).flush(saved({ version: 2, remark: 'theirs' }));
    await settle();
    expect(text('sd-return-edit-conflict')).toContain('Someone else changed this return');
    expect(success).not.toHaveBeenCalled();
  });

  it('SD-RETURN-EDIT-UI-011 saved quantities no longer offered are listed and left out of the PUT', async () => {
    const lines = [line(1, 31, '0', [allocation(), allocation(62, 'SI2', '2')]), line(2, 35, '3', [], 'DH9')];
    const { http, save, text } = await loaded(saved({ lines }));
    expect(text('sd-return-edit-dropped')).toContain('will be removed when you save');
    expect(text('sd-return-edit-dropped-0')).toBe('Line 1 (DH1, I1): credit 2 against SI2 no longer available');
    expect(text('sd-return-edit-dropped-1')).toBe('Line 2 (DH9, I1): 3 no longer returnable');
    await save();
    const request = http.expectOne(request => isPut(request.method, request.url));
    expect(request.request.body).toContain('"lines":[{"lineNo":1,"deliveryLineId":31,"warehouseId":51,"locationId":9,' +
      '"unbilledQuantity":0,"billed":[{"invoiceLineId":61,"quantity":1}]}]');
    request.flush(saved({ version: 2, lines: [line(1, 31, '0', [allocation()])] }));
    expect(success).toHaveBeenCalledWith('sd.returnEdit.saved', { number: 'SR001' });
  });

  it('SD-RETURN-EDIT-UI-012 load failures explain themselves and can be retried; a malformed ID reads nothing', async () => {
    const context = setup();
    context.http.expectOne(RETURN).flush(JSON.stringify({ errorCode: 'SAL.RETURN.NOT_FOUND' }), { status: 404, statusText: 'Not Found' });
    await context.settle();
    expect(context.text('sd-return-edit-load-error')).toContain('Sales return not found');
    expect(context.by('sd-return-create-form')).toBeNull();
    context.by('sd-return-edit-retry')!.click();
    context.http.expectOne(RETURN).flush(saved());
    await context.settle();
    context.http.expectOne(SOURCE).flush(JSON.stringify({ errorCode: 'COMMON.PERMISSION.DENIED' }), { status: 403, statusText: 'Forbidden' });
    await context.settle();
    expect(context.text('sd-return-create-order-error')).toBeTruthy();
    context.by('sd-return-create-order-retry')!.click();
    context.http.expectOne(SOURCE).flush(source());
    await context.settle();
    expect(context.by<HTMLInputElement>('sd-return-create-unbilled-31')!.value).toBe('0.5');
    TestBed.resetTestingModule();
    const invalid = setup('abc');
    await invalid.settle();
    expect(invalid.text('sd-return-edit-load-error')).toContain('Sales return not found');
  });
});

describe('SD-RETURN-EDIT-UI route', () => {
  it('SD-RETURN-EDIT-UI-013 the edit route needs VIEW and EDIT, guards leaving and precedes the list route', () => {
    const route = SD_ROUTES.find(candidate => candidate.path === 'returns/:id/edit')!;
    expect(route.canActivate).toHaveLength(2);
    expect(route.canDeactivate).toHaveLength(1);
    expect(SD_ROUTES.indexOf(route)).toBeLessThan(SD_ROUTES.findIndex(candidate => candidate.path === 'returns'));
  });
});
