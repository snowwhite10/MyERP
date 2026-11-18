import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const summary = { id: 81, documentNo: 'SO-0081', customerId: 7, customerName: 'Saved customer',
  businessDate: '2026-11-18', status: 'Approved', originalGross: 12.34, version: 2 };
const detail = { ...summary, customerCode: 'C-7', currencyCode: 'USD', baseCurrencyCode: 'TWD',
  exchangeRate: 32, sourceQuotationId: 31, originalNet: 11, originalTax: 1.34,
  baseNet: 352, baseTax: 42.88, baseGross: 394.88, remark: 'Saved remark',
  lines: [{ id: 91, lineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Saved item',
    quantity: 2, unitPrice: 5.5, discountRate: 0, taxRatePercent: 12,
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
  await capture(page, 'detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hant');
  expect(requests).toEqual(['/api/v1/sal/orders', '/api/v1/sal/orders/81']);
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
