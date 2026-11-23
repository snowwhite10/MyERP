import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { MAIN_MENU } from '../../layout/menu';
import { SalesReturnPage } from './return-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/returns';
const page = (items: object[], totalCount = items.length) => JSON.stringify({ items, totalCount, page: 1, pageSize: 20 });
const row = { id: 5, documentNo: 'SR001', orderId: 9, orderDocumentNo: 'SO001', customerId: 3, customerName: 'Acme',
  businessDate: '2028-02-29', currencyCode: 'USD', baseCurrencyCode: 'TWD', status: 'Pending', originalGross: 1234.5,
  baseGross: 38459, postedAt: null, lineCount: 2, version: 2 };
const line1 = '{"id":11,"lineNo":1,"deliveryId":30,"deliveryDocumentNo":"DH001","deliveryLineId":31,"orderLineId":21,' +
  '"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":51,"locationId":null,"quantity":1.500000,' +
  '"unbilledQuantity":0.500000,"billedQuantity":1.000000,"originalOutboundMovementId":501,"invMovementId":INV1,' +
  '"originalNet":33.3300,"originalTax":1.6700,"originalGross":35.0000,"baseNet":1033,"baseTax":52,"baseGross":1085,' +
  '"allocations":[{"id":81,"invoiceId":60,"invoiceDocumentNo":"SI001","invoiceLineId":61,"quantity":1.000000,' +
  '"originalNet":33.33,"originalTax":1.67,"originalGross":35.0000,"baseNet":1033,"baseTax":52,"baseGross":1085}],' +
  '"warehouseCode":"WH1","warehouseName":"Main","warehouseActive":true,"locationCode":null,"locationName":null,' +
  '"locationActive":null}';
const line2 = '{"id":12,"lineNo":2,"deliveryId":32,"deliveryDocumentNo":"DH002","deliveryLineId":33,"orderLineId":22,' +
  '"orderLineNo":2,"itemId":8,"itemCode":"I2","itemName":"Nut","warehouseId":52,"locationId":9,"quantity":1234.000001,' +
  '"unbilledQuantity":1234.000001,"billedQuantity":0,"originalOutboundMovementId":502,"invMovementId":INV2,' +
  '"originalNet":1000,"originalTax":50,"originalGross":1050,"baseNet":31000,"baseTax":1550,"baseGross":32550,' +
  '"allocations":[],"warehouseCode":null,"warehouseName":null,"warehouseActive":null,"locationCode":"L9",' +
  '"locationName":"Bin","locationActive":false}';
function detail(status: string, extra: { postedAt?: string; credit?: number; inv?: [number, number]; lines?: string } = {}) {
  const lines = extra.lines ?? `${line1.replace('INV1', String(extra.inv?.[0] ?? null))},${line2.replace('INV2', String(extra.inv?.[1] ?? null))}`;
  return '{"id":5,"documentNo":"SR001","requestKey":"k-1","orderId":9,"orderDocumentNo":"SO001","customerId":3,' +
    '"customerCode":"C1","customerName":"Acme","businessDate":"2028-02-29","currencyCode":"USD","baseCurrencyCode":"TWD",' +
    `"exchangeRate":31.154321,"status":"${status}","remark":"Damaged\\nin transit","approvalInstanceId":null,` +
    `"postedAt":${extra.postedAt ? `"${extra.postedAt}"` : null},"fiCreditNoteId":${extra.credit ?? null},` +
    '"originalNet":1033.3300,"originalTax":51.6700,"originalGross":1085.0000,"baseNet":32033,"baseTax":1602,' +
    `"baseGross":33635,"version":2,"lines":[${lines}]}`;
}

describe('SD-RETURN-UI sales return view page', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let query: Record<string, string> = {};
  let granted = new Set<string>();
  beforeEach(() => {
    company.set({ id: 1 }); query = {}; granted = new Set();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap(query) } }) },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function create(body = page([row])) {
    const fixture = TestBed.createComponent(SalesReturnPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne(isList);
    const params = first.request.params;
    first.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const open = (body = detail('Pending')) => {
      by('sd-return-open-5')!.click(); http.expectOne(`${API}/5`).flush(body); fixture.detectChanges();
    };
    return { fixture, http, element, by, text, params, open };
  }

  it('SD-RETURN-UI-001 lists returns in every state with order, customer, status tag and exact original and base amounts', () => {
    const { text, params } = create(page([row, { ...row, id: 6, documentNo: 'SR002', status: 'Rejected' }]));
    expect(params.get('sort')).toBe('-businessDate');
    expect(params.get('page')).toBe('1');
    const listed = text('sd-return-row-5')!;
    expect(listed).toContain('SR001');
    expect(listed).toContain('SO001');
    expect(listed).toContain('Acme');
    expect(listed).toContain('2028-02-29');
    expect(listed).toContain('USD 1,234.50');
    expect(listed).toContain('TWD 38,459.00');
    expect(text('sd-return-status-5')).toBe('Pending approval');
    expect(text('sd-return-status-6')).toBe('Rejected');
    expect(text('sd-return-notice')).toContain('only after final approval');
  });

  it('SD-RETURN-UI-002 explains an empty list and a filtered no-result list differently', () => {
    const { element, fixture, http, by } = create(page([]));
    expect(element.textContent).toContain('No sales returns yet.');
    fixture.componentInstance.keyword = ' SR9 ';
    fixture.componentInstance.status = 'Posted';
    by('sd-return-search')!.click();
    const request = http.expectOne(isList);
    expect(request.request.params.get('keyword')).toBe('SR9');
    expect(request.request.params.get('status')).toBe('Posted');
    request.flush(page([]));
    fixture.detectChanges();
    expect(element.textContent).toContain('No returns match these filters.');
  });

  it('SD-RETURN-UI-003 rejects impossible or reversed dates without sending a request', () => {
    const { fixture, by, text } = create();
    const component = fixture.componentInstance;
    component.from = '2028-03-01'; component.to = '2028-02-29';
    component.search();
    fixture.detectChanges();
    expect(text('sd-return-validation')).toBe('Enter valid dates with From on or before To.');
    expect(by('sd-return-from')!.getAttribute('aria-invalid')).toBe('true');
    component.from = '2027-02-29'; component.to = '';
    component.search();
    fixture.detectChanges();
    expect(text('sd-return-validation')).toBe('Enter valid dates with From on or before To.');
    expect(by('sd-return-row-5')).not.toBeNull();
  });

  it('SD-RETURN-UI-004 a Pending detail shows header, state hint, lines, destinations, allocations and no INV/FI yet', () => {
    const { by, text, open } = create();
    open();
    expect(text('sd-return-detail-status')).toBe('Pending approval');
    expect(text('sd-return-state')).toContain('only after final approval');
    expect(text('sd-return-detail-order')).toBe('SO001');
    expect(text('sd-return-detail-customer')).toBe('C1 · Acme');
    expect(text('sd-return-detail-rate')).toBe('31.154321');
    expect(text('sd-return-detail-posted-at')).toBe('Not posted');
    expect(by('sd-return-detail-remark')!.textContent).toContain('Damaged\nin transit');
    expect(text('sd-return-totals')).toContain('1,033.33');
    expect(text('sd-return-totals')).toContain('USD 1,085.00');
    expect(text('sd-return-totals')).toContain('TWD 33,635.00');
    expect(text('sd-return-fi-credit')).toBe('Not recorded');
    expect(text('sd-return-effects')).toContain('when the return is posted');
    const first = text('sd-return-line-11')!;
    expect(first).toContain('DH001');
    expect(first).toContain('I1 · Bolt');
    expect(first).toContain('1.5');
    expect(text('sd-return-destination-11')).toBe('WH1 · Main');
    expect(text('sd-return-allocation-81')).toBe('SI001: 1 (35.00)');
    expect(text('sd-return-inv-11')).toBe('Not received yet');
    expect(text('sd-return-line-12')).toContain('1,234.000001');
    expect(by('sd-return-detail')!.querySelector('input, textarea, select')).toBeNull();
  });

  it('SD-RETURN-UI-005 a Posted detail shows posted time, the FI credit note and each INV inbound movement', () => {
    const { text, open } = create();
    open(detail('Posted', { postedAt: '2028-03-01T10:00:00', credit: 9001, inv: [800011, 800012] }));
    expect(text('sd-return-detail-status')).toBe('Posted');
    expect(text('sd-return-state')).toContain('can no longer be changed');
    expect(text('sd-return-detail-posted-at')).toBe('2028-03-01 10:00');
    expect(text('sd-return-fi-credit')).toBe('9001');
    expect(text('sd-return-effects')).toContain('Created automatically when the return was posted');
    expect(text('sd-return-inv-11')).toBe('800011');
    expect(text('sd-return-inv-12')).toBe('800012');
  });

  it('SD-RETURN-UI-006 an all-uninvoiced return says no credit note is needed; deleted and inactive destinations are named', () => {
    const { text, open } = create();
    open(detail('Draft', { lines: line2.replace('INV2', 'null') }));
    expect(text('sd-return-detail-status')).toBe('Draft');
    expect(text('sd-return-state')).toContain('can be edited, deleted or submitted');
    expect(text('sd-return-fi-credit')).toBe('Not needed (nothing invoiced)');
    const destination = text('sd-return-destination-12')!;
    expect(destination).toContain('Deleted warehouse (ID 52)');
    expect(destination).toContain('L9 · Bin (inactive)');
  });

  it('SD-RETURN-UI-007 a Rejected detail tells the user to revise it; the hint uses the warning style', () => {
    const { by, text, open } = create();
    open(detail('Rejected'));
    expect(text('sd-return-detail-status')).toBe('Rejected');
    expect(text('sd-return-state')).toContain('Revise it to return it to Draft');
    expect(by('sd-return-state')!.querySelector('.ant-alert-warning')).not.toBeNull();
  });

  it('SD-RETURN-UI-008 shows a hidden return as a specific 404 and a server error with trace ID and retry', () => {
    const { by, text, http, fixture } = create();
    by('sd-return-open-5')!.click();
    http.expectOne(`${API}/5`).flush('{"errorCode":"SAL.RETURN.NOT_FOUND"}', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(text('sd-return-detail-error')).toContain('Sales return not found');
    by('sd-return-detail-retry')!.click();
    http.expectOne(`${API}/5`).flush('{"traceId":"trace-500"}', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('sd-return-detail-error')).toContain('trace-500');
    expect(by('sd-return-detail')).toBeNull();
  });

  it('SD-RETURN-UI-009 list failure shows a retry and never the empty-list message', () => {
    const { fixture, http, by, text, element } = create();
    fixture.componentInstance.load();
    http.expectOne(isList).flush('{"traceId":"trace-list"}', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('sd-return-error')).toContain('trace-list');
    expect(element.textContent).not.toContain('No sales returns yet.');
    expect(element.textContent).toContain('Could not load returns.');
    by('sd-return-retry')!.click();
    http.expectOne(isList).flush(page([row]));
    fixture.detectChanges();
    expect(by('sd-return-row-5')).not.toBeNull();
  });

  it('SD-RETURN-UI-010 company switch clears rows, detail, customer and order filters and ignores the old reply', () => {
    query = { customerId: '3', customerName: 'Acme', orderId: '9', orderNo: 'SO001' };
    const { fixture, http, by, params, open } = create();
    expect(params.get('customerId')).toBe('3');
    expect(params.get('orderId')).toBe('9');
    open();
    fixture.componentInstance.load();
    const stale = http.expectOne(isList);
    company.set({ id: 2 });
    TestBed.tick();
    const fresh = http.match(isList);
    expect(fresh.every(request => !request.request.params.has('customerId') && !request.request.params.has('orderId'))).toBe(true);
    stale.flush(page([row]));
    fresh.forEach(request => request.flush(page([])));
    fixture.detectChanges();
    expect(by('sd-return-row-5')).toBeNull();
    expect(by('sd-return-detail')).toBeNull();
    expect(by('sd-return-customer-filter')).toBeNull();
    expect(by('sd-return-order-filter')).toBeNull();
  });

  it('SD-RETURN-UI-011 the route and menu entry require SAL.RETURN.VIEW', () => {
    const route = SD_ROUTES.find(r => r.path === 'returns');
    expect(route?.canActivate?.length).toBe(1);
    const sales = MAIN_MENU.find(item => item.testId === 'layout-menu-sal');
    expect(sales?.children?.find(item => item.route === '/sal/returns')?.permission).toBe('SAL.RETURN.VIEW');
  });

  it('SD-RETURN-UI-012 restores filters, status, page, size and sort from the URL and drops invalid values', () => {
    query = { keyword: 'SR', from: '2028-02-01', to: '2028-02-30', status: 'Voided', page: '3', pageSize: '50',
      sort: 'documentNo', orderId: 'x', customerId: '-4' };
    const { params } = create();
    expect(params.get('keyword')).toBe('SR');
    expect(params.get('from')).toBe('2028-02-01');
    expect(params.has('to')).toBe(false);
    expect(params.has('status')).toBe(false);
    expect(params.get('page')).toBe('3');
    expect(params.get('pageSize')).toBe('50');
    expect(params.get('sort')).toBe('documentNo');
    expect(params.has('orderId')).toBe(false);
    expect(params.has('customerId')).toBe(false);
  });

  it('SD-RETURN-UI-013 filters by a row order and customer, names them in the URL, then reset clears every filter', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');
    query = { status: 'Draft' };
    const { by, text, http, fixture, params } = create();
    expect(params.get('status')).toBe('Draft');
    by('sd-return-filter-order-5')!.click();
    const byOrder = http.expectOne(isList);
    expect(byOrder.request.params.get('orderId')).toBe('9');
    expect(byOrder.request.params.get('page')).toBe('1');
    byOrder.flush(page([row]));
    fixture.detectChanges();
    expect(text('sd-return-order-filter')).toContain('SO001');
    expect(navigate.mock.lastCall?.[1]?.queryParams).toMatchObject({ orderId: 9, orderNo: 'SO001', status: 'Draft' });
    by('sd-return-filter-customer-5')!.click();
    const byCustomer = http.expectOne(isList);
    expect(byCustomer.request.params.get('customerId')).toBe('3');
    expect(byCustomer.request.params.get('orderId')).toBe('9');
    byCustomer.flush(page([row]));
    fixture.detectChanges();
    expect(text('sd-return-customer-filter')).toContain('Acme');
    by('sd-return-order-clear')!.click();
    const withoutOrder = http.expectOne(isList);
    expect(withoutOrder.request.params.has('orderId')).toBe(false);
    expect(withoutOrder.request.params.get('customerId')).toBe('3');
    withoutOrder.flush(page([row]));
    fixture.detectChanges();
    by('sd-return-reset')!.click();
    const cleared = http.expectOne(isList);
    expect(['customerId', 'orderId', 'status', 'keyword'].some(key => cleared.request.params.has(key))).toBe(false);
    cleared.flush(page([row]));
    fixture.detectChanges();
    expect(by('sd-return-customer-filter')).toBeNull();
    expect(navigate.mock.lastCall?.[1]?.queryParams).toMatchObject({ orderId: null, customerId: null, status: null });
  });

  it('SD-RETURN-UI-014 a stalled list read shows a timeout reason and retry', async () => {
    const { fixture, by, text, http } = create();
    vi.useFakeTimers();
    try {
      fixture.componentInstance.load();
      const pending = http.expectOne(isList);
      await vi.advanceTimersByTimeAsync(6001);
      fixture.detectChanges();
      expect(pending.cancelled).toBe(true);
      expect(text('sd-return-error')).toContain('Request timed out');
      expect(by('sd-return-retry')).not.toBeNull();
    } finally { vi.useRealTimers(); }
  });

  it('SD-RETURN-UI-015 ?open=<id> opens that return once with the first list read and focuses its title', async () => {
    query = { open: '5' };
    const fixture = TestBed.createComponent(SalesReturnPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page([row]));
    http.expectOne(`${API}/5`).flush(detail('Draft'));
    fixture.detectChanges();
    await fixture.whenStable();
    const title = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="sd-return-detail-title"]');
    expect(title?.textContent).toBe('SR001');
    expect(document.activeElement === title || title?.contains(document.activeElement)).toBe(true);
  });

  it('SD-RETURN-UI-015b an invalid ?open is ignored', () => {
    query = { open: 'abc' };
    create();
    TestBed.inject(HttpTestingController).expectNone(r => r.url.startsWith(`${API}/`));
  });

  it('SD-RETURN-UI-016 a newer open or close discards an older detail reply', () => {
    const { by, http, fixture } = create();
    by('sd-return-open-5')!.click();
    const older = http.expectOne(`${API}/5`);
    by('sd-return-open-5')!.click();
    const newer = http.expectOne(`${API}/5`);
    older.flush(detail('Draft'));
    fixture.detectChanges();
    expect(by('sd-return-detail')).toBeNull();
    newer.flush(detail('Posted', { postedAt: '2028-03-01T10:00:00', credit: 9001, inv: [1, 2] }));
    fixture.detectChanges();
    expect(by('sd-return-detail-status')!.textContent).toContain('Posted');
    by('sd-return-close')!.click();
    fixture.detectChanges();
    expect(by('sd-return-detail')).toBeNull();
  });
});

describe('SD-RETURN-UI-017 new return entry point', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  function render(codes: string[]) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useFactory: () => ({ snapshot: { queryParamMap: convertToParamMap({}) } }) },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => codes.includes(code),
        hasAny: (list: string[]) => list.some(code => codes.includes(code)) } }] });
    const fixture = TestBed.createComponent(SalesReturnPage);
    TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne(r => r.url === API).flush(page([]));
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('[data-testid="sd-return-new"]');
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows "New return" linking to /sal/returns/new only with SAL.RETURN.CREATE', () => {
    const link = render(['SAL.RETURN.VIEW', 'SAL.RETURN.CREATE']);
    expect(link?.textContent?.trim()).toBe('New return');
    expect(link?.getAttribute('href')).toBe('/sal/returns/new');
  });
  it('hides "New return" without SAL.RETURN.CREATE', () => {
    expect(render(['SAL.RETURN.VIEW'])).toBeNull();
  });
});
