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
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SalesPriceRuleFormPage } from './price-rule-form-page';

const API = '/api/v1/sal/price-rules';
const CURRENCIES = [{ code: 'TWD', name: 'New Taiwan dollar', decimalPlaces: 0 }, { code: 'USD', name: 'US dollar', decimalPlaces: 2 }];
const rule = (extra: Record<string, unknown> = {}) => JSON.stringify({ id: 5, itemId: 7, customerId: null, currencyCode: 'USD',
  validFrom: '2028-01-01', validTo: '2028-12-31', unitPrice: 0, discountCeiling: 0, isActive: true, version: 3,
  createdAt: '2028-01-01T00:00:00', updatedAt: null, itemCode: 'I1', itemName: 'Bolt', customerCode: null, customerName: null, ...extra })
  .replace('"unitPrice":0', '"unitPrice":1234567890123.123456').replace('"discountCeiling":0', '"discountCeiling":12.5000');

describe('SD-PRULE-FORM-UI price rule create/edit page (WU-SD-005b1b2)', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let params: Record<string, string>;
  let select: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    company.set({ id: 1 });
    params = {};
    select = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { get snapshot() { return { paramMap: convertToParamMap(params), queryParamMap: convertToParamMap({}) }; } } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select } },
      { provide: PermissionService, useValue: { has: () => true, hasAny: () => true } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isCurrencies = (r: HttpRequest<unknown>) => r.url === `${API}/currency-options`;
  function create(savedRule?: string) {
    const fixture = TestBed.createComponent(SalesPriceRuleFormPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isCurrencies).flush(CURRENCIES);
    if (savedRule !== undefined) http.expectOne(r => r.url === `${API}/${params['id']}` && r.method === 'GET').flush(savedRule);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const save = () => { fixture.componentInstance.save(); fixture.detectChanges(); };
    return { fixture, page: fixture.componentInstance, http, element, by, text, save, render: () => fixture.detectChanges() };
  }
  function fill(page: SalesPriceRuleFormPage) {
    select.mockReturnValueOnce(of([{ id: 7, code: 'I1', name: 'Bolt' }]));
    page.pickItem();
    Object.assign(page.draft, { currencyCode: 'USD', validFrom: '2028-01-01', validTo: '2028-12-31', unitPrice: '1250.50', discountCeiling: '10' });
    page.changed();
  }
  const isPost = (r: HttpRequest<unknown>) => r.url === API && r.method === 'POST';

  it('SD-PRULE-FORM-UI-001 creates a default rule from pricing options and shows success only after the reply', () => {
    const { page, http, by, text, save, render } = create();
    fill(page); render();
    expect(text('sd-price-rule-form-item')).toBe('I1 · Bolt');
    expect(text('sd-price-rule-form-currency-hint')).toContain('USD amounts use 2 decimal places');
    expect((select.mock.calls[0][0] as { descriptionKey: string }).descriptionKey).toBe('sd.priceRuleForm.activeItemsOnly');
    save();
    const post = http.expectOne(isPost);
    expect(post.request.body).toBe('{"itemId":7,"customerId":null,"currencyCode":"USD","validFrom":"2028-01-01",' +
      '"validTo":"2028-12-31","unitPrice":1250.50,"discountCeiling":10}');
    expect(by('sd-price-rule-form-success')).toBeNull();
    post.flush(rule({ id: 41, version: 1 }));
    render();
    expect(text('sd-price-rule-form-success')).toContain('Price rule 41 was created and is active.');
    expect((by('sd-price-rule-form-save') as HTMLButtonElement).disabled).toBe(true);
    expect(text('sd-price-rule-form-locked')).toContain('Create another');
    expect(page.canLeave()).toBe(true);
    by('sd-price-rule-form-another')!.click(); render();
    expect(text('sd-price-rule-form-item')).toBe('No item selected');
    expect((by('sd-price-rule-form-save') as HTMLButtonElement).disabled).toBe(false);
    http.expectNone(isPost);
  });

  it('SD-PRULE-FORM-UI-002 lists every invalid field, sends nothing, and needs a customer for the customer scope', () => {
    const { page, http, text, save, render } = create();
    page.draft.scope = 'customer'; page.draft.unitPrice = '1.1234567'; page.changed();
    save();
    http.expectNone(isPost);
    expect(text('sd-price-rule-form-errors')).toContain('6 field(s) need attention');
    expect(text('sd-price-rule-form-customer-error')).toContain('Choose a customer');
    expect(text('sd-price-rule-form-price-error')).toContain('at most 6 decimal places');
    fill(page);
    select.mockReturnValueOnce(of([{ id: 4, code: 'C-4', name: 'Harbour' }]));
    page.pickCustomer(); page.draft.validTo = '2027-12-31'; page.changed(); render();
    expect(text('sd-price-rule-form-to-error')).toContain('on or after');
    page.draft.validTo = '2028-01-01'; page.changed(); save();
    expect(http.expectOne(isPost).request.body).toContain('"customerId":4,');
  });

  it('SD-PRULE-FORM-UI-003 an overlap names the conflicting rules, links to them and lets the user fix the dates', () => {
    const { page, http, by, text, save, render } = create();
    fill(page); save();
    http.expectOne(isPost).flush({ errorCode: 'SAL.PRICING.RULE_OVERLAP', conflictingRuleIds: [12, 3] }, { status: 409, statusText: 'Conflict' });
    render();
    expect(text('sd-price-rule-form-overlap')).toContain('Not saved');
    expect(text('sd-price-rule-form-overlap')).toContain('#12, #3');
    expect(by('sd-price-rule-form-overlap-list')?.getAttribute('href'))
      .toBe('/sal/price-rules?itemId=7&currency=USD&active=true&defaultOnly=true');
    expect(by('sd-price-rule-form-success')).toBeNull();
    page.draft.validFrom = '2029-01-01'; page.draft.validTo = '2029-12-31'; page.changed(); render();
    expect(by('sd-price-rule-form-overlap')).toBeNull();
    save();
    http.expectOne(isPost).flush(rule({ id: 42, version: 1 }));
  });

  it('SD-PRULE-FORM-UI-004 a lock timeout says busy and keeps Save available for a retry', () => {
    const { page, http, by, text, save, render } = create();
    fill(page); save();
    http.expectOne(isPost).flush({ errorCode: 'SAL.PRICING.RULE_LOCK_TIMEOUT' }, { status: 409, statusText: 'Conflict' });
    render();
    expect(text('sd-price-rule-form-busy')).toContain('Try again in a moment');
    expect((by('sd-price-rule-form-save') as HTMLButtonElement).disabled).toBe(false);
    save();
    http.expectOne(isPost).flush(rule({ id: 43, version: 1 }));
  });

  it('SD-PRULE-FORM-UI-005 network failure or 500 locks the form as unknown with the trace ID (no blind resubmit)', () => {
    const { page, http, by, text, save, render } = create();
    fill(page); save();
    http.expectOne(isPost).flush({ traceId: 'T-9' }, { status: 500, statusText: 'Server Error' });
    render();
    expect(text('sd-price-rule-form-uncertain')).toContain('Check the price rule list');
    expect(text('sd-price-rule-form-uncertain')).toContain('T-9');
    expect((by('sd-price-rule-form-save') as HTMLButtonElement).disabled).toBe(true);
    expect(text('sd-price-rule-form-locked')).toContain('last result is unknown');
    save();
    http.expectNone(isPost);
  });

  it('SD-PRULE-FORM-UI-006 MD refusals and server field errors are shown on their own fields', () => {
    const { page, http, text, save, render } = create();
    fill(page); save();
    http.expectOne(isPost).flush({ errorCode: 'MD.ITEM.INACTIVE' }, { status: 422, statusText: 'Unprocessable' });
    render();
    expect(text('sd-price-rule-form-item-error')).toBe('This item is inactive. Choose an active item.');
    save();
    http.expectOne(isPost).flush({ errorCode: 'MD.CURRENCY.INACTIVE' }, { status: 422, statusText: 'Unprocessable' });
    render();
    expect(text('sd-price-rule-form-currency-error')).toContain('This currency is inactive');
    save();
    http.expectOne(isPost).flush({ errors: { unitPrice: ['x'], discountCeiling: ['y'] } }, { status: 400, statusText: 'Bad Request' });
    render();
    expect(text('sd-price-rule-form-price-error')).toContain('The server refused this value');
    expect(text('sd-price-rule-form-ceiling-error')).toContain('The server refused this value');
    save();
    http.expectOne(isPost).flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    render();
    expect(text('sd-price-rule-form-error')).toContain('permission');
  });

  it('SD-PRULE-FORM-UI-007 edit prefills exact digits, PUTs the version read and adopts the new version', () => {
    params = { id: '5' };
    const { page, http, by, text, save, render } = create(rule());
    expect(text('sd-price-rule-form-item')).toBe('I1 · Bolt');
    expect(page.draft.unitPrice).toBe('1234567890123.123456');
    expect(page.draft.discountCeiling).toBe('12.5000');
    expect(page.canLeave()).toBe(true);
    page.draft.unitPrice = '99'; page.changed(); save();
    const put = http.expectOne(r => r.url === `${API}/5` && r.method === 'PUT');
    expect(put.request.body).toBe('{"itemId":7,"customerId":null,"currencyCode":"USD","validFrom":"2028-01-01",' +
      '"validTo":"2028-12-31","unitPrice":99,"discountCeiling":12.5000,"version":3}');
    put.flush(rule({ version: 4 }).replace('1234567890123.123456', '99.000000'));
    render();
    expect(text('sd-price-rule-form-success')).toContain('Price rule 5 was saved (version 4)');
    expect(by('sd-price-rule-form-another')).toBeNull();
    expect(page.draft.unitPrice).toBe('99.000000');
  });

  it('SD-PRULE-FORM-UI-008 a stale version is explained, edits stop, and Reload restores the current rule', () => {
    params = { id: '5' };
    const { page, http, by, text, save, render } = create(rule());
    page.draft.unitPrice = '99'; page.changed(); save();
    http.expectOne(r => r.method === 'PUT').flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    render();
    expect(text('sd-price-rule-form-stale')).toContain('someone else changed this rule');
    expect((by('sd-price-rule-form-save') as HTMLButtonElement).disabled).toBe(true);
    by('sd-price-rule-form-refresh')!.click();
    http.expectOne(r => r.url === `${API}/5` && r.method === 'GET').flush(rule({ version: 6 }));
    render();
    expect(by('sd-price-rule-form-stale')).toBeNull();
    expect(page.draft.unitPrice).toBe('1234567890123.123456');
    save();
    expect(http.expectOne(r => r.method === 'PUT').request.body).toMatch(/"version":6}$/);
  });

  it('SD-PRULE-FORM-UI-009 an inactive rule cannot be edited; a rule deactivated meanwhile is explained', async () => {
    params = { id: '5' };
    const first = create(rule({ isActive: false }));
    expect(first.text('sd-price-rule-form-inactive')).toContain('inactive and cannot be changed');
    expect((first.by('sd-price-rule-form-save') as HTMLButtonElement).disabled).toBe(true);
    await first.fixture.whenStable(); first.render();
    expect((first.by('sd-price-rule-form-price') as HTMLInputElement).disabled).toBe(true);
    first.save();
    first.http.expectNone(r => r.method === 'PUT');
    first.fixture.destroy();
    const second = create(rule());
    second.page.draft.unitPrice = '1'; second.page.changed(); second.save();
    second.http.expectOne(r => r.method === 'PUT').flush({ errorCode: 'SAL.PRICING.RULE_INACTIVE' }, { status: 422, statusText: 'Unprocessable' });
    second.render();
    expect(second.text('sd-price-rule-form-became-inactive')).toContain('deactivated by someone else');
    expect(second.by('sd-price-rule-form-refresh')).not.toBeNull();
  });

  it('SD-PRULE-FORM-UI-010 another company\'s or invalid rule ID shows "not found" without a form; invalid IDs send nothing', () => {
    params = { id: '5' };
    const fixture = TestBed.createComponent(SalesPriceRuleFormPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isCurrencies).flush(CURRENCIES);
    http.expectOne(`${API}/5`).flush({ errorCode: 'SAL.PRICE_RULE.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="sd-price-rule-form-load-error"]')?.textContent).toContain('does not exist in the current company');
    expect(element.querySelector('[data-testid="sd-price-rule-form-form"]')).toBeNull();
    fixture.destroy();
    params = { id: 'abc' };
    const invalid = TestBed.createComponent(SalesPriceRuleFormPage);
    TestBed.tick();
    http.expectOne(isCurrencies).flush(CURRENCIES);
    http.expectNone(r => r.url.startsWith(`${API}/`) && !r.url.endsWith('currency-options'));
    invalid.detectChanges();
    expect((invalid.nativeElement as HTMLElement).textContent).toContain('does not exist in the current company');
  });

  it('SD-PRULE-FORM-UI-011 a deleted item, unlisted saved currency and currency load failure are all explained', () => {
    params = { id: '5' };
    const fixture = TestBed.createComponent(SalesPriceRuleFormPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isCurrencies).flush({ traceId: 'T-2' }, { status: 500, statusText: 'Server Error' });
    http.expectOne(`${API}/5`).flush(rule({ itemCode: null, itemName: null, currencyCode: 'JPY' }));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const text = (id: string) => element.querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s+/g, ' ').trim();
    expect(text('sd-price-rule-form-item')).toBe('Deleted item (ID 7)');
    expect(text('sd-price-rule-form-currency-load-error')).toBeTruthy();
    expect(text('sd-price-rule-form-currency')).toContain('JPY (inactive or not available)');
    element.querySelector<HTMLElement>('[data-testid="sd-price-rule-form-currency-retry"]')!.click();
    http.expectOne(isCurrencies).flush(CURRENCIES);
    fixture.detectChanges();
    expect(text('sd-price-rule-form-currency-load-error')).toBeUndefined();
  });

  it('SD-PRULE-FORM-UI-012 unsaved changes ask before leaving; a company switch starts a fresh rule', () => {
    const { page, http, text, render } = create();
    fill(page);
    const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false);
    expect(page.canLeave()).toBe(false);
    expect(ask).toHaveBeenCalledWith('You have unsaved changes to this price rule. Leave anyway?');
    company.set({ id: 2 });
    TestBed.tick();
    http.expectOne(isCurrencies).flush(CURRENCIES);
    render();
    expect(text('sd-price-rule-form-item')).toBe('No item selected');
    expect(page.canLeave()).toBe(true);
    ask.mockRestore();
  });
});
