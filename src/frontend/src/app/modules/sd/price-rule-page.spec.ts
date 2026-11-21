import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { MAIN_MENU } from '../../layout/menu';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SalesPriceRulePage } from './price-rule-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/price-rules';
const rule = (id: number, extra = '') => `{"id":${id},"itemId":7,"customerId":null,"currencyCode":"TWD",` +
  '"validFrom":"2028-01-01","validTo":"2028-12-31","unitPrice":1234.500000,"discountCeiling":12.5000,"isActive":true,' +
  `"version":3,"createdAt":"2028-01-01T00:00:00","updatedAt":null,"itemCode":"I1","itemName":"Bolt",` +
  `"customerCode":null,"customerName":null${extra}}`;
const page = (items: string[], total = items.length) => `{"items":[${items.join(',')}],"page":1,"pageSize":20,"totalCount":${total}}`;

describe('SD-PRULE-UI price rule list page (WU-SD-005b1a)', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let confirm: ReturnType<typeof vi.fn>;
  let select: ReturnType<typeof vi.fn>;
  let query: Record<string, string>;
  beforeEach(() => {
    company.set({ id: 1 });
    granted = new Set(['SAL.PRICING.VIEW', 'SAL.PRICING.DEACTIVATE']);
    confirm = vi.fn(() => of(true));
    select = vi.fn();
    query = {};
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { get snapshot() { return { queryParamMap: convertToParamMap(query) }; } } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { confirm, select } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function create(body = page([rule(5)])) {
    const fixture = TestBed.createComponent(SalesPriceRulePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne(isList);
    first.flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, http, element, by, text, first, render: () => fixture.detectChanges() };
  }

  it('SD-PRULE-UI-002 lists rules with MD labels, company default tag, exact price and ceiling', () => {
    const { text } = create(page([rule(5), rule(6, ',"customerId":3').replace('"customerId":null,', '')
      .replace('"customerCode":null,"customerName":null', '"customerCode":"C3","customerName":"Acme"')]));
    const row5 = text('sd-price-rule-row-5')!;
    expect(row5).toContain('I1 · Bolt');
    expect(row5).toContain('Company default');
    expect(row5).toContain('1,234.5');
    expect(row5).toContain('12.5%');
    expect(row5).toContain('Active');
    expect(text('sd-price-rule-row-6')).toContain('C3 · Acme');
  });

  it('SD-PRULE-UI-003 names a deleted item by ID and explains empty versus filtered lists', () => {
    const { text, element, fixture, http, by } = create(page([rule(5).replace('"itemCode":"I1","itemName":"Bolt"',
      '"itemCode":null,"itemName":null')]));
    expect(text('sd-price-rule-row-5')).toContain('Deleted item (ID 7)');
    fixture.componentInstance.status = 'inactive';
    by('sd-price-rule-search')!.click();
    const request = http.expectOne(isList);
    expect(request.request.params.get('active')).toBe('false');
    request.flush(page([]));
    fixture.detectChanges();
    expect(element.textContent).toContain('No price rules match these filters.');
    fixture.componentInstance.reset();
    http.expectOne(isList).flush(page([]));
    fixture.detectChanges();
    expect(element.textContent).toContain('No price rules yet.');
  });

  it('SD-PRULE-UI-004 validates currency, date and customer scope before sending; uppercases currency', () => {
    const { fixture, http, text, render } = create();
    const component = fixture.componentInstance;
    component.currency = 'US'; component.search(); render();
    expect(text('sd-price-rule-validation')).toContain('three-letter');
    component.currency = 'usd'; component.asOf = '2028-02-30'; component.search(); render();
    expect(text('sd-price-rule-validation')).toContain('valid date');
    component.asOf = '2028-02-29'; component.scope = 'customer'; component.search(); render();
    expect(text('sd-price-rule-validation')).toContain('Choose a customer');
    http.expectNone(isList);
    component.scope = 'default'; component.search();
    const request = http.expectOne(isList);
    expect(request.request.params.get('currencyCode')).toBe('USD');
    expect(request.request.params.get('defaultOnly')).toBe('true');
    expect(request.request.params.get('asOf')).toBe('2028-02-29');
    request.flush(page([]));
  });

  it('SD-PRULE-UI-005 restores filters, page and sort from the URL and names the item from the result', () => {
    query = { itemId: '7', customerId: '3', currency: 'TWD', active: 'true', page: '2', pageSize: '50', sort: 'id' };
    const { first, fixture } = create(page([rule(5).replace('"customerId":null', '"customerId":3')], 51));
    const params = first.request.params;
    expect([params.get('itemId'), params.get('customerId'), params.get('currencyCode'), params.get('active'),
      params.get('page'), params.get('pageSize'), params.get('sort')]).toEqual(['7', '3', 'TWD', 'true', '2', '50', 'id']);
    expect(fixture.componentInstance.scope).toBe('customer');
    expect(fixture.componentInstance.itemFilterLabel()).toBe('I1 · Bolt');
  });

  it('SD-PRULE-UI-006 deactivates with the shown version after a danger confirmation, then reloads', () => {
    const { by, http, text, render } = create();
    by('sd-price-rule-deactivate-5')!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ titleKey: 'sd.priceRule.deactivateTitle', danger: true,
      params: { id: 5, item: 'I1 · Bolt', from: '2028-01-01', to: '2028-12-31' } });
    const request = http.expectOne(`${API}/5/deactivate`);
    expect(request.request.body).toBe('{"version":3}');
    request.flush(rule(5).replace('"isActive":true', '"isActive":false').replace('"version":3', '"version":4'));
    http.expectOne(isList).flush(page([rule(5).replace('"isActive":true', '"isActive":false')]));
    render();
    expect(text('sd-price-rule-notice')).toContain('Price rule 5 was deactivated (version 4)');
    expect(by('sd-price-rule-deactivate-5')).toBeNull();
    expect(text('sd-price-rule-row-5')).toContain('Inactive');
  });

  it('SD-PRULE-UI-007 cancelling sends nothing; a stale version is explained and the list reloads', () => {
    const { by, http, text, render } = create();
    confirm.mockReturnValueOnce(of(false));
    by('sd-price-rule-deactivate-5')!.click();
    http.expectNone(`${API}/5/deactivate`);
    by('sd-price-rule-deactivate-5')!.click();
    http.expectOne(`${API}/5/deactivate`).flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(isList).flush(page([rule(5)]));
    render();
    expect(text('sd-price-rule-notice')).toContain('changed by someone else');
  });

  it('SD-PRULE-UI-008 a server error keeps the list and shows the trace ID', () => {
    const { by, http, text, render } = create();
    by('sd-price-rule-deactivate-5')!.click();
    http.expectOne(`${API}/5/deactivate`).flush({ traceId: 'T-1' }, { status: 500, statusText: 'Server Error' });
    http.expectNone(isList);
    render();
    expect(text('sd-price-rule-notice')).toContain('T-1');
    expect(by('sd-price-rule-row-5')).not.toBeNull();
  });

  it('SD-PRULE-UI-009 hides deactivate without SAL.PRICING.DEACTIVATE and for inactive rules', () => {
    granted.delete('SAL.PRICING.DEACTIVATE');
    const { by } = create(page([rule(5), rule(6).replace('"isActive":true', '"isActive":false')]));
    expect(by('sd-price-rule-deactivate-5')).toBeNull();
    expect(by('sd-price-rule-deactivate-6')).toBeNull();
  });

  it('SD-PRULE-UI-010 a list failure offers retry; company change reloads page 1 without the old customer', () => {
    const { http, text, render, fixture } = create();
    fixture.componentInstance.customerId = 3; fixture.componentInstance.scope = 'customer';
    fixture.componentInstance.load();
    http.expectOne(isList).flush({}, { status: 500, statusText: 'Server Error' });
    render();
    expect(text('sd-price-rule-retry')).toContain('Retry');
    company.set({ id: 2 });
    TestBed.tick();
    const reload = http.expectOne(isList);
    expect(reload.request.params.get('customerId')).toBeNull();
    expect(reload.request.params.get('page')).toBe('1');
    reload.flush(page([]));
  });

  it('SD-PRULE-UI-011 the route and menu entry require SAL.PRICING.VIEW', () => {
    const route = SD_ROUTES.find(r => r.path === 'price-rules');
    expect(route?.canActivate?.length).toBe(1);
    const sales = MAIN_MENU.find(item => item.testId === 'layout-menu-sal');
    expect(sales?.children?.find(item => item.route === '/sal/price-rules')?.permission).toBe('SAL.PRICING.VIEW');
  });
  it('SD-PRULE-UI-012 item and customer filters choose from the pricing option endpoints, not MD (ADR-162)', () => {
    granted = new Set(['SAL.PRICING.VIEW']);
    const { fixture, http, render, text } = create();
    select.mockReturnValueOnce(of([{ id: 9, code: 'ITEM-9', name: 'Stainless bolt' }]))
      .mockReturnValueOnce(of([{ id: 4, code: 'C-4', name: 'Harbour' }]));
    const component = fixture.componentInstance;
    component.pickItem();
    const itemDialog = select.mock.calls[0][0] as { load: (q: object) => { subscribe: () => void }; titleKey: string };
    expect(itemDialog.titleKey).toBe('sd.priceRule.chooseItem');
    expect((itemDialog as unknown as { descriptionKey: string }).descriptionKey).toBe('sd.priceRule.activeItemsOnly');
    itemDialog.load({ page: 1, pageSize: 10, keyword: ' bolt ' }).subscribe();
    const items = http.expectOne(r => r.url === `${API}/item-options`);
    expect([items.request.params.get('keyword'), items.request.params.get('page'), items.request.params.get('pageSize')])
      .toEqual(['bolt', '1', '10']);
    items.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    component.scope = 'customer';
    component.pickCustomer();
    const customerDialog = select.mock.calls[1][0] as { load: (q: object) => { subscribe: () => void } };
    customerDialog.load({ page: 2, pageSize: 10, keyword: '' }).subscribe();
    const customers = http.expectOne(r => r.url === `${API}/customer-options`);
    expect(customers.request.params.has('keyword')).toBe(false);
    customers.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
    http.expectNone(r => r.url.startsWith('/api/v1/md/'));
    render();
    expect(text('sd-price-rule-item')).toBe('ITEM-9 · Stainless bolt');
    expect(text('sd-price-rule-customer')).toBe('C-4 · Harbour');
    component.search();
    const list = http.expectOne(isList);
    expect([list.request.params.get('itemId'), list.request.params.get('customerId')]).toEqual(['9', '4']);
    list.flush(page([]));
  });
});
