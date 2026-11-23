import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { MAIN_MENU } from '../../layout/menu';
import { SalesInvoicePage } from './invoice-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/invoices';
const page = (items: object[], totalCount = items.length) => JSON.stringify({ items, totalCount, page: 1, pageSize: 20 });
const row = { id: 5, documentNo: 'SI001', orderId: 9, orderDocumentNo: 'SO001', customerId: 3, customerName: 'Acme',
  businessDate: '2028-02-29', currencyCode: 'USD', baseCurrencyCode: 'TWD', originalGross: 1234.5, baseGross: 38459,
  postedAt: '2028-02-29T10:15:30', fiJournalId: 77, lineCount: 2, version: 1 };
const posted = '{"id":5,"documentNo":"SI001","requestKey":"k-1","orderId":9,"orderDocumentNo":"SO001","customerId":3,' +
  '"customerCode":"C1","customerName":"Acme","businessDate":"2028-02-29","currencyCode":"USD","baseCurrencyCode":"TWD",' +
  '"exchangeRate":31.154321,"status":"Posted","postedAt":"2028-02-29T10:15:30","fiSalesSourceId":41,"fiReceivableId":42,' +
  '"fiJournalId":77,"originalNet":1175.7143,"originalTax":58.7857,"originalGross":1234.5000,"baseNet":36628,' +
  '"baseTax":1831,"baseGross":38459,"version":1,"lines":[' +
  '{"id":11,"lineNo":1,"deliveryId":30,"deliveryDocumentNo":"DH001","deliveryLineId":31,"orderLineId":21,"orderLineNo":1,' +
  '"itemId":7,"itemCode":"I1","itemName":"Bolt","taxRateId":2,"taxRatePercent":5.0000,"quantity":1234.000001,' +
  '"originalNet":1000.0000,"originalTax":50.0000,"originalGross":1050.0000,"baseNet":31154,"baseTax":1558,"baseGross":32712},' +
  '{"id":12,"lineNo":2,"deliveryId":32,"deliveryDocumentNo":"DH002","deliveryLineId":33,"orderLineId":22,"orderLineNo":2,' +
  '"itemId":8,"itemCode":"I2","itemName":"Nut","taxRateId":2,"taxRatePercent":5.0000,"quantity":0.5,' +
  '"originalNet":175.7143,"originalTax":8.7857,"originalGross":184.5000,"baseNet":5474,"baseTax":273,"baseGross":5747}]}';

describe('SD-INV-UI sales invoice view page', () => {
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
        hasAny: (codes: readonly string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function create(body = page([row])) {
    const fixture = TestBed.createComponent(SalesInvoicePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne(isList);
    const params = first.request.params;
    first.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const open = () => { by('sd-invoice-open-5')!.click(); http.expectOne(`${API}/5`).flush(posted); fixture.detectChanges(); };
    return { fixture, http, element, by, text, params, open };
  }

  it('SD-INV-UI-002 lists posted invoices with order, customer, currency, exact original and base amounts and FI journal', () => {
    const { text, params } = create();
    expect(params.get('sort')).toBe('-businessDate');
    const listed = text('sd-invoice-row-5')!;
    expect(listed).toContain('SI001');
    expect(listed).toContain('SO001');
    expect(listed).toContain('Acme');
    expect(listed).toContain('2028-02-29');
    expect(listed).toContain('USD 1,234.50');
    expect(listed).toContain('TWD 38,459.00');
    expect(listed).toContain('77');
    expect(text('sd-invoice-notice')).toContain('cannot be edited');
  });

  it('SD-INV-UI-003 explains an empty list and a filtered no-result list differently', () => {
    const { element, fixture, http, by } = create(page([]));
    expect(element.textContent).toContain('No invoices yet.');
    fixture.componentInstance.keyword = 'SI9';
    by('sd-invoice-search')!.click();
    const request = http.expectOne(isList);
    expect(request.request.params.get('keyword')).toBe('SI9');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([]));
    fixture.detectChanges();
    expect(element.textContent).toContain('No invoices match these filters.');
  });

  it('SD-INV-UI-004 rejects impossible or reversed dates without sending a request', () => {
    const { fixture, by, text } = create();
    const component = fixture.componentInstance;
    component.from = '2028-03-01'; component.to = '2028-02-29';
    component.search();
    fixture.detectChanges();
    expect(text('sd-invoice-validation')).toBe('Enter valid dates with From on or before To.');
    component.from = '2027-02-29'; component.to = '';
    component.search();
    fixture.detectChanges();
    expect(text('sd-invoice-validation')).toBe('Enter valid dates with From on or before To.');
    expect(by('sd-invoice-row-5')).not.toBeNull();
  });

  it('SD-INV-UI-005 shows the posted detail with saved totals, FI references read-only and every line', () => {
    const { by, text, open } = create();
    open();
    expect(text('sd-invoice-detail-status')).toBe('Posted');
    expect(text('sd-invoice-detail-customer')).toContain('C1 · Acme');
    expect(text('sd-invoice-detail-rate')).toContain('31.154321');
    expect(text('sd-invoice-totals')).toContain('1,175.7143');
    expect(text('sd-invoice-totals')).toContain('58.7857');
    expect(text('sd-invoice-totals')).toContain('USD 1,234.50');
    expect(text('sd-invoice-totals')).toContain('TWD 38,459.00');
    expect(text('sd-invoice-fi-journal')).toBe('77');
    expect(text('sd-invoice-fi-receivable')).toBe('42');
    expect(text('sd-invoice-fi-source')).toBe('41');
    expect(by('sd-invoice-detail')!.querySelector('input, textarea, select')).toBeNull();
    const line = text('sd-invoice-line-11')!;
    expect(line).toContain('DH001');
    expect(line).toContain('I1 · Bolt');
    expect(line).toContain('1,234.000001');
    expect(line).toContain('5%');
    expect(line).toContain('1,050.00');
    expect(line).toContain('32,712.00');
    expect(text('sd-invoice-line-12')).toContain('0.5');
  });

  it('SD-INV-UI-006 shows "not recorded" for a missing FI reference instead of an empty cell', () => {
    const { by, http, fixture, text } = create();
    by('sd-invoice-open-5')!.click();
    http.expectOne(`${API}/5`).flush(posted.replace('"fiJournalId":77', '"fiJournalId":null'));
    fixture.detectChanges();
    expect(text('sd-invoice-fi-journal')).toBe('Not recorded');
  });

  it('SD-INV-UI-007 shows a hidden invoice as a specific 404 and a server error with trace ID and retry', () => {
    const { by, text, http, fixture } = create();
    by('sd-invoice-open-5')!.click();
    http.expectOne(`${API}/5`).flush('{"errorCode":"SAL.INVOICE.NOT_FOUND"}', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(text('sd-invoice-detail-error')).toContain('Invoice not found');
    by('sd-invoice-detail-retry')!.click();
    http.expectOne(`${API}/5`).flush('{"traceId":"trace-500"}', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('sd-invoice-detail-error')).toContain('trace-500');
    expect(by('sd-invoice-detail')).toBeNull();
  });

  it('SD-INV-UI-008 list failure shows a retry and never the empty-list message', () => {
    const { fixture, http, by, text, element } = create();
    fixture.componentInstance.load();
    http.expectOne(isList).flush('{"traceId":"trace-list"}', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('sd-invoice-error')).toContain('trace-list');
    expect(element.textContent).not.toContain('No invoices yet.');
    by('sd-invoice-retry')!.click();
    http.expectOne(isList).flush(page([row]));
    fixture.detectChanges();
    expect(by('sd-invoice-row-5')).not.toBeNull();
  });

  it('SD-INV-UI-009 company switch clears the previous company rows, detail and customer filter and ignores the old reply', () => {
    query = { customerId: '3', customerName: 'Acme' };
    const { fixture, http, by, params, open } = create();
    expect(params.get('customerId')).toBe('3');
    open();
    fixture.componentInstance.load();
    const stale = http.expectOne(isList);
    company.set({ id: 2 });
    TestBed.tick();
    const fresh = http.match(isList);
    expect(fresh.every(request => !request.request.params.has('customerId'))).toBe(true);
    stale.flush(page([row]));
    fresh.forEach(request => request.flush(page([])));
    fixture.detectChanges();
    expect(by('sd-invoice-row-5')).toBeNull();
    expect(by('sd-invoice-detail')).toBeNull();
    expect(by('sd-invoice-customer-filter')).toBeNull();
  });

  it('SD-INV-UI-010 the route and menu entry require SAL.INVOICE.VIEW', () => {
    const route = SD_ROUTES.find(r => r.path === 'invoices');
    expect(route?.canActivate?.length).toBe(1);
    const sales = MAIN_MENU.find(item => item.testId === 'layout-menu-sal');
    expect(sales?.children?.find(item => item.route === '/sal/invoices')?.permission).toBe('SAL.INVOICE.VIEW');
  });

  it('SD-INV-UI-011 restores filters, page, size and sort from the URL and drops invalid values', () => {
    query = { keyword: 'SI', from: '2028-02-01', to: '2028-02-30', page: '3', pageSize: '50', sort: 'documentNo',
      orderId: 'x', customerId: '-4' };
    const { params } = create();
    expect(params.get('keyword')).toBe('SI');
    expect(params.get('from')).toBe('2028-02-01');
    expect(params.has('to')).toBe(false);
    expect(params.get('page')).toBe('3');
    expect(params.get('pageSize')).toBe('50');
    expect(params.get('sort')).toBe('documentNo');
    expect(params.has('orderId')).toBe(false);
    expect(params.has('customerId')).toBe(false);
  });

  it('SD-INV-UI-012 a customer filter from the URL is named and can be cleared', () => {
    query = { customerId: '3', customerName: 'Acme' };
    const { by, text, http, fixture } = create();
    expect(text('sd-invoice-customer-filter')).toContain('Acme');
    by('sd-invoice-customer-clear')!.click();
    const request = http.expectOne(isList);
    expect(request.request.params.has('customerId')).toBe(false);
    request.flush(page([row]));
    fixture.detectChanges();
    expect(by('sd-invoice-customer-filter')).toBeNull();
  });

  it('SD-INV-UI-013 filters by a row customer and names it, then reset clears every filter', () => {
    const { by, text, http, fixture } = create();
    by('sd-invoice-filter-customer-5')!.click();
    const request = http.expectOne(isList);
    expect(request.request.params.get('customerId')).toBe('3');
    request.flush(page([row]));
    fixture.detectChanges();
    expect(text('sd-invoice-customer-filter')).toContain('Acme');
    by('sd-invoice-reset')!.click();
    const cleared = http.expectOne(isList);
    expect(cleared.request.params.has('customerId')).toBe(false);
    cleared.flush(page([row]));
  });

  it('SD-INV-UI-014 a stalled list read shows a timeout reason and retry', async () => {
    const { fixture, by, text, http } = create();
    vi.useFakeTimers();
    try {
      fixture.componentInstance.load();
      const pending = http.expectOne(isList);
      await vi.advanceTimersByTimeAsync(6001);
      fixture.detectChanges();
      expect(pending.cancelled).toBe(true);
      expect(text('sd-invoice-error')).toContain('Request timed out');
      expect(by('sd-invoice-retry')).not.toBeNull();
    } finally { vi.useRealTimers(); }
  });

  it('SD-INV-ISSUE-UI-013 shows Issue invoice only with SAL.INVOICE.ISSUE', () => {
    const without = create();
    expect(without.by('sd-invoice-new')).toBeNull();
    without.fixture.destroy();
    granted = new Set(['SAL.INVOICE.ISSUE']);
    const withIssue = create();
    expect(withIssue.by('sd-invoice-new')?.getAttribute('href')).toBe('/sal/invoices/new');
  });

  it('SD-INV-ISSUE-UI-014 ?open=<id> opens that invoice once with the first list read and focuses its title', async () => {
    query = { open: '5' };
    const fixture = TestBed.createComponent(SalesInvoicePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page([row]));
    http.expectOne(`${API}/5`).flush(posted);
    fixture.detectChanges();
    await fixture.whenStable();
    const title = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="sd-invoice-detail-title"]');
    expect(title?.textContent).toBe('SI001');
    expect(document.activeElement === title || title?.contains(document.activeElement)).toBe(true);
  });

  it('SD-INV-ISSUE-UI-014b an invalid ?open is ignored', () => {
    query = { open: 'abc' };
    create();
    TestBed.inject(HttpTestingController).expectNone(r => r.url.startsWith(`${API}/`));
  });
});
