import { mkdir } from 'node:fs/promises';
import { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** WU-SD-005b2b (ADR-165): manual order form pricing controls on top of ADR-159 quote and ADR-160 save decisions. */
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

async function mockMasterData(page: Page): Promise<void> {
  await page.route('**/api/v1/sal/document-options/customers**', route => route.fulfill({ json: { items: [
    { id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/document-options/items**', route => route.fulfill({ json: { items: [
    { id: 9, code: 'I-9', name: 'Item Nine', isActive: true }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/document-options/output-tax-rates**', route => route.fulfill({ json: { items:
    [{ id: 4, code: 'OUT', name: 'Output VAT', ratePercent: 5 }], totalCount: 1, page: 1, pageSize: 20 } }));
}

async function fillHeaderAndLine(page: Page): Promise<void> {
  await page.getByTestId('sd-order-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-date').fill('2028-02-29');
  await page.getByTestId('sd-order-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
}

const savedOrder = (lines: object[]) => ({ id: 81, documentNo: 'SO-0081', status: 'Draft', version: 1, customerId: 7,
  customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29', currencyCode: 'TWD',
  baseCurrencyCode: 'TWD', exchangeRate: 1, sourceQuotationId: null, remark: null as string | null, approvalInstanceId: null,
  approvalHistory: [], originalNet: 100.5, originalTax: 5.03, originalGross: 105.53, baseNet: 100.5, baseTax: 5.03,
  baseGross: 105.53, lines });

test('SD-OPRICE-UI-101 rule price is applied, a deviation needs a reason and the saved body carries the quote', async ({ page }) => {
  let quotes = 0;
  let body = '';
  await mockMasterData(page);
  await page.route('**/api/v1/sal/price-rules/quote**', route => { quotes++; return route.fulfill({ json: rule }); });
  await page.route('**/api/v1/sal/orders**', route => {
    if (route.request().method() === 'POST') { body = route.request().postData() ?? '';
      return route.fulfill({ status: 201, json: savedOrder([{ id: 1, lineNo: 1, itemId: 9, itemCode: 'I-9', itemName: 'Item Nine' }]) }); }
    return route.fulfill({ json: savedOrder([{ id: 1, lineNo: 1, itemId: 9, itemCode: 'I-9', itemName: 'Item Nine' }]) });
  });
  await page.goto('/sal/orders/new');
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('選好客戶、物料、幣別與單據日期後');
  await fillHeaderAndLine(page);
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('價格規則 #5（版本 3）：單價 100.5，折扣上限 10%，有效至 2028-12-31');
  await expect(page.getByTestId('sd-order-create-price-0')).toHaveValue('100.5');
  await expect(page.getByTestId('sd-order-reason-0')).toHaveCount(0);
  await bothThemes(page, '2.5-pricing-rule');
  await page.getByTestId('sd-order-create-price-0').fill('95');
  await page.getByTestId('sd-order-create-discount-0').fill('12');
  await expect(page.getByTestId('sd-order-override-needed-0')).toContainText('單價與規則不同或折扣超過上限');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('請填寫覆核理由。');
  await expect(page.getByTestId('sd-order-reason-0')).toBeFocused();
  expect(body).toBe('');
  await bothThemes(page, '2.5-pricing-deviation');
  for (const [lang, text] of [['en', 'The price differs from the rule'], ['zh-Hans', '单价与规则不同']] as const) {
    await chooseLanguage(page, lang);
    await expect(page.getByTestId('sd-order-override-needed-0')).toContainText(text);
    await capture(page, `2.5-pricing-deviation-light-${lang}`);
  }
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-order-use-rule-0').click();
  await expect(page.getByTestId('sd-order-create-price-0')).toHaveValue('100.5');
  await expect(page.getByTestId('sd-order-create-discount-0')).toHaveValue('10');
  await expect(page.getByTestId('sd-order-reason-0')).toHaveCount(0);
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0081');
  expect(body).toContain('"unitPrice":100.5,"discountRate":10,');
  expect(body).toContain('"pricingRuleId":5,"pricingRuleVersion":3}');
  expect(body).not.toContain('overrideReason');
  expect(quotes).toBe(1);
});

test('SD-OPRICE-UI-102 no rule: the reason is sent, and a refused override marks the line without success', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 403/);
  let body = '';
  let writes = 0;
  await mockMasterData(page);
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ status: 422,
    json: { errorCode: 'SAL.PRICING.RULE_NOT_FOUND' } }));
  await page.route('**/api/v1/sal/orders', route => { writes++; body = route.request().postData() ?? '';
    return route.fulfill({ status: 403, json: { errorCode: 'SAL.PRICING.OVERRIDE_REQUIRED', lineNo: 1 } }); });
  await page.goto('/sal/orders/new');
  await fillHeaderAndLine(page);
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('沒有適用的價格規則');
  await expect(page.getByTestId('sd-order-reason-0')).toBeVisible();
  await page.getByTestId('sd-order-create-price-0').fill('80');
  await page.getByTestId('sd-order-reason-0').fill('  新品上市優惠  ');
  await bothThemes(page, '2.5-pricing-no-rule');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-line-error-0')).toContainText('此行需要定價覆核權限，訂單未儲存');
  await expect(page.getByTestId('sd-order-create-error')).toContainText('第 1 行');
  // UX P2 fix: the shared message must not say 'quotation' on the order page.
  await expect(page.getByTestId('sd-order-create-error')).toContainText('有明細行需要定價覆核，你沒有覆核權限');
  await expect(page.getByTestId('sd-order-create-error')).not.toContainText('報價');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-uncertain')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-save')).toBeEnabled();
  expect(writes).toBe(1);
  expect(body).toContain('"overrideReason":"新品上市優惠"}');
  expect(body).not.toContain('pricingRuleId');
  await bothThemes(page, '2.5-pricing-override-refused');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-order-line-error-0')).toContainText('This line needs pricing override permission. The order was not saved.');
  await expect(page.getByTestId('sd-order-create-error')).toContainText('Line 1: This line needs pricing override permission');
  await capture(page, '2.5-pricing-override-refused-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, '2.5-pricing-override-refused-light-zh-Hans');
});

test.describe('SD-OPRICE-UI-103 without override permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }], companyId: 1,
    isFullAccess: false, permissions: [{ code: 'SAL.ORDER.VIEW', dataScope: 'All' },
      { code: 'SAL.ORDER.CREATE', dataScope: 'All' }] } });
  test('says up front that a deviating line cannot be saved', async ({ page }) => {
    await mockMasterData(page);
    await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: rule }));
    await page.goto('/sal/orders/new');
    await fillHeaderAndLine(page);
    await page.getByTestId('sd-order-create-price-0').fill('90');
    await expect(page.getByTestId('sd-order-override-needed-0')).toContainText('你沒有定價覆核權限，此行目前無法儲存');
    await bothThemes(page, '2.5-pricing-no-override-permission');
  });
});

test('SD-OPRICE-UI-104 edit: a changed rule is re-quoted for that line only, kept reason resent, saved source shown in detail', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let puts = 0;
  let lastPut = '';
  const ruleLine = { id: 1, lineNo: 1, itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 100.5,
    discountRate: 0, taxRateId: 4, taxRatePercent: 5, deliveredQuantity: 0, pendingQuantity: 0, remainingQuantity: 1,
    originalNet: 100.5, originalTax: 5.03, originalGross: 105.53, pricingMode: 'Rule', pricingRuleId: 5,
    pricingRuleVersion: 3, ruleUnitPrice: 100.5, ruleDiscountCeiling: 10, overrideReason: null };
  const overrideLine = { ...ruleLine, id: 2, lineNo: 2, unitPrice: 80, originalNet: 80, originalTax: 4, originalGross: 84,
    pricingMode: 'Override', pricingRuleId: null, pricingRuleVersion: null, ruleUnitPrice: null, ruleDiscountCeiling: null,
    overrideReason: '舊品清倉' };
  let stored = savedOrder([ruleLine, overrideLine]);
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: { ...rule, ruleVersion: 4, unitPrice: 101 } }));
  await page.route(/\/api\/v1\/sal\/orders\?/, route => route.fulfill({ json: { items: [stored], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/orders/81', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: stored });
    puts++; lastPut = route.request().postData() ?? '';
    if (puts === 1) return route.fulfill({ status: 409, json: { errorCode: 'SAL.PRICING.STALE_QUOTE', lineNo: 1 } });
    stored = { ...stored, version: 2, remark: (JSON.parse(lastPut) as { draft: { remark: string } }).draft.remark, lines: [{ ...ruleLine, unitPrice: 101, pricingRuleVersion: 4, ruleUnitPrice: 101 }, overrideLine] };
    return route.fulfill({ json: stored });
  });
  await page.goto('/sal/orders/81/edit');
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('已儲存的價格規則 #5（版本 3）');
  await expect(page.getByTestId('sd-order-pricing-1')).toContainText('沒有適用的價格規則');
  await expect(page.getByTestId('sd-order-reason-1')).toHaveValue('舊品清倉');
  await page.getByTestId('sd-order-create-remark').fill('改備註');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-line-error-0')).toContainText('價格規則已變更，已重新取得目前價格');
  await expect(page.getByTestId('sd-order-edit-conflict')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-pricing-0')).toContainText('價格規則 #5（版本 4）：單價 101');
  await expect(page.getByTestId('sd-order-create-price-0')).toHaveValue('101');
  await expect(page.getByTestId('sd-order-pricing-1')).toContainText('沒有適用的價格規則');
  await bothThemes(page, '2.6-pricing-stale-requoted');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0081');
  expect(puts).toBe(2);
  expect(lastPut).toContain('"version":1');
  expect(lastPut).toContain('"unitPrice":101,');
  expect(lastPut).toContain('"pricingRuleId":5,"pricingRuleVersion":4}');
  expect(lastPut).toContain('"overrideReason":"舊品清倉"}');
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-row-81').getByRole('button').click();
  await expect(page.getByTestId('sd-order-detail-pricing-1')).toContainText('規則 #5（版本 4）');
  await expect(page.getByTestId('sd-order-detail-pricing-2')).toContainText('覆核（無規則）：舊品清倉');
  await bothThemes(page, '2.4-pricing-detail-source');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-order-detail-pricing-2')).toContainText('Override (no rule): 舊品清倉');
  await capture(page, '2.4-pricing-detail-source-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, '2.4-pricing-detail-source-light-zh-Hans');
});
