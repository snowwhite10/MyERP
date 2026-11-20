import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const summary = { id: 81, documentNo: 'SO-0081', customerId: 7, customerName: 'Saved customer',
  businessDate: '2026-11-18', status: 'Approved', originalGross: 12.34, version: 2 };
const detail = { ...summary, customerCode: 'C-7', currencyCode: 'USD', baseCurrencyCode: 'TWD',
  exchangeRate: 32, sourceQuotationId: 31, originalNet: 11, originalTax: 1.34,
  baseNet: 352, baseTax: 42.88, baseGross: 394.88, remark: 'Saved remark',
  lines: [{ id: 91, lineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Saved item',
    quantity: 2, deliveredQuantity: 0.5, pendingQuantity: 0.25, remainingQuantity: 1.25,
    unitPrice: 5.5, discountRate: 0, taxRatePercent: 12,
    originalNet: 11, originalTax: 1.34, originalGross: 12.34 }] };
async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.4-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-ORDER-UI-002 shows saved list and detail in three languages and two themes', async ({ page }) => {
  const requests: string[] = [];
  await page.route('**/api/v1/sal/orders**', route => {
    const url = new URL(route.request().url()); requests.push(url.pathname);
    return route.fulfill({ json: url.pathname.endsWith('/81') ? detail :
      { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/orders');
  await expect(page.getByTestId('sd-order-row-81')).toContainText('SO-0081');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-order-row-81').getByRole('button').click();
  await expect(page.getByTestId('sd-order-detail')).toContainText('Saved remark');
  await expect(page.getByTestId('sd-order-detail')).toContainText('ITEM-9');
  await expect(page.getByTestId('sd-order-detail')).toContainText('已核准');
  await expect(page.getByTestId('sd-order-line-91')).toContainText('1.25');
  await expect(page.getByTestId('sd-order-detail')).toContainText('審批中預留');
  await expect(page.getByTestId('sd-order-detail')).toContainText('已過帳交貨');
  await expect(page.getByTestId('sd-order-detail')).toContainText('剩餘可交');
  await capture(page, 'detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hant');
  expect(requests).toEqual(['/api/v1/sal/orders', '/api/v1/sal/orders/81']);
});

test('SD-ORDER-UI-009 shows saved approval rounds and empty history without inventing decisions', async ({ page }) => {
  let withHistory = true;
  await page.route('**/api/v1/sal/orders**', route => {
    const url = new URL(route.request().url());
    return route.fulfill({ json: url.pathname.endsWith('/81') ? { ...detail,
      approvalHistory: withHistory ? [{ instanceId: 52, status: 'Rejected', submittedAt: '2026-11-17T09:30:00',
        steps: [{ stepNo: 1, name: 'Manager', status: 'Rejected', comment: 'Limit review' }] }] : [] } :
      { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-row-81').getByRole('button').click();
  await expect(page.getByTestId('sd-order-approval-history')).toContainText('Limit review');
  await expect(page.getByTestId('sd-order-approval-history')).toContainText('Manager');
  await capture(page, 'approval-history-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'approval-history-dark-zh-Hant');
  withHistory = false;
  await page.getByRole('button', { name: /關閉明細/ }).click();
  await page.getByTestId('sd-order-row-81').getByRole('button').click();
  await expect(page.getByTestId('sd-order-approval-history')).toContainText('尚無審批歷史');
});

test('SD-ORDER-UI-003 separates filtered empty, invalid dates and server errors', async ({ page, pageErrors }) => {
  let failure = false;
  const queries: URL[] = [];
  await page.route('**/api/v1/sal/orders**', route => {
    const url = new URL(route.request().url()); queries.push(url);
    if (failure) return route.fulfill({ status: 500, json: { traceId: 'trace-order-500' } });
    const items = url.searchParams.get('status') === 'Draft' ? [] : [summary];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-status').selectOption('Draft');
  await page.getByTestId('sd-order-search').click();
  await expect(page.getByTestId('sd-order-table')).toContainText('沒有符合篩選條件');
  await expect(page).toHaveURL(/status=Draft/);
  await capture(page, 'no-results-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'no-results-dark-zh-Hant');
  failure = true;
  await page.getByTestId('sd-order-search').click();
  await expect(page.getByTestId('sd-order-error')).toContainText('trace-order-500');
  await expect(page.getByTestId('sd-order-table')).not.toContainText('SO-0081');
  await capture(page, 'server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'server-error-light-zh-Hant');
  expect(queries.at(-1)?.searchParams.get('status')).toBe('Draft');
});

test('SD-ORDER-UI-006 distinguishes empty, invalid date, missing detail and offline retry', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let mode: 'empty' | 'saved' | 'offline' = 'empty';
  let listReads = 0; let detailReads = 0;
  await page.route('**/api/v1/sal/orders**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/81')) {
      detailReads++;
      return route.fulfill({ status: 404, json: { errorCode: 'SAL.ORDER.NOT_FOUND' } });
    }
    listReads++;
    if (mode === 'offline') return route.abort('failed');
    const items = mode === 'saved' ? [summary] : [];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/orders');
  await expect(page.getByTestId('sd-order-table')).toContainText('尚未有');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');

  await page.goto('/sal/orders?from=2026-02-30');
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByTestId('sd-order-table')).not.toContainText('SO-0081');
  expect(listReads).toBe(1);
  await capture(page, 'invalid-date-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'invalid-date-light-zh-Hant');

  mode = 'saved';
  await page.locator('input[name="from"]').fill('');
  await page.getByTestId('sd-order-search').click();
  await expect(page.getByTestId('sd-order-table')).toContainText('SO-0081');
  await page.getByTestId('sd-order-row-81').getByRole('button').click();
  await expect(page.getByTestId('sd-order-detail-error')).toContainText('找不到這張訂單');
  await expect(page.getByTestId('sd-order-detail')).toHaveCount(0);
  expect(detailReads).toBe(1);
  await capture(page, 'detail-404-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-404-dark-zh-Hant');

  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  mode = 'offline';
  await page.getByTestId('sd-order-search').click();
  await expect(page.getByTestId('sd-order-error')).toBeVisible();
  await expect(page.getByTestId('sd-order-table')).not.toContainText('SO-0081');
  await capture(page, 'offline-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'offline-light-zh-Hant');
  mode = 'saved';
  await page.getByRole('button', { name: /重試/ }).click();
  await expect(page.getByTestId('sd-order-table')).toContainText('SO-0081');
  expect(listReads).toBe(4);
});

test('SD-ORDER-UI-007 reports a stalled read and restores saved rows on retry', async ({ page }) => {
  let reads = 0;
  await page.route('**/api/v1/sal/orders**', async route => {
    reads++;
    if (reads === 1) return new Promise<void>(() => {});
    return route.fulfill({ json: { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/orders');
  await expect(page.getByTestId('sd-order-error')).toContainText('逾時', { timeout: 12000 });
  await expect(page.getByTestId('sd-order-table')).not.toContainText('SO-0081');
  await capture(page, 'timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'timeout-dark-zh-Hant');
  await page.getByRole('button', { name: /重試/ }).click();
  await expect(page.getByTestId('sd-order-row-81')).toContainText('SO-0081');
  expect(reads).toBe(2);
});

test('SD-ORDER-UI-008 reports a stalled detail and retries the same saved order', async ({ page }) => {
  let listReads = 0; let detailReads = 0;
  await page.route('**/api/v1/sal/orders**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/81')) {
      detailReads++;
      if (detailReads === 1) return new Promise<void>(() => {});
      return route.fulfill({ json: detail });
    }
    listReads++;
    return route.fulfill({ json: { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-row-81').getByRole('button').click();
  await expect(page.getByTestId('sd-order-detail-error')).toContainText('逾時', { timeout: 12000 });
  await expect(page.getByTestId('sd-order-detail')).toHaveCount(0);
  await capture(page, 'detail-timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-timeout-dark-zh-Hant');
  await page.getByTestId('sd-order-detail-retry').click();
  await expect(page.getByTestId('sd-order-detail')).toContainText('Saved remark');
  expect(listReads).toBe(1);
  expect(detailReads).toBe(2);
});

test.describe('SD-ORDER-UI-004 read permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false, permissions: [] } });
  test('hides order route and menu without VIEW', async ({ page }) => {
    let reads = 0;
    await page.route('**/api/v1/sal/orders**', route => { reads++; return route.abort(); });
    await page.goto('/sal/orders');
    await expect(page).toHaveURL(/\/forbidden/);
    await expect(page.getByTestId('layout-menu-sal-orders')).toHaveCount(0);
    expect(reads).toBe(0);
    await capture(page, 'no-permission-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-permission-dark-zh-Hant');
  });
});
