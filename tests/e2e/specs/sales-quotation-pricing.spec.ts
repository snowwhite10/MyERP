import { mkdir } from 'node:fs/promises';
import { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** WU-SD-005b2a (ADR-164): quotation form pricing controls on top of ADR-159 quote and ADR-160 save decisions. */
async function capture(page: Page, name: string): Promise<void> {
  await mkdir('../../tests/qa/SD/screenshots', { recursive: true });
  await page.screenshot({ path: `../../tests/qa/SD/screenshots/2.2-pricing-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function bothThemes(page: Page, name: string): Promise<void> {
  await capture(page, `${name}-light-zh-Hant`);
  await chooseTheme(page, 'dark'); await capture(page, `${name}-dark-zh-Hant`);
  await chooseTheme(page, 'light');
}

const rule = { asOf: '2028-02-29', ruleId: 5, ruleVersion: 3, itemId: 9, customerId: 7, currencyCode: 'TWD',
  unitPrice: 100.5, discountCeiling: 10, validFrom: '2028-01-01', validTo: '2028-12-31' };

async function mockMasterData(page: Page): Promise<void> {
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: { items: [
    { id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/items**', route => route.fulfill({ json: { items: [
    { id: 9, code: 'I-9', name: 'Item Nine', isActive: true }], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/tax-codes**', route => route.fulfill({ json: { items:
    route.request().url().includes('/rates') ? [{ id: 4, effectiveFrom: '2028-01-01', effectiveTo: null, ratePercent: 5 }]
      : [{ id: 2, code: 'OUT', name: 'Output VAT', taxType: 'Output', isActive: true }], totalCount: 1, page: 1, pageSize: 20 } }));
}

async function fillHeaderAndLine(page: Page): Promise<void> {
  await page.getByTestId('sd-quote-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByText('5%', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-expiry').fill('2028-03-31');
  await page.getByTestId('sd-quote-create-date').fill('2028-02-29');
}

test('SD-QPRICE-UI-101 rule price is applied, a deviation needs a reason and the saved body carries the quote', async ({ page }) => {
  let quotes = 0;
  let body = '';
  await mockMasterData(page);
  await page.route('**/api/v1/sal/price-rules/quote**', route => { quotes++; return route.fulfill({ json: rule }); });
  await page.route('**/api/v1/sal/quotations', route => { body = route.request().postData() ?? '';
    return route.fulfill({ status: 201, json: { id: 41, documentNo: 'SQ-0041', version: 1 } }); });
  await page.goto('/sal/quotations/new');
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('選好客戶、物料、幣別與單據日期後');
  await fillHeaderAndLine(page);
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('價格規則 #5（版本 3）：單價 100.5，折扣上限 10%，有效至 2028-12-31');
  await expect(page.getByTestId('sd-quote-create-price-0')).toHaveValue('100.5');
  await expect(page.getByTestId('sd-quote-reason-0')).toHaveCount(0);
  await bothThemes(page, 'rule');
  await page.getByTestId('sd-quote-create-price-0').fill('95');
  await page.getByTestId('sd-quote-create-discount-0').fill('12');
  await expect(page.getByTestId('sd-quote-override-needed-0')).toContainText('單價與規則不同或折扣超過上限');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('請填寫覆核理由。');
  expect(body).toBe('');
  await bothThemes(page, 'deviation');
  for (const [lang, text] of [['en', 'The price differs from the rule'], ['zh-Hans', '单价与规则不同']] as const) {
    await chooseLanguage(page, lang);
    await expect(page.getByTestId('sd-quote-override-needed-0')).toContainText(text);
    await capture(page, `deviation-light-${lang}`);
  }
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-quote-use-rule-0').click();
  await expect(page.getByTestId('sd-quote-create-price-0')).toHaveValue('100.5');
  await expect(page.getByTestId('sd-quote-create-discount-0')).toHaveValue('10');
  await expect(page.getByTestId('sd-quote-reason-0')).toHaveCount(0);
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-success')).toContainText('SQ-0041');
  expect(body).toContain('"unitPrice":100.5,"discountRate":10,');
  expect(body).toContain('"pricingRuleId":5,"pricingRuleVersion":3}');
  expect(body).not.toContain('overrideReason');
  expect(quotes).toBe(1);
});

test('SD-QPRICE-UI-102 no rule: the reason is sent, and a refused override marks the line without success', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 403/);
  let body = '';
  await mockMasterData(page);
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ status: 422,
    json: { errorCode: 'SAL.PRICING.RULE_NOT_FOUND' } }));
  await page.route('**/api/v1/sal/quotations', route => { body = route.request().postData() ?? '';
    return route.fulfill({ status: 403, json: { errorCode: 'SAL.PRICING.OVERRIDE_REQUIRED', lineNo: 1 } }); });
  await page.goto('/sal/quotations/new');
  await fillHeaderAndLine(page);
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('沒有適用的價格規則');
  await expect(page.getByTestId('sd-quote-reason-0')).toBeVisible();
  await page.getByTestId('sd-quote-create-price-0').fill('80');
  await page.getByTestId('sd-quote-reason-0').fill('  新品上市優惠  ');
  await bothThemes(page, 'no-rule');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-line-error-0')).toContainText('此行需要定價覆核權限，報價未儲存');
  await expect(page.getByTestId('sd-quote-create-error')).toContainText('第 1 行');
  await expect(page.getByTestId('sd-quote-create-success')).toHaveCount(0);
  expect(body).toContain('"overrideReason":"新品上市優惠"}');
  expect(body).not.toContain('pricingRuleId');
  await bothThemes(page, 'override-refused');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-quote-line-error-0')).toContainText('This line needs pricing override permission');
  await capture(page, 'override-refused-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'override-refused-light-zh-Hans');
});

test.describe('SD-QPRICE-UI-103 without override permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }], companyId: 1,
    isFullAccess: false, permissions: [{ code: 'SAL.QUOTATION.VIEW', dataScope: 'All' },
      { code: 'SAL.QUOTATION.CREATE', dataScope: 'All' }] } });
  test('says up front that a deviating line cannot be saved', async ({ page }) => {
    await mockMasterData(page);
    await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: rule }));
    await page.goto('/sal/quotations/new');
    await fillHeaderAndLine(page);
    await page.getByTestId('sd-quote-create-price-0').fill('90');
    await expect(page.getByTestId('sd-quote-override-needed-0')).toContainText('你沒有定價覆核權限，此行目前無法儲存');
    await bothThemes(page, 'no-override-permission');
  });
});

test('SD-QPRICE-UI-104 edit: a changed rule is re-quoted for that line only and the saved source shows in detail', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let puts = 0;
  let lastPut = '';
  const savedLine = { id: 5, lineNo: 1, itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 100.5,
    discountRate: 0, taxRateId: 4, taxRatePercent: 5, originalGross: 105.53, pricingMode: 'Rule', pricingRuleId: 5,
    pricingRuleVersion: 3, ruleUnitPrice: 100.5, ruleDiscountCeiling: 10, overrideReason: null };
  const overrideLine = { ...savedLine, id: 6, lineNo: 2, unitPrice: 80, originalGross: 84, pricingMode: 'Override',
    pricingRuleId: null, pricingRuleVersion: null, ruleUnitPrice: null, ruleDiscountCeiling: null, overrideReason: '舊品清倉' };
  let stored = { id: 41, documentNo: 'SQ-0041', version: 2, status: 'Draft', customerId: 7, customerCode: 'C-7',
    customerName: 'Customer Seven', businessDate: '2028-02-29', expiryDate: '2028-03-31', currencyCode: 'TWD',
    originalGross: 189.53, remark: null, approvalHistory: [], isExpired: false, lines: [savedLine, overrideLine] };
  await page.route('**/api/v1/sal/price-rules/quote**', route => route.fulfill({ json: { ...rule, ruleVersion: 4, unitPrice: 101 } }));
  await page.route(/\/api\/v1\/sal\/quotations\?/, route => route.fulfill({ json: { items: [stored], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/quotations/41', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: stored });
    puts++; lastPut = route.request().postData() ?? '';
    if (puts === 1) return route.fulfill({ status: 409, json: { errorCode: 'SAL.PRICING.STALE_QUOTE', lineNo: 1 } });
    stored = { ...stored, version: 3, lines: [{ ...savedLine, unitPrice: 101, pricingRuleVersion: 4, ruleUnitPrice: 101 }, overrideLine] };
    return route.fulfill({ json: stored });
  });
  await page.goto('/sal/quotations/41/edit');
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('已儲存的價格規則 #5（版本 3）');
  await expect(page.getByTestId('sd-quote-pricing-1')).toContainText('沒有適用的價格規則');
  await expect(page.getByTestId('sd-quote-reason-1')).toHaveValue('舊品清倉');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-line-error-0')).toContainText('價格規則已變更，已重新取得目前價格');
  await expect(page.getByTestId('sd-quote-edit-conflict')).toHaveCount(0);
  await expect(page.getByTestId('sd-quote-pricing-0')).toContainText('價格規則 #5（版本 4）：單價 101');
  await expect(page.getByTestId('sd-quote-create-price-0')).toHaveValue('101');
  await bothThemes(page, 'stale-requoted');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-success')).toContainText('SQ-0041');
  expect(lastPut).toContain('"version":2');
  expect(lastPut).toContain('"unitPrice":101,');
  expect(lastPut).toContain('"pricingRuleId":5,"pricingRuleVersion":4}');
  expect(lastPut).toContain('"overrideReason":"舊品清倉"}');
  await page.goto('/sal/quotations');
  await page.getByRole('button', { name: 'SQ-0041' }).click();
  await expect(page.getByTestId('sd-quote-detail-pricing-1')).toContainText('規則 #5（版本 4）');
  await expect(page.getByTestId('sd-quote-detail-pricing-2')).toContainText('覆核（無規則）：舊品清倉');
  await bothThemes(page, 'detail-source');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-quote-detail-pricing-2')).toContainText('Override (no rule): 舊品清倉');
  await capture(page, 'detail-source-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'detail-source-light-zh-Hans');
});
