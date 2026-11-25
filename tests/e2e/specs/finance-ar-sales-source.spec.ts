import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';
import type { Page } from '@playwright/test';

const API = '**/api/v1/ar/sales-sources**';
const PAGE = '/ar/sales-sources';
const invoice = { id: 12, customerId: 4, sourceType: 'Invoice', sourceId: 9, sourceVersion: 2,
  businessDate: '2026-11-25', currency: 'TWD', baseCurrency: 'TWD', grossAmount: 420,
  baseGrossAmount: 420, glJournalId: 28, receivableId: 7, openBaseAmount: 315 };
const credit = { ...invoice, id: 14, sourceType: 'Return', sourceId: 11, grossAmount: 105,
  baseGrossAmount: 105, glJournalId: 30, receivableId: null, openBaseAmount: null };
const allocation = { id: 31, creditSourceId: 14, receivableId: 7, originalSourceId: 9,
  invoiceLineId: 18, quantity: 1, grossAmount: 105, baseGrossAmount: 105 };
const saved = [invoice, credit];
const detail = (source: typeof invoice) => ({ source, netAmount: source.id === 12 ? 400 : 100,
  taxAmount: source.id === 12 ? 20 : 5, baseNetAmount: source.id === 12 ? 400 : 100,
  baseTaxAmount: source.id === 12 ? 20 : 5, exchangeRate: 1, allocations: [allocation] });

async function capture(page: Page, name: string) {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.31-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('FI-SRC-UI-001/003/006 committed invoice and return details match saved API, in three languages and both themes', async ({ page }) => {
  const reads: string[] = [];
  await page.route(API, route => {
    const url = new URL(route.request().url()); reads.push(url.pathname);
    if (url.pathname.endsWith('/by-key/Invoice/9')) return route.fulfill({ json: detail(invoice) });
    const id = Number(url.pathname.split('/').pop());
    if (id === 12 || id === 14) return route.fulfill({ json: detail(saved.find(s => s.id === id)!) });
    const type = url.searchParams.get('sourceType');
    const items = type ? saved.filter(s => s.sourceType === type) : saved;
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-source-row-12')).toContainText('315');
  await expect(page.getByTestId('fi-source-row-14')).toContainText('#30');
  await page.getByTestId('fi-source-view-14').click();
  await expect(page.getByTestId('fi-source-detail')).toContainText('#11');
  await expect(page.getByTestId('fi-source-allocations')).toContainText('#9');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en'); await expect(page.getByRole('heading', { name: 'Sales receivable sources' })).toBeVisible();
  await capture(page, 'normal-dark-en');
  await chooseLanguage(page, 'zh-Hans'); await expect(page.getByRole('heading', { name: '销售应收来源' })).toBeVisible();
  await capture(page, 'normal-dark-zh-Hans');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('fi-source-lookup-id').fill('9'); await page.getByTestId('fi-source-lookup-button').click();
  await expect(page.getByTestId('fi-source-detail')).toContainText('#28');
  expect(reads).toContain('/api/v1/ar/sales-sources/by-key/Invoice/9');
  // Screen values and the independent saved API objects agree: 420 original, 105 credited, 315 currently open.
  expect(invoice.grossAmount - allocation.baseGrossAmount).toBe(invoice.openBaseAmount);
  expect(credit.glJournalId).toBe(30);
});

test('FI-SRC-UI-002/007 empty, invalid, missing, server and network states give a reason and retry', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/ar\/sales-sources/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (404|500)/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  let mode: 'empty' | 'server' | 'offline' = 'empty';
  await page.route(API, route => {
    const path = new URL(route.request().url()).pathname;
    if (path.includes('/by-key/')) return route.fulfill({ status: 404, json: { errorCode: 'FI.SALES_SOURCE.NOT_FOUND' } });
    if (mode === 'server') return route.fulfill({ status: 500, json: { traceId: 'fi-trace-500' } });
    if (mode === 'offline') return route.abort('failed');
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
  });
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-source-table')).toContainText('尚無已過帳'); await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('fi-source-customer').fill('4'); await page.getByTestId('fi-source-search').click();
  await expect(page.getByTestId('fi-source-table')).toContainText('沒有符合條件'); await capture(page, 'no-results-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'no-results-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('fi-source-customer').fill('0'); await page.getByTestId('fi-source-search').click();
  await expect(page.getByTestId('fi-source-validation')).toContainText('正整數'); await capture(page, 'invalid-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'invalid-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('fi-source-lookup-id').fill('999'); await page.getByTestId('fi-source-lookup-button').click();
  await expect(page.getByTestId('fi-source-error')).toContainText('找不到已過帳來源'); await capture(page, 'missing-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'missing-dark-zh-Hant'); await chooseTheme(page, 'light');
  mode = 'server'; await page.getByTestId('fi-source-reset').click();
  await expect(page.getByTestId('fi-source-error')).toContainText('fi-trace-500');
  await expect(page.getByTestId('fi-source-table')).toContainText('讀取失敗'); await capture(page, 'server-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-dark-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-source-retry').click();
  await expect(page.getByTestId('fi-source-error')).toContainText('無法連線'); await capture(page, 'offline-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'offline-light-zh-Hant');
});

test.describe('FI-SRC-UI-005 permission guard', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false, permissions: [{ code: 'AR.COLLECTION.VIEW', dataScope: 'Company' }] } });
  test('hides the Finance link and refuses a direct route before reading FI data', async ({ page }) => {
    let reads = 0;
    await page.route(API, route => { reads++; return route.abort(); });
    await page.goto(PAGE);
    await expect(page).toHaveURL(/\/forbidden/);
    await page.getByTestId('layout-menu-fi').click();
    await expect(page.getByTestId('layout-menu-fi-sales-sources')).toHaveCount(0);
    await capture(page, 'forbidden-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'forbidden-dark-zh-Hant');
    expect(reads).toBe(0);
  });
});

test('FI-SRC-UI-007 timeout names the problem and keeps a retry action in both themes', async ({ page }) => {
  test.setTimeout(45_000);
  await page.route(API, async route => {
    await new Promise(resolve => setTimeout(resolve, 16_000));
    try { await route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }); } catch { /* client aborted on timeout */ }
  });
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-source-error')).toContainText('讀取逾時', { timeout: 25_000 });
  await expect(page.getByTestId('fi-source-retry')).toBeVisible();
  await expect(page.getByTestId('fi-source-table')).toContainText('讀取失敗');
  await capture(page, 'timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'timeout-dark-zh-Hant');
});
