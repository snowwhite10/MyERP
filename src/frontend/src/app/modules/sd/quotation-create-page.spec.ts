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
import { SalesQuotationCreatePage } from './quotation-create-page';

const QUOTE = '/api/v1/sal/price-rules/quote';
const API = '/api/v1/sal/quotations';
const quoteJson = (extra = '') => '{"asOf":"2028-02-29","ruleId":5,"ruleVersion":3,"itemId":9,"customerId":7,' +
  `"currencyCode":"TWD","unitPrice":100.500000,"discountCeiling":10.0000,"validFrom":"2028-01-01","validTo":"2028-12-31"${extra}}`;
const savedJson = (line: string) => `{"id":41,"documentNo":"SQ-0041","customerId":7,"customerCode":"C-7",` +
  `"customerName":"Customer Seven","businessDate":"2028-02-29","expiryDate":"2028-03-31","currencyCode":"TWD","status":"Draft",` +
  `"version":2,"remark":null,"lines":[{"id":1,"lineNo":1,"itemId":9,"itemCode":"I-9","itemName":"Item Nine","quantity":1,` +
  `"unitPrice":100.5,"discountRate":0,"taxRateId":4,"taxRatePercent":5,${line}}]}`;

describe('SD-QPRICE-UI quotation form pricing controls (WU-SD-005b2a)', () => {
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
    const fixture = TestBed.createComponent(SalesQuotationCreatePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    if (saved !== undefined) http.expectOne(r => r.url === `${API}/${params['id']}` && r.method === 'GET').flush(saved);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, page: fixture.componentInstance, http, by, text, render: () => fixture.detectChanges() };
  }
  /** Customer, item, tax and date — the date change triggers the dated quote. */
  function fill(page: SalesQuotationCreatePage, http: HttpTestingController) {
    select.mockReturnValueOnce(of([{ id: 7, code: 'C-7', name: 'Customer Seven' }]));
    page.pickCustomer();
    select.mockReturnValueOnce(of([{ id: 9, code: 'I-9', name: 'Item Nine' }]));
    page.pickItem(0);
    http.expectNone(isQuote);
    page.draft.expiryDate = '2028-03-31';
    page.draft.lines[0].taxRateId = 4;
    page.draft.businessDate = '2028-02-29';
    page.dateChanged();
  }

  it('SD-QPRICE-UI-004 applies the dated rule price and sends the quote pair on save', () => {
    const { page, http, text, by, render } = create();
    fill(page, http);
    const quote = http.expectOne(isQuote);
    expect(quote.request.params.get('itemId')).toBe('9');
    expect(quote.request.params.get('customerId')).toBe('7');
    expect(quote.request.params.get('currency')).toBe('TWD');
    expect(quote.request.params.get('businessDate')).toBe('2028-02-29');
    render();
    expect(text('sd-quote-pricing-0')).toContain('Looking up the price rule');
    quote.flush(quoteJson());
    render();
    expect(page.draft.lines[0].unitPrice).toBe('100.5');
    expect(text('sd-quote-pricing-0')).toContain('Price rule #5 (version 3): unit price 100.5, discount ceiling 10%, valid to 2028-12-31.');
    expect(by('sd-quote-reason-0')).toBeNull();
    expect(by('sd-quote-override-needed-0')).toBeNull();
    page.save();
    const post = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toContain('"unitPrice":100.5,');
    expect(post.request.body).toContain('"taxRateId":4,"pricingRuleId":5,"pricingRuleVersion":3}');
    expect(post.request.body).not.toContain('overrideReason');
    post.flush({ id: 41, documentNo: 'SQ-0041', version: 1 });
  });

  it('SD-QPRICE-UI-005 a changed price or discount above the ceiling needs a reason; Use rule price restores it', () => {
    const { page, http, text, by, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush(quoteJson());
    page.draft.lines[0].unitPrice = '99'; page.draft.lines[0].discountRate = '12'; page.revalidate(); render();
    expect(text('sd-quote-override-needed-0')).toContain('The price differs from the rule');
    expect(by('sd-quote-reason-0')).not.toBeNull();
    page.save(); render();
    http.expectNone(r => r.method === 'POST');
    expect(page.issues()['lines[0].overrideReason']).toBe('required');
    expect(text('sd-quote-pricing-0')).toContain('Enter an override reason.');
    by('sd-quote-use-rule-0')!.click(); render();
    expect(page.draft.lines[0].unitPrice).toBe('100.5');
    expect(page.draft.lines[0].discountRate).toBe('10');
    expect(by('sd-quote-override-needed-0')).toBeNull();
    expect(page.issues()['lines[0].overrideReason']).toBeUndefined();
  });

  it('SD-QPRICE-UI-006 no rule needs a reason, which is sent trimmed; missing override permission is said up front', () => {
    canOverride = false;
    const { page, http, text, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush({ errorCode: 'SAL.PRICING.RULE_NOT_FOUND' }, { status: 422, statusText: 'Unprocessable' });
    render();
    expect(text('sd-quote-pricing-0')).toContain('No price rule applies');
    expect(text('sd-quote-override-needed-0')).toContain('You do not have pricing override permission');
    page.draft.lines[0].unitPrice = '80';
    page.draft.lines[0].overrideReason = '  Launch discount  ';
    page.save();
    const post = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toContain('"taxRateId":4,"overrideReason":"Launch discount"}');
    post.flush({ errorCode: 'SAL.PRICING.OVERRIDE_REQUIRED', lineNo: 1 }, { status: 403, statusText: 'Forbidden' });
    render();
    expect(text('sd-quote-line-error-0')).toContain('This line needs pricing override permission. The quotation was not saved.');
    expect(text('sd-quote-create-error')).toContain('Line 1: This line needs pricing override permission');
    expect(text('sd-quote-create-success')).toBeNull();
  });

  it('SD-QPRICE-UI-007 duplicate active rules give no price and list the rule IDs', () => {
    const { page, http, text, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush({ errorCode: 'SAL.PRICING.DUPLICATE_ACTIVE_RULE', conflictingRuleIds: [4, 6] },
      { status: 409, statusText: 'Conflict' });
    render();
    expect(page.draft.lines[0].unitPrice).toBe('0');
    expect(page.draft.lines[0].pricingRuleId).toBeNull();
    expect(text('sd-quote-pricing-0')).toContain('Several active price rules apply (#4, #6)');
  });

  it('SD-QPRICE-UI-008 only the latest quote reply counts and a failed lookup can be retried', () => {
    const { page, http, text, by, render } = create();
    fill(page, http);
    const first = http.expectOne(isQuote);
    page.draft.lines[0].currencyCode = 'USD'; page.currencyChanged(0);
    const second = http.expectOne(isQuote);
    expect(second.request.params.get('currency')).toBe('USD');
    second.flush({}, { status: 0, statusText: 'Network' });
    first.flush(quoteJson());
    render();
    expect(page.draft.lines[0].unitPrice).toBe('0');
    expect(by('sd-quote-pricing-0')!.querySelector('.error')).not.toBeNull();
    by('sd-quote-requote-0')!.click();
    http.expectOne(isQuote).flush(quoteJson().replace('"TWD"', '"USD"'));
    render();
    expect(text('sd-quote-pricing-0')).toContain('Price rule #5');
  });

  it('SD-QPRICE-UI-009 removing a line keeps the other line price source', () => {
    const { page, http, text, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush({ errorCode: 'SAL.PRICING.RULE_NOT_FOUND' }, { status: 422, statusText: 'Unprocessable' });
    page.addLine();
    expect(page.draft.lines[1].currencyCode).toBe('TWD');
    select.mockReturnValueOnce(of([{ id: 9, code: 'I-9', name: 'Item Nine' }]));
    page.pickItem(1);
    http.expectOne(isQuote).flush(quoteJson());
    page.removeLine(0); render();
    expect(text('sd-quote-pricing-0')).toContain('Price rule #5');
  });

  it('SD-QPRICE-UI-010 edit shows the saved rule, resends its pair and treats STALE_QUOTE as a line problem', () => {
    params = { id: '41' };
    const { page, http, text, render } = create(savedJson('"pricingMode":"Rule","pricingRuleId":5,"pricingRuleVersion":3,' +
      '"ruleUnitPrice":100.500000,"ruleDiscountCeiling":10.0000,"overrideReason":null'));
    expect(text('sd-quote-pricing-0')).toContain('Saved price rule #5 (version 3): rule price 100.5, discount ceiling 10%.');
    page.save();
    const put = http.expectOne(r => r.url === `${API}/41` && r.method === 'PUT');
    expect(put.request.body).toContain('"pricingRuleId":5,"pricingRuleVersion":3');
    put.flush({ errorCode: 'SAL.PRICING.STALE_QUOTE', lineNo: 1 }, { status: 409, statusText: 'Conflict' });
    render();
    expect(page.conflict()).toBe(false);
    expect(text('sd-quote-line-error-0')).toContain('The price rule changed');
    const requote = http.expectOne(isQuote);
    requote.flush(quoteJson().replace('"ruleVersion":3', '"ruleVersion":4').replace('100.500000', '101'));
    render();
    expect(page.draft.lines[0].unitPrice).toBe('101');
    expect(page.draft.lines[0].pricingRuleVersion).toBe(4);
    expect(text('sd-quote-pricing-0')).toContain('Price rule #5 (version 4)');
  });

  it('SD-QPRICE-UI-011 edit of a legacy line asks for a reason; an override line keeps its saved reason', () => {
    params = { id: '41' };
    const legacy = create(savedJson('"pricingMode":"Legacy"'));
    expect(legacy.text('sd-quote-pricing-0')).toContain('saved before price rules');
    expect(legacy.by('sd-quote-reason-0')).not.toBeNull();
    legacy.fixture.destroy();
    const override = create(savedJson('"pricingMode":"Override","pricingRuleId":null,"pricingRuleVersion":null,' +
      '"ruleUnitPrice":null,"ruleDiscountCeiling":null,"overrideReason":"Clearance"'));
    expect(override.page.draft.lines[0].overrideReason).toBe('Clearance');
    expect(override.text('sd-quote-pricing-0')).toContain('No price rule applies');
    override.page.save();
    const put = override.http.expectOne(r => r.url === `${API}/41` && r.method === 'PUT');
    expect(put.request.body).toContain('"overrideReason":"Clearance"');
    expect(put.request.body).not.toContain('pricingRuleId');
    put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    override.render();
    expect(override.page.conflict()).toBe(true);
  });

  it('SD-QPRICE-UI-012 a 400 override reason error opens and requires that line reason', () => {
    const { page, http, by, render } = create();
    fill(page, http);
    http.expectOne(isQuote).flush(quoteJson());
    page.save();
    http.expectOne(r => r.method === 'POST').flush({ errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { 'lines[0].overrideReason': ['An override reason of 1 to 200 characters is required.'] } },
      { status: 400, statusText: 'Bad Request' });
    render();
    expect(by('sd-quote-reason-0')).not.toBeNull();
    expect(page.issues()['lines[0].overrideReason']).toBe('required');
    expect(by('sd-quote-create-error')!.textContent).toContain('Line 1: The override reason must be 1–200 characters.');
  });
  it('SD-DOCOPT-UI-002 pickers read SD document options and the tax picker needs the business date (WU-SD-FORM-OPTIONS)', async () => {
    const { fixture, page, http, by, render } = create();
    render();
    await fixture.whenStable();
    expect((by('sd-quote-create-tax-pick-0') as HTMLButtonElement).disabled).toBe(true);
    expect(by('sd-quote-create-tax-hint-0')!.textContent).toContain('Enter the document date first');
    page.pickTaxRate(0);
    expect(select).not.toHaveBeenCalled();
    select.mockReturnValue(of(null));
    const query = { page: 1, pageSize: 20, keyword: 'a' };
    page.pickCustomer();
    select.mock.calls[0][0].load(query).subscribe();
    http.expectOne(r => r.url === '/api/v1/sal/document-options/customers' && r.params.get('keyword') === 'a')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    page.pickItem(0);
    select.mock.calls[1][0].load(query).subscribe();
    http.expectOne(r => r.url === '/api/v1/sal/document-options/items').flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    http.expectNone(r => r.url.includes('/md/'));
    const date = by('sd-quote-create-date') as HTMLInputElement;
    date.value = '2028-02-29'; date.dispatchEvent(new Event('input'));
    render();
    expect(page.draft.businessDate).toBe('2028-02-29');
    expect((by('sd-quote-create-tax-pick-0') as HTMLButtonElement).disabled).toBe(false);
    expect(by('sd-quote-create-tax-hint-0')).toBeNull();
    select.mockReturnValueOnce(of([{ id: 4, code: 'OUT', name: 'Output VAT', ratePercent: 5 }]));
    page.pickTaxRate(0);
    const options = select.mock.calls[2][0];
    expect(options.columns.map((c: { key: string }) => c.key)).toEqual(['code', 'name', 'rate']);
    options.load(query).subscribe();
    const rates = http.expectOne(r => r.url === '/api/v1/sal/document-options/output-tax-rates');
    expect(rates.request.params.get('businessDate')).toBe('2028-02-29');
    rates.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    expect(select).toHaveBeenCalledTimes(3);
    expect(page.draft.lines[0].taxRateId).toBe(4);
    render();
    expect(page.taxLabel(0)).toBe('OUT · 5%');
  });
});
