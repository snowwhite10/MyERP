import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

// Price Rule View Interface 2.11 (WU-SD-005b1a, ADR-161): labelled list, filters in the URL, versioned deactivation.
const base = { currencyCode: 'TWD', createdAt: '2026-11-01T00:00:00', updatedAt: null };
const defaultRule = { ...base, id: 31, itemId: 9, customerId: null, validFrom: '2026-01-01', validTo: '2026-12-31',
  unitPrice: 1250.125, discountCeiling: 10, isActive: true, version: 2, itemCode: 'ITEM-9', itemName: 'Stainless bolt M8',
  customerCode: null, customerName: null };
const customerRule = { ...base, id: 32, itemId: 9, customerId: 7, validFrom: '2026-06-01', validTo: '2026-06-30',
  unitPrice: 1180, discountCeiling: 12.5, isActive: true, version: 1, itemCode: 'ITEM-9', itemName: 'Stainless bolt M8',
  customerCode: 'C-7', customerName: 'Harbour Trading Co.' };
const retired = { ...base, id: 30, itemId: 10, customerId: null, validFrom: '2025-01-01', validTo: '2025-12-31',
  unitPrice: 0.000001, discountCeiling: 0, isActive: false, version: 3, itemCode: null, itemName: null,
  customerCode: null, customerName: null };

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.11-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-PRULE-UI-101 lists labelled rules in three languages and two themes', async ({ page }) => {
  await page.route('**/api/v1/sal/price-rules**', route => route.fulfill({
    json: { items: [customerRule, defaultRule, retired], totalCount: 3, page: 1, pageSize: 20 } }));
  await page.goto('/sal/price-rules');
  await expect(page.getByTestId('sd-price-rule-row-32')).toContainText('C-7 · Harbour Trading Co.');
  await expect(page.getByTestId('sd-price-rule-row-31')).toContainText('公司預設');
  await expect(page.getByTestId('sd-price-rule-row-31')).toContainText('1,250.125');
  await expect(page.getByTestId('sd-price-rule-row-30')).toContainText('已刪除物料（ID 10）');
  await expect(page.getByTestId('sd-price-rule-row-30')).toContainText('0.000001');
  // UX review 2.11: dates and amounts stay on one line (no "2026-" / "06-01" split).
  const dateCell = page.getByTestId('sd-price-rule-row-31').locator('td.date').first();
  await expect(dateCell).toHaveText('2026-01-01');
  expect(await dateCell.evaluate(cell => {
    const range = document.createRange(); range.selectNodeContents(cell); return range.getClientRects().length;
  })).toBe(1);
  await expect(page.getByTestId('sd-price-rule-deactivate-30')).toHaveCount(0);
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-price-rule-row-31')).toContainText('Company default');
  await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-price-rule-row-30')).toContainText('已停用');
  await capture(page, 'normal-light-zh-Hans');
  await page.setViewportSize({ width: 768, height: 900 });
  await expect.poll(() => page.locator('nz-sider').evaluate(element => element.getBoundingClientRect().width))
    .toBeLessThanOrEqual(65);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  const scroll = await page.locator('.scroll').evaluate(element => ({ width: element.clientWidth, content: element.scrollWidth }));
  expect(scroll.content).toBeGreaterThan(scroll.width); // the table scrolls inside its own box
  await capture(page, 'tablet-light-zh-Hans');
});

test('SD-PRULE-UI-102 filters go to the server and the URL; invalid input sends nothing; empty states differ', async ({ page }) => {
  const queries: URL[] = [];
  await page.route('**/api/v1/sal/price-rules**', route => {
    const url = new URL(route.request().url()); queries.push(url);
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/price-rules');
  await expect(page.getByTestId('sd-price-rule-table')).toContainText('尚未有價格規則');
  await capture(page, 'empty-light-zh-Hant');
  await page.getByTestId('sd-price-rule-currency').fill('us');
  await page.getByTestId('sd-price-rule-search').click();
  await expect(page.getByTestId('sd-price-rule-validation')).toContainText('三個字母');
  await page.getByTestId('sd-price-rule-scope').selectOption('customer');
  await page.getByTestId('sd-price-rule-currency').fill('usd');
  const reads = queries.length;
  await page.getByTestId('sd-price-rule-search').click();
  await expect(page.getByTestId('sd-price-rule-validation')).toContainText('請選擇客戶');
  expect(queries.length).toBe(reads);
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
  await page.getByTestId('sd-price-rule-scope').selectOption('default');
  await page.getByTestId('sd-price-rule-status').selectOption('active');
  await page.getByTestId('sd-price-rule-as-of').fill('2026-06-15');
  await page.getByTestId('sd-price-rule-search').click();
  await expect(page.getByTestId('sd-price-rule-table')).toContainText('沒有符合篩選條件的價格規則');
  await expect(page).toHaveURL(/currency=USD/);
  await expect(page).toHaveURL(/defaultOnly=true/);
  await expect(page).toHaveURL(/active=true/);
  const last = queries.at(-1)!;
  expect([last.searchParams.get('currencyCode'), last.searchParams.get('defaultOnly'), last.searchParams.get('active'),
    last.searchParams.get('asOf')]).toEqual(['USD', 'true', 'true', '2026-06-15']);
  await capture(page, 'no-results-dark-zh-Hant');
});

test('SD-PRULE-UI-103 deactivates with the shown version after a danger confirmation and explains a stale version', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 409/);
  let state: 'active' | 'retired' = 'active';
  let staleNext = false;
  const bodies: string[] = [];
  await page.route('**/api/v1/sal/price-rules**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/deactivate')) {
      bodies.push(route.request().postData() ?? '');
      if (staleNext) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
      state = 'retired';
      return route.fulfill({ json: { ...defaultRule, isActive: false, version: 3 } });
    }
    const rule = state === 'retired' ? { ...defaultRule, isActive: false, version: 3 } : defaultRule;
    return route.fulfill({ json: { items: [customerRule, rule], totalCount: 2, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/price-rules');
  await page.getByTestId('sd-price-rule-deactivate-31').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('ITEM-9 · Stainless bolt M8');
  await expect(dialog).toContainText('不能重新啟用');
  await capture(page, 'deactivate-confirm-light-zh-Hant');
  await dialog.getByRole('button', { name: '停用' }).click();
  await expect(page.getByTestId('sd-price-rule-notice')).toContainText('價格規則 31 已停用（版本 3）');
  await expect(page.getByTestId('sd-price-rule-deactivate-31')).toHaveCount(0);
  await expect(page.getByTestId('sd-price-rule-row-31')).toContainText('已停用');
  await capture(page, 'deactivated-light-zh-Hant');
  expect(bodies).toEqual(['{"version":2}']);

  staleNext = true;
  await chooseTheme(page, 'dark');
  await page.getByTestId('sd-price-rule-deactivate-32').click();
  await page.getByRole('dialog').getByRole('button', { name: '停用' }).click();
  await expect(page.getByTestId('sd-price-rule-notice')).toContainText('已被其他人修改');
  await capture(page, 'deactivate-conflict-dark-zh-Hant');
  expect(bodies.at(-1)).toBe('{"version":1}');
});

test('SD-PRULE-UI-104 a list server error shows the trace ID and retry', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/price-rules/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  await page.route('**/api/v1/sal/price-rules**', route =>
    route.fulfill({ status: 500, json: { traceId: 'trace-price-500' } }));
  await page.goto('/sal/price-rules');
  await expect(page.getByTestId('sd-price-rule-error')).toContainText('trace-price-500');
  await expect(page.getByTestId('sd-price-rule-retry')).toBeVisible();
  const retry = await page.getByTestId('sd-price-rule-retry').boundingBox();
  expect(retry!.width).toBeLessThan(300); // not stretched across the page
  await expect(page.getByTestId('sd-price-rule-table')).toContainText('無法載入價格規則');
  await capture(page, 'error-light-zh-Hant');
});

test.describe('pricing viewer without MD permission (WU-SD-005b1b1, ADR-162)', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'SAL.PRICING.VIEW', dataScope: 'Company' }] } });

  test('SD-PRULE-UI-105 picks item and customer filters from the pricing option lists, never MD', async ({ page }) => {
    const lists: URL[] = []; const options: URL[] = []; const masterData: string[] = [];
    await page.route('**/api/v1/md/**', route => { masterData.push(route.request().url());
      return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } }); });
    await page.route('**/api/v1/sal/price-rules**', route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/item-options')) { options.push(url);
        return route.fulfill({ json: { items: [{ id: 9, code: 'ITEM-9', name: 'Stainless bolt M8' }], totalCount: 1,
          page: 1, pageSize: 10 } }); }
      if (url.pathname.endsWith('/customer-options')) { options.push(url);
        return route.fulfill({ json: { items: [{ id: 7, code: 'C-7', name: 'Harbour Trading Co.' }], totalCount: 1,
          page: 1, pageSize: 10 } }); }
      lists.push(url);
      return route.fulfill({ json: { items: [customerRule], totalCount: 1, page: 1, pageSize: 20 } });
    });
    await page.goto('/sal/price-rules');
    await expect(page.getByTestId('sd-price-rule-row-32')).toBeVisible();
    await page.getByTestId('sd-price-rule-item-pick').click();
    await page.getByTestId('erp-select-keyword').fill('bolt');
    await page.getByTestId('erp-select-search').click();
    await expect(page.getByTestId('erp-select-row-9')).toContainText('Stainless bolt M8');
    await expect(page.getByTestId('erp-select-description')).toContainText('只列出啟用中的物料');
    await capture(page, 'item-picker-light-zh-Hant');
    await page.getByTestId('erp-select-row-9').click(); await page.getByTestId('erp-select-confirm').click();
    await expect(page.getByTestId('sd-price-rule-item')).toHaveText('ITEM-9 · Stainless bolt M8');
    await page.getByTestId('sd-price-rule-scope').selectOption('customer');
    await page.getByTestId('sd-price-rule-customer-pick').click();
    await page.getByTestId('erp-select-row-7').click(); await page.getByTestId('erp-select-confirm').click();
    await expect(page.getByTestId('sd-price-rule-customer')).toHaveText('C-7 · Harbour Trading Co.');
    await page.getByTestId('sd-price-rule-search').click();
    await expect(page).toHaveURL(/itemId=9/);
    await expect(page).toHaveURL(/customerId=7/);
    const last = lists.at(-1)!;
    expect([last.searchParams.get('itemId'), last.searchParams.get('customerId')]).toEqual(['9', '7']);
    expect(options.some(url => url.searchParams.get('keyword') === 'bolt')).toBe(true);
    expect(masterData).toEqual([]);
    await chooseTheme(page, 'dark'); await capture(page, 'filtered-by-options-dark-zh-Hant');
  });
});
