import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const summary = { id: 31, documentNo: 'SQ-0031', customerId: 7, customerName: 'Saved customer',
  businessDate: '2026-10-20', expiryDate: '2026-10-25', status: 'Approved', originalGross: 12.34, version: 2 };
const detail = { ...summary, customerCode: 'C-7', currencyCode: 'USD', baseCurrencyCode: 'TWD',
  exchangeRate: 32, originalNet: 11, originalTax: 1.34, baseNet: 352, baseTax: 42.88,
  baseGross: 394.88, remark: 'Saved remark', approvalInstanceId: 6, isExpired: true,
  lines: [{ id: 51, lineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Saved item',
    quantity: 2, unitPrice: 5.5, discountRate: 0, taxRateId: 4, taxRatePercent: 12,
    originalNet: 11, originalTax: 1.34, originalGross: 12.34, baseNet: 352, baseTax: 42.88, baseGross: 394.88 }],
  approvalHistory: [{ instanceId: 6, status: 'Approved', submittedAt: '2026-10-21T09:00:00',
    steps: [{ stepNo: 1, name: 'Manager', status: 'Approved', actedBy: 12,
      actedAt: '2026-10-21T10:00:00', comment: 'Checked' }] }] };

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.1-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-QUOTE-UI-001 shows saved details, separate expiry and history in three languages and two themes', async ({ page }) => {
  const requests: string[] = [];
  await page.route('**/api/v1/sal/quotations**', route => {
    const url = new URL(route.request().url()); requests.push(url.pathname);
    return route.fulfill({ json: url.pathname.endsWith('/31') ? detail :
      { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/quotations');
  await expect(page.getByTestId('sd-quote-row-31')).toContainText('SQ-0031');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-quote-row-31').getByRole('button').click();
  await expect(page.getByTestId('sd-quote-detail')).toContainText('Saved remark');
  await expect(page.getByTestId('sd-quote-detail')).toContainText('ITEM-9');
  await expect(page.getByTestId('sd-quote-detail')).toContainText('Checked');
  await expect(page.getByTestId('sd-quote-expired')).toBeVisible();
  await expect(page.getByTestId('sd-quote-detail')).toContainText('已核准');
  await expect(page.getByTestId('sd-quote-detail')).not.toContainText(' · Approved');
  await capture(page, 'detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hant');
  expect(requests).toEqual(['/api/v1/sal/quotations', '/api/v1/sal/quotations/31']);
});

test('SD-QUOTE-UI-002 keeps filters in URL and distinguishes empty and invalid date', async ({ page }) => {
  const queries: URL[] = [];
  await page.route('**/api/v1/sal/quotations**', route => {
    const url = new URL(route.request().url()); queries.push(url);
    const items = url.searchParams.get('status') === 'Draft' ? [] : [summary];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/quotations');
  await expect(page.getByTestId('sd-quote-row-31')).toBeVisible();
  await page.getByTestId('sd-quote-from').fill('2026-10-30');
  await page.getByTestId('sd-quote-to').fill('2026-10-01');
  await page.getByTestId('sd-quote-search').click();
  await expect(page.getByTestId('sd-quote-validation')).toContainText('起始日期不得晚於結束日期');
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
  await chooseTheme(page, 'light');
  expect(queries).toHaveLength(1);
  await page.getByTestId('sd-quote-from').fill('');
  await page.getByTestId('sd-quote-to').fill('');
  await page.getByTestId('sd-quote-status').selectOption('Draft');
  await page.getByTestId('sd-quote-search').click();
  await expect(page.getByTestId('sd-quote-table')).toContainText('沒有符合條件');
  await expect(page).toHaveURL(/status=Draft/);
  await capture(page, 'no-results-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'no-results-dark-zh-Hant');
  expect(queries.at(-1)?.searchParams.get('status')).toBe('Draft');
});

test('SD-QUOTE-UI-003 shows empty and server failure with trace ID and retry', async ({ page, pageErrors }) => {
  let failed = false;
  await page.route('**/api/v1/sal/quotations**', route => failed
    ? route.fulfill({ status: 500, json: { traceId: 'trace-quotation-500' } })
    : route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }));
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/quotations/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  await page.goto('/sal/quotations');
  await expect(page.getByTestId('sd-quote-table')).toContainText('尚無報價單');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  failed = true;
  await page.getByTestId('sd-quote-search').click();
  await expect(page.getByTestId('sd-quote-error')).toContainText('trace-quotation-500');
  await expect(page.getByTestId('sd-quote-table')).not.toContainText('SQ-0031');
  await capture(page, 'server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'server-error-light-zh-Hant');
  failed = false;
  await page.getByRole('button', { name: '重試' }).click();
  await expect(page.getByTestId('sd-quote-error')).toHaveCount(0);
  await expect(page.getByTestId('sd-quote-table')).toContainText('尚無報價單');
});

test.describe('SD-QUOTE-UI-004 read permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false, permissions: [] } });
  test('hides the route and menu without VIEW', async ({ page }) => {
    let readCount = 0;
    await page.route('**/api/v1/sal/quotations**', route => { readCount++; return route.abort(); });
    await page.goto('/sal/quotations');
    await expect(page).toHaveURL(/\/forbidden/);
    await expect(page.getByTestId('layout-menu-sal-quotations')).toHaveCount(0);
    expect(readCount).toBe(0);
    await capture(page, 'no-permission-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-permission-dark-zh-Hant');
  });
});

test('SD-QUOTE-UI-006 explains a missing detail without changing the list', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  await page.route('**/api/v1/sal/quotations**', route =>
    route.request().url().endsWith('/31')
      ? route.fulfill({ status: 404, json: { errorCode: 'SAL.QUOTATION.NOT_FOUND' } })
      : route.fulfill({ json: { items: [summary], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.goto('/sal/quotations');
  await page.getByTestId('sd-quote-row-31').getByRole('button').click();
  await expect(page.getByTestId('sd-quote-detail-error')).toContainText('找不到這張報價單');
  await expect(page.getByTestId('sd-quote-detail')).toHaveCount(0);
  await expect(page.getByTestId('sd-quote-row-31')).toBeVisible();
  await capture(page, 'detail-missing-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-missing-dark-zh-Hant');
});

test('SD-QUOTE-UI-007 shows a retry path after network loss', async ({ page, pageErrors }) => {
  let disconnected = true;
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  await page.route('**/api/v1/sal/quotations**', route => disconnected
    ? route.abort('failed')
    : route.fulfill({ json: { items: [summary], totalCount: 1, page: 1, pageSize: 20 } }));
  await page.goto('/sal/quotations');
  await expect(page.getByTestId('sd-quote-error')).toContainText('無法連線到伺服器');
  await capture(page, 'network-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'network-error-dark-zh-Hant');
  disconnected = false;
  await page.getByRole('button', { name: '重試' }).click();
  await expect(page.getByTestId('sd-quote-row-31')).toBeVisible();
  await expect(page.getByTestId('sd-quote-error')).toHaveCount(0);
});

test('SD-QUOTE-UI-008 sends supported sort and page size to GET and URL', async ({ page }) => {
  const queries: URL[] = [];
  await page.route('**/api/v1/sal/quotations**', route => {
    queries.push(new URL(route.request().url()));
    return route.fulfill({ json: { items: [summary], totalCount: 100, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/quotations');
  await expect(page.getByTestId('sd-quote-row-31')).toBeVisible();
  await page.getByRole('columnheader', { name: '單號' }).click();
  await expect.poll(() => queries.at(-1)?.searchParams.get('sort')).toBe('documentNo');
  await expect(page).toHaveURL(/sort=documentNo/);
  await page.getByText('20 條/頁').click();
  await page.getByText('50 條/頁').last().click();
  await expect.poll(() => queries.at(-1)?.searchParams.get('pageSize')).toBe('50');
  await expect(page).toHaveURL(/pageSize=50/);
});
