import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const saved = { id: 41, documentNo: 'SQ-0041', status: 'Draft', customerId: 7,
  customerName: 'Customer Seven', businessDate: '2028-02-29', expiryDate: '2028-03-31',
  originalGross: 1, version: 1, lines: [] };

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  await mkdir('../../tests/qa/SD/screenshots', { recursive: true });
  await page.screenshot({ path: `../../tests/qa/SD/screenshots/2.2-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-QUOTE-UI-014/015/022 Create validates and saves the exact submitted document', async ({ page }) => {
  let posts = 0;
  let body = '';
  let stored = false;
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: {
    items: [{ id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/items**', route => route.fulfill({ json: {
    items: [{ id: 9, code: 'I-9', name: 'Item Nine', isActive: true }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/tax-codes**', route => route.fulfill({ json: {
    items: route.request().url().includes('/rates')
      ? [{ id: 4, taxCodeId: 2, effectiveFrom: '2028-01-01', effectiveTo: null, ratePercent: 5, version: 1 }]
      : [{ id: 2, code: 'OUT', name: 'Output VAT', taxType: 'Output', isActive: true }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/quotations**', async route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: {
      items: stored ? [saved] : [], totalCount: stored ? 1 : 0, page: 1, pageSize: 20 } });
    posts++; body = route.request().postData() ?? ''; stored = true;
    await route.fulfill({ status: 201, json: saved });
  });
  await page.goto('/sal/quotations/new');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-errors')).toContainText('5 個標示欄位');
  await expect(page.getByTestId('sd-quote-create-form')).toContainText('請選擇客戶');
  expect(posts).toBe(0);
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('sd-quote-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-date').fill('2028-02-29');
  await page.getByTestId('sd-quote-create-expiry').fill('2028-03-31');
  await page.getByTestId('sd-quote-create-quantity-0').fill('1.000001');
  await page.getByTestId('sd-quote-create-price-0').fill('0.000001');
  await page.getByTestId('sd-quote-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByText('5%', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-quote-create-errors')).toHaveCount(0);
  await capture(page, 'ready-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'ready-dark-zh-Hant');
  await chooseTheme(page, 'light');
  page.once('dialog', dialog => { expect(dialog.message()).toContain('未儲存'); void dialog.dismiss(); });
  await page.getByRole('link', { name: '返回報價列表' }).click();
  await expect(page).toHaveURL(/\/sal\/quotations\/new$/);
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-success')).toContainText('SQ-0041');
  expect(posts).toBe(1);
  expect(body).toContain('"quantity":1.000001');
  expect(body).toContain('"unitPrice":0.000001');
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'success-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'success-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.setViewportSize({ width: 768, height: 900 });
  await expect.poll(() => page.locator('nz-sider').evaluate(element => element.getBoundingClientRect().width))
    .toBeLessThanOrEqual(65);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  await capture(page, 'tablet-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'tablet-dark-zh-Hant');
  await page.getByRole('link', { name: '返回報價列表' }).click();
  await expect(page.getByTestId('sd-quote-row-41')).toContainText('SQ-0041');
  expect(stored).toBe(true);
});

test('SD-QUOTE-UI-016/020 server errors keep unsaved data and never report success', async ({ page, pageErrors }) => {
  let attempts = 0;
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/quotations/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 400/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 403/);
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: { items: [
    { id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/items**', route => route.fulfill({ json: { items: [
    { id: 9, code: 'I-9', name: 'Item Nine', isActive: true }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/tax-codes**', route => route.fulfill({ json: { items:
    route.request().url().includes('/rates') ? [{ id: 4, effectiveFrom: '2028-01-01', effectiveTo: null, ratePercent: 5 }]
      : [{ id: 2, code: 'OUT', name: 'Output VAT', taxType: 'Output', isActive: true }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/quotations', route => {
    attempts++;
    if (attempts === 1) return route.fulfill({ status: 400, json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { 'lines[0].currencyCode': ['Mixed currencies are not allowed.'] } } });
    if (attempts === 2) return route.fulfill({ status: 422, json: { errorCode: 'MD.CUSTOMER.NOT_APPROVED' } });
    if (attempts === 3) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
    if (attempts === 4) return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } });
    if (attempts === 5) return route.fulfill({ status: 500, json: { traceId: 'trace-create-500' } });
    return route.fulfill({ status: 201, json: saved });
  });
  await page.goto('/sal/quotations/new');
  await page.getByTestId('sd-quote-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-date').fill('2028-02-29');
  await page.getByTestId('sd-quote-create-expiry').fill('2028-03-31');
  await page.getByTestId('sd-quote-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByText('5%', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-error')).toBeVisible();
  await expect(page.getByTestId('sd-quote-create-error')).toContainText('第 1 行');
  await expect(page.getByTestId('sd-quote-create-error')).toContainText('必須使用同一幣別');
  await capture(page, 'bad-request-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'bad-request-dark-zh-Hant');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-error')).toContainText('客戶須啟用並核准');
  await expect(page.getByTestId('sd-quote-create-success')).toHaveCount(0);
  await capture(page, 'rejected-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'rejected-light-zh-Hant');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-error')).toContainText('已變更');
  await capture(page, 'conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'conflict-dark-zh-Hant');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-error')).toContainText('權限');
  await capture(page, 'permission-denied-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'permission-denied-light-zh-Hant');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-uncertain')).toContainText('trace-create-500');
  await expect(page.getByTestId('sd-quote-create-uncertain')).toContainText('先到報價列表核對');
  await expect(page.getByTestId('sd-quote-create-save')).toBeDisabled();
  await capture(page, 'server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'server-error-light-zh-Hant');
  expect(await page.getByTestId('sd-quote-create-date').inputValue()).toBe('2028-02-29');
  await expect(page.getByTestId('sd-quote-create-error')).toHaveCount(0);
  await expect(page.getByTestId('sd-quote-create-success')).toHaveCount(0);
  expect(attempts).toBe(5);
});

test.describe('SD-QUOTE-UI-018 create permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false,
    permissions: [{ code: 'SAL.QUOTATION.VIEW', dataScope: 'All' }] } });
  test('hides Create and blocks direct navigation before POST', async ({ page }) => {
    let posts = 0;
    await page.route('**/api/v1/sal/quotations**', route => {
      if (route.request().method() === 'POST') posts++;
      return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
    });
    await page.goto('/sal/quotations');
    await expect(page.getByTestId('sd-quote-create-link')).toHaveCount(0);
    await page.goto('/sal/quotations/new');
    await expect(page).toHaveURL(/\/forbidden/);
    expect(posts).toBe(0);
    await capture(page, 'no-permission-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-permission-dark-zh-Hant');
  });
});

test('SD-QUOTE-UI-023 ignores a late save after switching company', async ({ page }) => {
  let release: (() => void) | undefined;
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: { items: [
    { id: 7, code: 'C-7', name: 'Customer Seven', isActive: true, reviewStatus: 'Approved' }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/items**', route => route.fulfill({ json: { items: [
    { id: 9, code: 'I-9', name: 'Item Nine', isActive: true }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/md/tax-codes**', route => route.fulfill({ json: { items:
    route.request().url().includes('/rates') ? [{ id: 4, effectiveFrom: '2028-01-01', effectiveTo: null, ratePercent: 5 }]
      : [{ id: 2, code: 'OUT', name: 'Output VAT', taxType: 'Output', isActive: true }],
    totalCount: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/v1/sal/quotations', async route => {
    await new Promise<void>(resolve => release = resolve);
    await route.fulfill({ status: 201, json: saved });
  });
  await page.goto('/sal/quotations/new');
  await page.getByTestId('sd-quote-create-customer-pick').click();
  await page.getByText('C-7', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-item-pick-0').click();
  await page.getByText('I-9', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-date').fill('2028-02-29');
  await page.getByTestId('sd-quote-create-expiry').fill('2028-03-31');
  await page.getByTestId('sd-quote-create-tax-pick-0').click();
  await page.getByText('OUT', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByText('5%', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-quote-create-save').click();
  await expect.poll(() => !!release).toBe(true);
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('sd-quote-create-customer')).toContainText('尚未選擇客戶');
  release?.();
  await expect(page.getByTestId('sd-quote-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-quote-create-date')).toHaveValue('');
  await capture(page, 'company-switch-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'company-switch-dark-zh-Hant');
});
