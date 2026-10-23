import { mkdir } from 'node:fs/promises';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const comparison = { rfqId: 17, comparisonDate: '2026-10-23', baseCurrencyCode: 'TWD', lines: [
  { rfqLineId: 170, lineNo: 1, quantity: 2, offers: [
    { quotationId: 31, supplierId: 7, quotationLineId: 51, unitPrice: 10.123456, currencyCode: 'USD',
      taxRateId: 9, isComparable: true, unavailableReason: null, exchangeRate: 2, exchangeRateId: 42,
      exchangeRateFrom: '2026-10-01', exchangeRateTo: '2026-10-31', taxRatePercent: 5,
      taxRateFrom: '2026-01-01', taxRateTo: null, baseNet: 40.49, baseTax: 2.02,
      baseGross: 42.51, status: 'Active' },
    { quotationId: 32, supplierId: 8, quotationLineId: 52, unitPrice: 9.5, currencyCode: 'EUR',
      taxRateId: 9, isComparable: false, unavailableReason: 'MD.EXCHANGE_RATE.NOT_FOUND',
      exchangeRate: null, exchangeRateId: null, exchangeRateFrom: null, exchangeRateTo: null,
      taxRatePercent: null, taxRateFrom: null, taxRateTo: null, baseNet: null, baseTax: null,
      baseGross: null, status: 'Active' },
  ] },
  { rfqLineId: 171, lineNo: 2, quantity: 1, offers: [] },
] };

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: `${directory}/4.15-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('PUR-COMPARE-UI-001 shows sorted live cost evidence and every ineligible reason in three languages', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => {
    methods.push(route.request().method()); return route.fulfill({ json: comparison });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-comparison-line-1')).toContainText('42.51 TWD');
  await expect(page.getByTestId('pur-comparison-offer-31')).toContainText('2026-10-01');
  await expect(page.getByTestId('pur-comparison-offer-31')).toContainText('42');
  await expect(page.getByTestId('pur-comparison-offer-32')).toContainText('此日期沒有有效匯率');
  await expect(page.getByTestId('pur-comparison-offer-32')).not.toContainText('42.51');
  await expect(page.getByTestId('pur-comparison-no-offers')).toBeVisible();
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
  await expect(page.getByTestId('pur-comparison-offer-31').getByText('42.51 TWD')).toBeInViewport();
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
  expect(methods).toEqual(['GET']);
});

test('PUR-COMPARE-UI-002 clears stale cost on empty, business rule and server failures', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/rfqs\/17\/comparison/, 500);
  let mode = 'normal';
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => {
    if (mode === 'empty') return route.fulfill({ json: { ...comparison, lines: [] } });
    if (mode === 'draft') return route.fulfill({ status: 422, json: { errorCode: 'PUR.RFQ.NOT_SENT' } });
    if (mode === 'server') return route.fulfill({ status: 500, json: { traceId: 'compare-trace' } });
    return route.fulfill({ json: comparison });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-comparison-offer-31')).toBeVisible();
  mode = 'empty'; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-no-lines')).toBeVisible();
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await chooseTheme(page, 'light');
  mode = 'draft'; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-error')).toContainText('只有已發送');
  await expect(page.getByTestId('pur-comparison-offer-31')).toHaveCount(0);
  await capture(page, 'rule-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'rule-error-dark-zh-Hant');
  await chooseTheme(page, 'light');
  mode = 'server'; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-error')).toContainText('compare-trace');
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
});

test('PUR-COMPARE-UI-004 switches company before showing new costs and explains network failure', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource|net::ERR_FAILED/);
  const companyHeaders: string[] = [];
  let disconnected = false;
  await page.route('**/api/v1/pur/rfqs/17/comparison', route => {
    companyHeaders.push(route.request().headers()['x-company-id'] ?? '');
    if (disconnected) return route.abort();
    return route.fulfill({ json: route.request().headers()['x-company-id'] === '2'
      ? { ...comparison, lines: [] } : comparison });
  });
  await page.goto('/pur/rfqs/17/comparison');
  await expect(page.getByTestId('pur-comparison-offer-31')).toBeVisible();
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('pur-comparison-no-lines')).toBeVisible();
  await expect(page.getByTestId('pur-comparison-offer-31')).toHaveCount(0);
  await capture(page, 'company-empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'company-empty-dark-zh-Hant');
  await chooseTheme(page, 'light');
  disconnected = true; await page.getByTestId('pur-comparison-refresh').click();
  await expect(page.getByTestId('pur-comparison-error')).toBeVisible();
  await expect(page.getByTestId('pur-comparison-no-lines')).toHaveCount(0);
  await capture(page, 'network-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'network-error-dark-zh-Hant');
  expect(companyHeaders).toContain('2');
});

test.describe('comparison permission boundary', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [
    { code: 'PUR.RFQ.VIEW', dataScope: 'Company' },
  ] } });
  test('PUR-COMPARE-UI-004 denies direct navigation without COMPARE before requesting data', async ({ page }) => {
    let calls = 0;
    await page.route('**/api/v1/pur/rfqs/17/comparison', route => { calls++; return route.fulfill({ json: comparison }); });
    await page.goto('/pur/rfqs/17/comparison');
    await expect(page).toHaveURL(/\/forbidden/);
    expect(calls).toBe(0);
  });
});
