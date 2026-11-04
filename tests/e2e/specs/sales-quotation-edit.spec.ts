import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

async function capture(page: import('@playwright/test').Page, state: string): Promise<void> {
  await mkdir('../../tests/qa/SD/screenshots', { recursive: true });
  await page.screenshot({ path: `../../tests/qa/SD/screenshots/2.3-${state}.png`, fullPage: true, animations: 'disabled' });
}

const detail = (version = 1, status = 'Draft', remark = 'Original') => ({
  id: 41, documentNo: 'SQ-0041', version, status, customerId: 7,
  customerCode: 'C-7', customerName: 'Customer Seven', originalGross: 2.1, businessDate: '2028-02-29',
  expiryDate: '2028-03-31', currencyCode: 'USD', remark, lines: [{ id: 5, lineNo: 1,
    itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
    discountRate: 0, taxRateId: 4, taxRatePercent: 5 }], approvalHistory: [],
});

test('SD-QUOTE-EDIT-002 saved Draft uses a versioned PUT then reads the persisted version', async ({ page }) => {
  let stored = detail();
  let putBody = '';
  let reads = 0;
  await page.route('**/api/v1/sal/quotations/41', route => {
    if (route.request().method() === 'GET') { reads++; return route.fulfill({ json: stored }); }
    putBody = route.request().postData() ?? '';
    stored = detail(2, 'Draft', 'Revised');
    return route.fulfill({ json: stored });
  });
  await page.goto('/sal/quotations/41/edit');
  await expect(page.getByTestId('sd-quote-create-customer')).toContainText('C-7');
  await expect(page.getByTestId('sd-quote-create-remark')).toHaveValue('Original');
  await capture(page, 'ready-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'ready-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'ready-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'ready-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-quote-create-remark').fill('Revised');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-success')).toContainText('SQ-0041');
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
  expect(putBody).toContain('"version":1');
  expect(putBody).toContain('"remark":"Revised"');
  expect(reads).toBe(2);
  expect(stored.version).toBe(2);
});

test('SD-QUOTE-EDIT-003 409 preserves entered text until explicit refresh', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let stored = detail();
  let puts = 0;
  await page.route('**/api/v1/sal/quotations/41', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: stored });
    puts++; stored = detail(2, 'Draft', 'Other editor');
    return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
  });
  await page.goto('/sal/quotations/41/edit');
  await page.getByTestId('sd-quote-create-remark').fill('My unsaved revision');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-edit-conflict')).toBeVisible();
  await capture(page, 'conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'conflict-dark-zh-Hant');
  await expect(page.getByTestId('sd-quote-create-remark')).toHaveValue('My unsaved revision');
  await expect(page.getByTestId('sd-quote-create-save')).toBeDisabled();
  expect(puts).toBe(1);
  await page.getByTestId('sd-quote-edit-refresh').click();
  await expect(page.getByTestId('sd-quote-create-remark')).toHaveValue('Other editor');
  expect(puts).toBe(1);
});

test('SD-QUOTE-EDIT-005 Rejected revision returns to Draft and retains saved number', async ({ page }) => {
  let stored = detail(3, 'Rejected');
  await page.route('**/api/v1/sal/quotations/41', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: stored });
    stored = detail(4, 'Draft', 'Revised after rejection');
    return route.fulfill({ json: stored });
  });
  await page.goto('/sal/quotations/41/edit');
  await page.getByTestId('sd-quote-create-remark').fill('Revised after rejection');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-success')).toContainText('SQ-0041');
  expect(stored.status).toBe('Draft');
  expect(stored.version).toBe(4);
});

test('SD-QUOTE-EDIT-004 Pending is read-only and sends no PUT', async ({ page }) => {
  let puts = 0;
  await page.route('**/api/v1/sal/quotations/41', route => {
    if (route.request().method() === 'PUT') puts++;
    return route.fulfill({ json: detail(3, 'Pending') });
  });
  await page.goto('/sal/quotations/41/edit');
  await expect(page.getByText('只有草稿及已駁回報價可以編輯。')).toBeVisible();
  await expect(page.getByTestId('sd-quote-create-save')).toBeDisabled();
  expect(puts).toBe(0);
});

test('SD-QUOTE-EDIT-006 a failed detail load never sends a replacement', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let puts = 0;
  await page.route('**/api/v1/sal/quotations/41', route => {
    if (route.request().method() === 'PUT') puts++;
    return route.fulfill({ status: 404, json: { errorCode: 'SAL.QUOTATION.NOT_FOUND' } });
  });
  await page.goto('/sal/quotations/41/edit');
  await expect(page.getByTestId('sd-quote-create-save')).toBeDisabled();
  await expect(page.getByRole('button', { name: '重試' })).toBeVisible();
  expect(puts).toBe(0);
});

test('SD-QUOTE-EDIT-012 invalid edit ID cannot fall through to Create', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let writes = 0;
  await page.route('**/api/v1/sal/quotations**', route => {
    if (route.request().method() !== 'GET') writes++;
    return route.fulfill({ status: 404, json: { errorCode: 'SAL.QUOTATION.NOT_FOUND' } });
  });
  await page.goto('/sal/quotations/invalid/edit');
  await expect(page.getByTestId('sd-quote-create-save')).toBeDisabled();
  await expect(page.getByRole('button', { name: '重試' })).toBeVisible();
  expect(writes).toBe(0);
});

test('SD-QUOTE-EDIT-013 ignores an old company detail after switching company', async ({ page }) => {
  let releaseOld: (() => void) | undefined;
  let reads = 0;
  let writes = 0;
  await page.route('**/api/v1/sal/quotations/41', async route => {
    if (route.request().method() !== 'GET') { writes++; return route.fulfill({ json: detail() }); }
    reads++;
    if (reads === 1) {
      await new Promise<void>(resolve => { releaseOld = resolve; });
      return route.fulfill({ json: detail() });
    }
    return route.fulfill({ json: { ...detail(), customerCode: 'HK-8', customerName: 'Hong Kong Customer' } });
  });
  await page.goto('/sal/quotations/41/edit');
  await expect.poll(() => !!releaseOld).toBe(true);
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('sd-quote-create-customer')).toContainText('HK-8');
  releaseOld?.();
  await expect(page.getByTestId('sd-quote-create-customer')).toContainText('HK-8');
  expect(writes).toBe(0);
});

test('SD-QUOTE-EDIT-010 validation, 422 and 500 keep the saved document unchanged', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/quotations\/41/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  let puts = 0;
  const stored = detail();
  await page.route('**/api/v1/sal/quotations/41', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: stored });
    puts++;
    return puts === 1 ? route.fulfill({ status: 422, json: { errorCode: 'MD.CUSTOMER.NOT_APPROVED' } })
      : route.fulfill({ status: 500, json: { traceId: 'edit-500-trace' } });
  });
  await page.goto('/sal/quotations/41/edit');
  await page.getByTestId('sd-quote-create-date').fill('');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-errors')).toBeVisible();
  expect(puts).toBe(0);
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('sd-quote-create-date').fill('2028-02-29');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-error')).toBeVisible();
  expect(stored.remark).toBe('Original');
  await capture(page, 'rejected-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'rejected-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await page.getByTestId('sd-quote-create-save').click();
  await expect(page.getByTestId('sd-quote-create-uncertain')).toContainText('修改結果未能確認');
  await expect(page.getByTestId('sd-quote-create-uncertain')).toContainText('edit-500-trace');
  await expect(page.getByTestId('sd-quote-create-save')).toBeDisabled();
  expect(puts).toBe(2);
  expect(stored.remark).toBe('Original');
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
});

test.describe('SD-QUOTE-EDIT-007 exact edit permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false,
    permissions: [{ code: 'SAL.QUOTATION.VIEW', dataScope: 'All' }] } });
  test('hides Edit and blocks the deep link before a PUT', async ({ page }) => {
    let puts = 0;
    await page.route('**/api/v1/sal/quotations**', route => {
      if (route.request().method() === 'PUT') puts++;
      return route.request().url().endsWith('/41') ? route.fulfill({ json: detail() })
        : route.fulfill({ json: { items: [detail()], totalCount: 1, page: 1, pageSize: 20 } });
    });
    await page.goto('/sal/quotations');
    await page.getByText('SQ-0041').click();
    await expect(page.getByTestId('sd-quote-edit-link')).toHaveCount(0);
    await page.goto('/sal/quotations/41/edit');
    await expect(page).toHaveURL(/\/forbidden/);
    expect(puts).toBe(0);
    await capture(page, 'no-permission-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-permission-dark-zh-Hant');
  });
});
