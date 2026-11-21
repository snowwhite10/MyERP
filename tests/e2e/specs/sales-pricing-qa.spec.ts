import { mkdir } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/**
 * WU-SD-005b3b (ADR-168): pricing error-state UX QA round over Interfaces 2.2/2.5 (line quote and save) and
 * 2.11/2.12 (rule list and save). Every state asserts the screen message and what the simulated server received.
 */
async function capture(page: Page, name: string): Promise<void> {
  await mkdir('../../tests/qa/SD/screenshots', { recursive: true });
  await page.screenshot({ path: `../../tests/qa/SD/screenshots/${name}.png`, fullPage: true, animations: 'disabled' });
}

async function bothThemes(page: Page, name: string): Promise<void> {
  await capture(page, `${name}-light-zh-Hant`);
  await chooseTheme(page, 'dark'); await capture(page, `${name}-dark-zh-Hant`);
  await chooseTheme(page, 'light');
}

const rule = { asOf: '2028-02-29', ruleId: 5, ruleVersion: 3, itemId: 9, customerId: 7, currencyCode: 'TWD',
  unitPrice: 100.5, discountCeiling: 10, validFrom: '2028-01-01', validTo: '2028-12-31' };

type QuoteMode = 'ok' | 'timeout' | 'server' | 'network' | 'duplicate' | 'denied';

/** Document options plus a quote endpoint whose answer follows `state.mode`. */
async function mockPricing(page: Page, state: { mode: QuoteMode; quotes: number }): Promise<void> {
  await page.route('**/api/v1/sal/document-options/customers**', route => route.fulfill({ json: { items: [
    { id: 7, code: 'C-7', name: 'Customer Seven' }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/document-options/items**', route => route.fulfill({ json: { items: [
    { id: 9, code: 'I-9', name: 'Item Nine' }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/document-options/output-tax-rates**', route => route.fulfill({ json: { items:
    [{ id: 4, code: 'OUT', name: 'Output VAT', ratePercent: 5 }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/price-rules/quote**', (route: Route) => {
    state.quotes++;
    switch (state.mode) {
      case 'timeout': return new Promise<void>(() => {});
      case 'server': return route.fulfill({ status: 500, json: { traceId: 'trace-quote-500' } });
      case 'network': return route.abort('failed');
      case 'duplicate': return route.fulfill({ status: 409, json: { errorCode: 'SAL.PRICING.DUPLICATE_ACTIVE_RULE',
        conflictingRuleIds: [5, 6] } });
      case 'denied': return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } });
      default: return route.fulfill({ json: rule });
    }
  });
}

async function pick(page: Page, button: string, code: string): Promise<void> {
  await page.getByTestId(button).click();
  await page.getByText(code, { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
}

function allowQuoteErrors(pageErrors: { allow(pattern: RegExp): void; allowApi5xx(url: RegExp, status: number): void }): void {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/price-rules\/quote/, 500);
  pageErrors.allow(/status of (500|409|403|422)/);
  pageErrors.allow(/net::ERR_FAILED/);
}

/** Line quote failure states shared by 2.2 (prefix `sd-quote`) and 2.5 (prefix `sd-order`). */
async function walkQuoteFailures(page: Page, state: { mode: QuoteMode; quotes: number }, pricing: string, requote: string,
  shot: string): Promise<void> {
  const cases: [QuoteMode, string, string | null][] = [
    ['server', '發生未預期的錯誤', 'trace-quote-500'],
    ['network', '無法連線到伺服器', null],
    ['duplicate', '有多條啟用的價格規則同時適用（#5, #6）', null],
    ['denied', '權限', null],
    ['timeout', '取得價格逾時', null],
  ];
  for (const [mode, text, trace] of cases) {
    state.mode = mode;
    const before = state.quotes;
    await page.getByTestId(requote).click();
    await expect(page.getByTestId(pricing)).toContainText(text, { timeout: 12_000 });
    if (trace) await expect(page.getByTestId(pricing)).toContainText(`Trace ID: ${trace}`);
    else await expect(page.getByTestId(pricing)).not.toContainText('Trace ID');
    expect(state.quotes).toBe(before + 1);
    // A failed quote never leaves a rule hint behind and always offers a retry.
    await expect(page.getByTestId(requote)).toBeEnabled();
    await bothThemes(page, `${shot}-quote-${mode}`);
  }
}

test('SD-PRICE-QA-001 quotation 2.2: every quote failure is explained, 5xx with trace ID; number rule gap has its own text', async ({ page, pageErrors }) => {
  test.setTimeout(120_000);
  allowQuoteErrors(pageErrors);
  const state = { mode: 'ok' as QuoteMode, quotes: 0 };
  const posts: string[] = [];
  await mockPricing(page, state);
  await page.route('**/api/v1/sal/quotations', route => { posts.push(route.request().postData() ?? '');
    return route.fulfill({ status: 422, json: { errorCode: 'SYS.NUMBER_RULE.NOT_CONFIGURED' } }); });
  await page.goto('/sal/quotations/new');
  await pick(page, 'sd-quote-create-customer-pick', 'C-7');
  await pick(page, 'sd-quote-create-item-pick-0', 'I-9');
  await page.getByTestId('sd-quote-create-expiry').fill('2028-03-31');
  await page.getByTestId('sd-quote-create-date').fill('2028-02-29');
  await pick(page, 'sd-quote-create-tax-pick-0', 'OUT');
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('價格規則 #5');
  await walkQuoteFailures(page, state, 'sd-quote-pricing-0', 'sd-quote-requote-0', '2.2-qa');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('timed out');
  await capture(page, '2.2-qa-quote-timeout-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, '2.2-qa-quote-timeout-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  state.mode = 'ok';
  await page.getByTestId('sd-quote-requote-0').click();
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('價格規則 #5');
  await page.getByTestId('sd-quote-create-save').click();
  const error = page.getByTestId('sd-quote-create-error');
  await expect(error).toContainText('此單據類型尚未設定單號規則，沒有儲存任何內容');
  await expect(error).not.toContainText('伺服器拒絕客戶');
  await expect(page.getByTestId('sd-quote-create-success')).toHaveCount(0);
  expect(posts).toHaveLength(1);
  expect(posts[0]).toContain('"pricingRuleId":5,"pricingRuleVersion":3}');
  await bothThemes(page, '2.2-qa-number-rule');
  await chooseLanguage(page, 'en');
  await expect(error).toContainText('number rule');
  await capture(page, '2.2-qa-number-rule-light-en');
});

test('SD-PRICE-QA-002 order 2.5: quote failures match the quotation form; number rule gap is not "rejected"', async ({ page, pageErrors }) => {
  test.setTimeout(120_000);
  allowQuoteErrors(pageErrors);
  const state = { mode: 'ok' as QuoteMode, quotes: 0 };
  const posts: string[] = [];
  await mockPricing(page, state);
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().method() === 'POST') { posts.push(route.request().postData() ?? '');
      return route.fulfill({ status: 422, json: { errorCode: 'SYS.NUMBER_RULE.NOT_CONFIGURED' } }); }
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/orders/new');
  await pick(page, 'sd-order-create-customer-pick', 'C-7');
  await pick(page, 'sd-order-create-item-pick-0', 'I-9');
  await page.getByTestId('sd-order-create-date').fill('2028-02-29');
  await pick(page, 'sd-order-create-tax-pick-0', 'OUT');
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('價格規則 #5');
  await walkQuoteFailures(page, state, 'sd-order-pricing-0', 'sd-order-requote-0', '2.5-qa');
  state.mode = 'ok';
  await page.getByTestId('sd-order-requote-0').click();
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('價格規則 #5');
  await page.getByTestId('sd-order-create-save').click();
  const error = page.getByTestId('sd-order-create-error');
  await expect(error).toContainText('此單據類型尚未設定單號規則，沒有儲存任何內容');
  await expect(error).not.toContainText('伺服器拒絕建立訂單');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  expect(posts).toHaveLength(1);
  await bothThemes(page, '2.5-qa-number-rule');
  await chooseLanguage(page, 'zh-Hans');
  await expect(error).toContainText('单号规则');
  await capture(page, '2.5-qa-number-rule-light-zh-Hans');
});

test('SD-PRICE-QA-003 rule list 2.11: timeout, network and no permission each have their own message and retry', async ({ page, pageErrors }) => {
  test.setTimeout(60_000);
  pageErrors.allow(/status of 403/);
  pageErrors.allow(/net::ERR_FAILED/);
  let mode: 'timeout' | 'network' | 'denied' = 'timeout';
  let lists = 0;
  await page.route('**/api/v1/sal/price-rules**', route => {
    if (!/\/price-rules(\?|$)/.test(route.request().url())) return route.fulfill({ json: [] });
    lists++;
    if (mode === 'timeout') return new Promise<void>(() => {});
    if (mode === 'network') return route.abort('failed');
    return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } });
  });
  await page.goto('/sal/price-rules');
  const error = page.getByTestId('sd-price-rule-error');
  await expect(error).toContainText('請求逾時', { timeout: 12_000 });
  await expect(page.getByTestId('sd-price-rule-table')).toContainText('無法載入價格規則');
  await bothThemes(page, '2.11-qa-timeout');
  mode = 'network';
  await page.getByTestId('sd-price-rule-retry').click();
  await expect(error).toContainText('無法連線到伺服器');
  await bothThemes(page, '2.11-qa-network');
  mode = 'denied';
  await page.getByTestId('sd-price-rule-retry').click();
  await expect(error).toContainText('權限');
  await expect(error).not.toContainText('Trace ID');
  await bothThemes(page, '2.11-qa-no-permission');
  expect(lists).toBe(3);
  await chooseLanguage(page, 'en'); await capture(page, '2.11-qa-no-permission-light-en');
});

test('SD-PRICE-QA-004 rule create 2.12: network and timeout are "unknown outcome", 403 says no permission; nothing claims success', async ({ page, pageErrors }) => {
  test.setTimeout(60_000);
  pageErrors.allow(/status of 403/);
  pageErrors.allow(/net::ERR_FAILED/);
  let mode: 'network' | 'denied' | 'timeout' = 'denied';
  const writes: string[] = [];
  await page.route('**/api/v1/sal/price-rules**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/currency-options')) return route.fulfill({ json: [{ code: 'TWD', name: '新台幣', decimalPlaces: 0 }] });
    if (url.pathname.endsWith('/item-options')) return route.fulfill({ json: { items: [{ id: 9, code: 'ITEM-9', name: 'Stainless bolt M8' }],
      totalCount: 1, page: 1, pageSize: 10 } });
    if (route.request().method() !== 'POST') return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
    writes.push(route.request().postData() ?? '');
    if (mode === 'timeout') return new Promise<void>(() => {});
    if (mode === 'network') return route.abort('failed');
    return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } });
  });
  await page.goto('/sal/price-rules/new');
  await page.getByTestId('sd-price-rule-form-item-pick').click();
  await page.getByTestId('erp-select-row-9').click(); await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-price-rule-form-currency').selectOption('TWD');
  await page.getByTestId('sd-price-rule-form-from').fill('2027-01-01');
  await page.getByTestId('sd-price-rule-form-to').fill('2027-12-31');
  await page.getByTestId('sd-price-rule-form-price').fill('120');
  await page.getByTestId('sd-price-rule-form-save').click();
  await expect(page.getByTestId('sd-price-rule-form-error')).toContainText('權限');
  await expect(page.getByTestId('sd-price-rule-form-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-price-rule-form-save')).toBeEnabled();
  await bothThemes(page, '2.12-qa-no-permission');
  mode = 'network';
  await page.getByTestId('sd-price-rule-form-save').click();
  await expect(page.getByTestId('sd-price-rule-form-uncertain')).toContainText('無法確認規則是否已建立');
  await expect(page.getByTestId('sd-price-rule-form-save')).toBeDisabled();
  await expect(page.getByTestId('sd-price-rule-form-success')).toHaveCount(0);
  await bothThemes(page, '2.12-qa-network');
  expect(writes).toHaveLength(2);
  await page.goto('/sal/price-rules/new');
  mode = 'timeout';
  await page.getByTestId('sd-price-rule-form-item-pick').click();
  await page.getByTestId('erp-select-row-9').click(); await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-price-rule-form-currency').selectOption('TWD');
  await page.getByTestId('sd-price-rule-form-from').fill('2027-01-01');
  await page.getByTestId('sd-price-rule-form-to').fill('2027-12-31');
  await page.getByTestId('sd-price-rule-form-price').fill('120');
  await page.getByTestId('sd-price-rule-form-save').click();
  await expect(page.getByTestId('sd-price-rule-form-uncertain')).toContainText('無法確認規則是否已建立', { timeout: 25_000 });
  await expect(page.getByTestId('sd-price-rule-form-success')).toHaveCount(0);
  await bothThemes(page, '2.12-qa-timeout');
  expect(writes).toHaveLength(3);
});
