import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const PATH = '/ap/other-payments';
const payment = { id: 7, documentNo: 'OP2026110007', payeeDescription: 'Utility bill', businessDate: '2026-11-27',
  currency: 'TWD', amount: 'AMOUNT', expenseCategoryId: 3, expenseCategoryCode: 'UTIL', expenseCategoryName: 'Utilities',
  bankAccountId: 9, bankAccountCode: 'BANK', bankAccountName: 'Cash', bankReference: 'WD-7', status: 'Draft', version: 1,
  journalId: null };
const rowJson = JSON.stringify(payment).replace('"AMOUNT"', '999999999999999.9999');
const map = { expenseCategoryId: 3, expenseCategoryCode: 'UTIL', expenseCategoryName: 'Utilities',
  expenseCategoryActive: true, configured: false, version: 0, expenseAccount: null };
async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/FI/screenshots';
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: `${directory}/6.10-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('FI-OP-UI-101 list, mapping reason and exact saved amount in three languages and both themes', async ({ page }) => {
  const reads: string[] = [];
  await page.route('**/api/v1/ap/other-payments**', route => {
    reads.push(route.request().method());
    return route.fulfill({ contentType: 'application/json', body: `{"items":[${rowJson}],"totalCount":1,"page":1,"pageSize":20}` });
  });
  await page.route('**/api/v1/gl/other-payment-account-map', route => route.fulfill({ json: { items: [map] } }));
  await page.goto(PATH);
  await expect(page.getByTestId('fi-op-row-7')).toBeVisible();
  await expect(page.getByTestId('fi-op-row-7')).toContainText('999,999,999,999,999.9999 TWD');
  await expect(page.getByTestId('fi-op-map-7')).toContainText('缺少費用科目對照');
  await expect(page.locator('erp-other-payment-page .hint')).toContainText('不核銷採購發票');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-op-map-7')).toContainText('Missing expense account');
  await capture(page, 'normal-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-op-map-7')).toContainText('缺少费用科目对照');
  await capture(page, 'normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hans');
  expect(reads.every(method => method === 'GET')).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
});

test('FI-OP-UI-102 empty, search-empty, error and retry keep read feedback honest', async ({ page, pageErrors }) => {
  let fail = false;
  let reads = 0;
  pageErrors.allowApi5xx(/\/api\/v1\/ap\/other-payments/, 500);
  pageErrors.allow(/^console: Failed to load resource: the server responded with a status of 500/);
  await page.route('**/api/v1/ap/other-payments**', route => {
    reads++;
    if (fail) return route.fulfill({ status: 500, json: { status: 500, traceId: 'op-trace-1' } });
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
  });
  await page.route('**/api/v1/gl/other-payment-account-map', route => route.fulfill({ json: { items: [] } }));
  await page.goto(PATH);
  await expect(page.getByTestId('fi-op-table')).toContainText('尚無其他付款');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await page.getByTestId('fi-op-keyword').fill('none'); await page.getByTestId('fi-op-search').click();
  await expect(page.getByTestId('fi-op-table')).toContainText('沒有符合條件');
  await capture(page, 'search-empty-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'search-empty-light-zh-Hant');
  await page.getByTestId('fi-op-from').fill('2026-11-30'); await page.getByTestId('fi-op-to').fill('2026-11-01');
  const before = reads;
  await page.getByTestId('fi-op-search').click();
  await expect(page.getByTestId('fi-op-range-error')).toBeVisible();
  expect(reads).toBe(before);
  await capture(page, 'range-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'range-error-dark-zh-Hant');
  await page.getByTestId('fi-op-reset').click();
  fail = true;
  await page.getByTestId('fi-op-search').click();
  await expect(page.getByTestId('fi-op-error')).toContainText('op-trace-1');
  await capture(page, 'server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'server-error-light-zh-Hant');
  fail = false;
  await page.getByTestId('fi-op-retry').click();
  await expect(page.getByTestId('fi-op-table')).toContainText('尚無其他付款');
  await expect(page.getByTestId('fi-op-error')).toHaveCount(0);
});

test('FI-OP-UI-103 filters, page, size and sort live in the URL and survive reload, back and invalid links', async ({ page }) => {
  const requests: URLSearchParams[] = [];
  await page.route('**/api/v1/ap/other-payments**', route => {
    requests.push(new URL(route.request().url()).searchParams);
    return route.fulfill({ contentType: 'application/json', body: `{"items":[${rowJson}],"totalCount":120,"page":2,"pageSize":50}` });
  });
  await page.route('**/api/v1/gl/other-payment-account-map', route => route.fulfill({ json: { items: [map] } }));
  const keys = ['keyword', 'status', 'from', 'to', 'page', 'pageSize', 'sort'] as const;
  const last = () => Object.fromEntries(keys.map(key => [key, requests.at(-1)!.get(key)]));
  const inUrl = () => Object.fromEntries(keys.map(key => [key, new URL(page.url()).searchParams.get(key)]));

  await page.goto(`${PATH}?status=Posted&from=2026-11-01&to=2026-11-30&page=2&pageSize=50&sort=-amount`);
  await expect(page.getByTestId('fi-op-row-7')).toBeVisible();
  expect(last()).toEqual({ keyword: null, status: 'Posted', from: '2026-11-01', to: '2026-11-30', page: '2', pageSize: '50', sort: '-amount' });
  await expect(page.getByTestId('fi-op-status-filter')).toHaveValue('Posted');
  await expect(page.getByTestId('fi-op-from')).toHaveValue('2026-11-01');
  await expect(page.locator('nz-table .ant-pagination-item-active')).toHaveText('2');
  // The restored page number must be readable without scrolling the wide table sideways.
  await expect(page.locator('nz-table .ant-pagination-item-active')).toBeInViewport();

  await page.getByTestId('fi-op-keyword').fill('  WD-7 ');
  await page.getByTestId('fi-op-search').click();
  await expect(page).toHaveURL(/keyword=WD-7/);
  expect(inUrl()).toEqual({ keyword: 'WD-7', status: 'Posted', from: '2026-11-01', to: '2026-11-30', page: null, pageSize: '50', sort: '-amount' });
  expect(last()).toEqual({ ...inUrl(), page: '1' });

  await page.locator('nz-table .ant-pagination-item').filter({ hasText: /^3$/ }).click();
  await expect(page).toHaveURL(/page=3/);
  expect(last()).toEqual({ ...inUrl() });

  await page.reload();
  await expect(page.getByTestId('fi-op-row-7')).toBeVisible();
  await expect(page.getByTestId('fi-op-keyword')).toHaveValue('WD-7');
  expect(last()).toEqual({ ...inUrl() });
  await capture(page, 'url-restored-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'url-restored-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-op-keyword')).toHaveValue('WD-7');
  await capture(page, 'url-restored-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'url-restored-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'url-restored-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'url-restored-dark-zh-Hans');

  // Returning from another page restores the same query.
  await page.goto('/');
  await page.goBack();
  await expect(page.getByTestId('fi-op-row-7')).toBeVisible();
  await expect(page.getByTestId('fi-op-keyword')).toHaveValue('WD-7');
  expect(last()).toEqual({ ...inUrl() });

  // Hand-edited links: unknown values fall back silently and the URL is normalised.
  await page.goto(`${PATH}?status=Deleted&from=2026-02-30&to=2026-11-01&page=x&pageSize=33&sort=payee`);
  await expect(page.getByTestId('fi-op-row-7')).toBeVisible();
  expect(last()).toEqual({ keyword: null, status: null, from: null, to: '2026-11-01', page: '1', pageSize: '20', sort: '-businessDate' });
  await expect(page).toHaveURL(/\/ap\/other-payments\?to=2026-11-01$/);
  await expect(page.getByTestId('fi-op-range-error')).toHaveCount(0);
  await expect(page.getByTestId('fi-op-error')).toHaveCount(0);
});
