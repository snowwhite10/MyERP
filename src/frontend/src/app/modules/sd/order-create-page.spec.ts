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
import { SalesOrderCreatePage } from './order-create-page';

const QUOTE = '/api/v1/sal/price-rules/quote';
const API = '/api/v1/sal/orders';
const quoteJson = () => '{"asOf":"2028-02-29","ruleId":5,"ruleVersion":3,"itemId":9,"customerId":7,' +
  '"currencyCode":"TWD","unitPrice":100.500000,"discountCeiling":10.0000,"validFrom":"2028-01-01","validTo":"2028-12-31"}';
const savedJson = (line: string) => '{"id":81,"documentNo":"SO-0081","customerId":7,"customerCode":"C-7",' +
  '"customerName":"Customer Seven","businessDate":"2028-02-29","currencyCode":"TWD","status":"Draft","sourceQuotationId":null,' +
  '"version":2,"remark":null,"lines":[{"id":1,"lineNo":1,"itemId":9,"itemCode":"I-9","itemName":"Item Nine","quantity":1,' +
  `"unitPrice":100.5,"discountRate":0,"taxRateId":4,"taxRatePercent":5,"deliveredQuantity":0,"pendingQuantity":0,${line}}]}`;

describe('SD-OPRICE-UI manual order form pricing controls (WU-SD-005b2b)', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let params: Record<string, string>;
  let select: ReturnType<typeof vi.fn>;
  let canOverride: boolean;
  beforeEach(() => {
    params = {};
    canOverride = true;
    select = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { get snapshot() { return { paramMap: convertToParamMap(params) }; } } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select } },
      { provide: PermissionService, useValue: { has: (code: string) => code !== 'SAL.PRICING.OVERRIDE' || canOverride } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isQuote = (r: HttpRequest<unknown>) => r.url === QUOTE;
  function create(saved?: string) {
    const fixture = TestBed.createComponent(SalesOrderCreatePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    if (saved !== undefined) http.expectOne(r => r.url === `${API}/${params['id']}` && r.method === 'GET').flush(saved);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, page: fixture.componentInstance, http, by, text, render: () => fixture.detectChanges() };
  }
  /** Customer, item, tax and date — the date change triggers the dated quote (header currency TWD). */
  function fill(page: SalesOrderCreatePage, http: HttpTestingController) {
    select.mockReturnValueOnce(of([{ id: 7, code: 'C-7', name: 'Customer Seven' }]));
    page.pickCustomer();
    select.mockReturnValueOnce(of([{ id: 9, code: 'I-9', name: 'Item Nine' }]));
    page.pickItem(0);
    http.expectNone(isQuote);
    page.draft.lines[0].taxRateId = 4;
    page.draft.businessDate = '2028-02-29';
    page.dateChanged();
  }

  it('SD-OPRICE-UI-001 applies the dated rule price with the header currency and sends the quote pair on save', () => {
    const { page, http, text, by, render } = create();
    fill(page, http);
    const quote = http.expectOne(isQuote);
    expect(quote.request.params.get('itemId')).toBe('9');
    expect(quote.request.params.get('customerId')).toBe('7');
    expect(quote.request.params.get('currency')).toBe('TWD');
    expect(quote.request.params.get('businessDate')).toBe('2028-02-29');
    render();
    expect(text('sd-order-pricing-0')).toContain('Looking up the price rule');
    quote.flush(quoteJson());
    render();
    expect(page.draft.lines[0].unitPrice).toBe('100.5');
    expect(text('sd-order-pricing-0')).toContain('Price rule #5 (version 3): unit price 100.5, discount ceiling 10%, valid to 2028-12-31.');
    expect(by('sd-order-reason-0')).toBeNull();
    page.save();
    const post = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toContain('"unitPrice":100.5,');
    expect(post.request.body).toContain('"taxRateId":4,"pricingRuleId":5,"pricingRuleVersion":3}');
    expect(post.request.body).not.toContain('overrideReason');
    post.flush({ errorCode: 'MD.ITEM.NOT_FOUND' }, { status: 422, statusText: 'Unprocessable' });
  });

  it('SD-OPRICE-UI-002 a header currency change re-quotes every line', () => {
    const { page, http } = create();
    fill(page, http);
    http.expectOne(isQuote).flush(quoteJson());
    page.draft.currencyCode = 'USD'; page.currencyChanged();
    const again = http.expectOne(isQuote);
    expect(again.request.params.get('currency')).toBe('USD');
    expect(page.draft.lines[0].pricingRuleId).toBeNull();
    again.flush({ errorCode: 'SAL.PRICING.RULE_NOT_FOUND' }, { status: 422, statusText: 'Unprocessable' });
    expect(page.draft.lines[0].unitPrice).toBe('100.5');
    page.draft.currencyCode = 'us'; page.currencyChanged();
    http.expectNone(isQuote);
  });

  it('SD-OPRICE-UI-003 a deviation needs a reason before any POST; Use rule price clears it', () => {
    const { page, http, text, by, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush(quoteJson());
    page.draft.lines[0].unitPrice = '99'; page.draft.lines[0].discountRate = '12'; render();
    expect(text('sd-order-override-needed-0')).toContain('The price differs from the rule');
    page.save(); render();
    http.expectNone(r => r.method === 'POST');
    expect(page.issues()['lines[0].overrideReason']).toBe('required');
    expect(text('sd-order-pricing-0')).toContain('Enter an override reason.');
    by('sd-order-use-rule-0')!.click(); render();
    expect(page.draft.lines[0].unitPrice).toBe('100.5');
    expect(page.draft.lines[0].discountRate).toBe('10');
    expect(by('sd-order-override-needed-0')).toBeNull();
    expect(page.issues()['lines[0].overrideReason']).toBeUndefined();
  });

  it('SD-OPRICE-UI-004 no rule without override permission says so up front; reason sent trimmed; 403 names the line', () => {
    canOverride = false;
    const { page, http, text, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush({ errorCode: 'SAL.PRICING.RULE_NOT_FOUND' }, { status: 422, statusText: 'Unprocessable' });
    render();
    expect(text('sd-order-pricing-0')).toContain('No price rule applies');
    expect(text('sd-order-override-needed-0')).toContain('You do not have pricing override permission');
    page.draft.lines[0].unitPrice = '80';
    page.draft.lines[0].overrideReason = '  Launch discount  ';
    page.save();
    const post = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toContain('"taxRateId":4,"overrideReason":"Launch discount"}');
    post.flush({ errorCode: 'SAL.PRICING.OVERRIDE_REQUIRED', lineNo: 1 }, { status: 403, statusText: 'Forbidden' });
    render();
    expect(text('sd-order-line-error-0')).toContain('This line needs pricing override permission. The order was not saved.');
    expect(text('sd-order-create-error')).toContain('Line 1: This line needs pricing override permission');
    expect(page.uncertain()).toBe(false);
    expect(text('sd-order-create-success')).toBeNull();
  });

  it('SD-OPRICE-UI-005 duplicate rules on save clear the hint and list the IDs; late older quote reply ignored', () => {
    const { page, http, text, render } = create();
    fill(page, http);
    const first = http.expectOne(isQuote);
    page.requote(0);
    const second = http.expectOne(isQuote);
    second.flush({ errorCode: 'SAL.PRICING.DUPLICATE_ACTIVE_RULE', conflictingRuleIds: [4, 6] }, { status: 409, statusText: 'Conflict' });
    first.flush(quoteJson());
    render();
    expect(page.draft.lines[0].unitPrice).toBe('0');
    expect(text('sd-order-pricing-0')).toContain('Several active price rules apply (#4, #6)');
  });

  it('SD-OPRICE-UI-006 removing a line keeps the other line price source', () => {
    const { page, http, text, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush({ errorCode: 'SAL.PRICING.RULE_NOT_FOUND' }, { status: 422, statusText: 'Unprocessable' });
    page.addLine();
    select.mockReturnValueOnce(of([{ id: 9, code: 'I-9', name: 'Item Nine' }]));
    page.pickItem(1);
    http.expectOne(isQuote).flush(quoteJson());
    page.removeLine(0); render();
    expect(text('sd-order-pricing-0')).toContain('Price rule #5');
  });

  it('SD-OPRICE-UI-007 edit resends the saved pair; STALE_QUOTE is a line problem, not a version conflict', () => {
    params = { id: '81' };
    const { page, http, text, render } = create(savedJson('"pricingMode":"Rule","pricingRuleId":5,"pricingRuleVersion":3,' +
      '"ruleUnitPrice":100.500000,"ruleDiscountCeiling":10.0000,"overrideReason":null'));
    expect(text('sd-order-pricing-0')).toContain('Saved price rule #5 (version 3): rule price 100.5, discount ceiling 10%.');
    page.draft.remark = 'changed';
    page.save();
    const put = http.expectOne(r => r.url === `${API}/81` && r.method === 'PUT');
    expect(put.request.body).toContain('"pricingRuleId":5,"pricingRuleVersion":3');
    put.flush({ errorCode: 'SAL.PRICING.STALE_QUOTE', lineNo: 1 }, { status: 409, statusText: 'Conflict' });
    render();
    expect(page.conflict()).toBe(false);
    expect(text('sd-order-line-error-0')).toContain('The price rule changed');
    http.expectOne(isQuote).flush(quoteJson().replace('"ruleVersion":3', '"ruleVersion":4').replace('100.500000', '101'));
    render();
    expect(page.draft.lines[0].unitPrice).toBe('101');
    expect(page.draft.lines[0].pricingRuleVersion).toBe(4);
    expect(text('sd-order-pricing-0')).toContain('Price rule #5 (version 4)');
  });

  it('SD-OPRICE-UI-008 edit: legacy line shows an optional reason; override line keeps its reason; version 409 stays a conflict', () => {
    params = { id: '81' };
    const legacy = create(savedJson('"pricingMode":"Legacy"'));
    expect(legacy.text('sd-order-pricing-0')).toContain('saved before price rules');
    expect(legacy.by('sd-order-reason-0')).not.toBeNull();
    legacy.fixture.destroy();
    const override = create(savedJson('"pricingMode":"Override","pricingRuleId":null,"pricingRuleVersion":null,' +
      '"ruleUnitPrice":null,"ruleDiscountCeiling":null,"overrideReason":"Clearance"'));
    expect(override.page.draft.lines[0].overrideReason).toBe('Clearance');
    expect(override.text('sd-order-pricing-0')).toContain('No price rule applies');
    override.page.save();
    override.http.expectNone(r => r.method === 'PUT');
    expect(override.page.noChange()).toBe(true);
    override.page.draft.remark = 'changed';
    override.page.save();
    const put = override.http.expectOne(r => r.url === `${API}/81` && r.method === 'PUT');
    expect(put.request.body).toContain('"overrideReason":"Clearance"');
    expect(put.request.body).not.toContain('pricingRuleId');
    put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    override.render();
    expect(override.page.conflict()).toBe(true);
  });

  it('SD-OPRICE-UI-009 a 400 override reason error opens and requires that line reason and names line 1', () => {
    const { page, http, by, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush(quoteJson());
    page.save();
    http.expectOne(r => r.method === 'POST').flush({ errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { 'lines[0].overrideReason': ['An override reason of 1 to 200 characters is required.'] } },
      { status: 400, statusText: 'Bad Request' });
    render();
    expect(by('sd-order-reason-0')).not.toBeNull();
    expect(page.issues()['lines[0].overrideReason']).toBe('required');
    expect(by('sd-order-create-error')!.textContent).toContain('Line 1: The override reason must be 1–200 characters.');
  });

  it('SD-OPRICE-UI-010 quotation conversion never looks up prices', () => {
    const { page, http } = create();
    select.mockReturnValueOnce(of([{ id: 17, documentNo: 'SQ-0017', customerName: 'Customer Seven' }]));
    page.pickQuotation();
    page.convert();
    http.expectNone(isQuote);
    http.expectOne(r => r.url === `${API}/from-quotation/17`).flush({}, { status: 0, statusText: 'Network' });
  });
});
