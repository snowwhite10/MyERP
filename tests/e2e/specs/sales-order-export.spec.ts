import { mkdir, readFile } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

const summary = { id: 81, documentNo: 'SO-0081', customerId: 7, customerName: 'Saved customer',
  businessDate: '2026-11-18', status: 'Approved', originalGross: 12.34, version: 2 };
const HEADERS: Record<string, string> = {
  'zh-Hant': '訂單號碼,訂單日期,狀態,客戶代碼', en: 'Order number,Order date,Status,Customer code',
  'zh-Hans': '订单号码,订单日期,状态,客户代码' };
const STATUS: Record<string, string> = { 'zh-Hant': '已核准', en: 'Approved', 'zh-Hans': '已核准' };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.4-export-${name}.png`, fullPage: true, animations: 'disabled' });
}
function csv(language: string, lines: number): string {
  const rows = Array.from({ length: lines }, (_, i) => `SO-0081,2026-11-18,${STATUS[language]},"C-7, ""Main""\r\nHQ ${i}"`);
  return '﻿' + [HEADERS[language], ...rows].join('\r\n') + '\r\n';
}
type ExportReply = (route: Route, language: string) => Promise<void> | void;
async function mockOrders(page: Page, reply: ExportReply, list = [summary]): Promise<URL[]> {
  const exports: URL[] = [];
  await page.route('**/api/v1/sal/orders**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/export')) {
      exports.push(url);
      return reply(route, (route.request().headers()['accept-language'] ?? '').split(',')[0]);
    }
    return route.fulfill({ json: { items: list, totalCount: list.length, page: 1, pageSize: 20 } });
  });
  return exports;
}
const csvReply = (lines: number, count = String(lines)): ExportReply => (route, language) =>
  route.fulfill({ status: 200, headers: { 'Content-Type': 'text/csv; charset=utf-8', 'X-Export-Row-Count': count,
    'Content-Disposition': 'attachment; filename=sales-orders.csv' }, body: csv(language, lines) });

test('SD-ORDER-EXPORT-UI-001/002 downloads applied filters in the current language and matches the shown count', async ({ page }) => {
  const exports = await mockOrders(page, csvReply(2));
  await page.goto('/sal/orders?status=Approved&keyword=SO&sort=documentNo');
  await expect(page.getByTestId('sd-order-row-81')).toBeVisible();
  await expect(page.getByTestId('sd-order-export-hint')).toContainText('已套用的篩選');
  await capture(page, 'ready-light-zh-Hant');
  for (const language of ['zh-Hant', 'en', 'zh-Hans'] as const) {
    if (language !== 'zh-Hant') await chooseLanguage(page, language);
    const download = page.waitForEvent('download');
    await page.getByTestId('sd-order-export').click();
    const file = await download;
    expect(file.suggestedFilename()).toBe('sales-orders.csv');
    const text = (await readFile((await file.path())!)).toString('utf8');
    expect(text.startsWith('﻿' + HEADERS[language] + '\r\n')).toBe(true);
    expect(text).toContain(STATUS[language]);
    // 每列含引號內換行，原始分割為 4 段；頁面依 RFC 4180 計為 2 列並與 X-Export-Row-Count 核對。
    expect(text.split('\r\n').length - 2).toBe(4);
    await expect(page.getByTestId('sd-order-export-notice')).toContainText('2');
    await expect(page.getByTestId('sd-order-export-notice').locator('.ant-alert-success')).toHaveCount(1);
    await capture(page, `success-light-${language}`);
    await chooseTheme(page, 'dark'); await capture(page, `success-dark-${language}`); await chooseTheme(page, 'light');
  }
  await expect(page.getByTestId('sd-order-export-notice')).toContainText('已导出 2 行');
  await expect(page.locator('[aria-live="polite"]').filter({ has: page.getByTestId('sd-order-export-notice') })).toHaveCount(1);
  expect(exports).toHaveLength(3);
  for (const url of exports) {
    expect(url.searchParams.get('status')).toBe('Approved');
    expect(url.searchParams.get('keyword')).toBe('SO');
    expect(url.searchParams.get('sort')).toBe('documentNo');
    expect(url.searchParams.has('page')).toBe(false);
  }
});

test('SD-ORDER-EXPORT-UI-004 warns on zero exported lines and on a mismatched file without downloading', async ({ page }) => {
  let mode: 'empty' | 'mismatch' = 'empty';
  let downloads = 0;
  page.on('download', () => ++downloads);
  await mockOrders(page, (route, language) => mode === 'empty' ? csvReply(0)(route, language) : csvReply(1, '3')(route, language));
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-export').click();
  const notice = page.getByTestId('sd-order-export-notice');
  await expect(notice).toContainText('未下載檔案');
  await expect(notice.locator('.ant-alert-warning')).toHaveCount(1);
  await expect(notice.locator('.ant-alert-success')).toHaveCount(0);
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await chooseLanguage(page, 'en'); await expect(notice).toContainText('No file was downloaded');
  await capture(page, 'empty-dark-en');
  mode = 'mismatch';
  await page.getByTestId('sd-order-export').click();
  await expect(page.getByTestId('sd-order-export-error')).toContainText('row count did not match');
  await expect(notice).toHaveCount(0);
  await capture(page, 'mismatch-dark-en');
  expect(downloads).toBe(0);
});

test('SD-ORDER-EXPORT-UI-006 explains why export is disabled', async ({ page }) => {
  await mockOrders(page, csvReply(1), []);
  await page.goto('/sal/orders');
  await expect(page.getByTestId('sd-order-export')).toBeDisabled();
  // 未篩選的空列表與「尚未有訂單」一致；套用篩選後才說「沒有符合篩選」。
  await expect(page.getByTestId('sd-order-export-hint')).toContainText('尚未有訂單，無可匯出內容');
  await capture(page, 'disabled-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'disabled-dark-zh-Hant');
  await page.getByTestId('sd-order-status').selectOption('Draft');
  await page.getByTestId('sd-order-search').click();
  await expect(page.getByTestId('sd-order-export')).toBeDisabled();
  await expect(page.getByTestId('sd-order-export-hint')).toContainText('沒有符合目前篩選的訂單');
  await capture(page, 'disabled-filtered-dark-zh-Hant');
});

test('SD-ORDER-EXPORT-UI-006 blocks a second export while one is running', async ({ page }) => {
  let release!: () => void;
  const exports = await mockOrders(page, async (route, language) => {
    await new Promise<void>(resolve => release = resolve);
    await csvReply(1)(route, language);
  });
  await page.goto('/sal/orders');
  await page.getByTestId('sd-order-export').click();
  await expect(page.getByTestId('sd-order-export')).toBeDisabled();
  await expect(page.getByTestId('sd-order-export-hint')).toContainText('正在匯出');
  await page.getByTestId('sd-order-export').click({ force: true });
  const download = page.waitForEvent('download');
  release(); await download;
  await expect(page.getByTestId('sd-order-export-notice')).toContainText('已匯出 1 行');
  expect(exports).toHaveLength(1);
});

test('SD-ORDER-EXPORT-UI-007 shows translated 400, 403, 422 and 500 failures', async ({ page, pageErrors }) => {
  let status = 422;
  await mockOrders(page, route => {
    const body = status === 422 ? { errorCode: 'SAL.ORDER.EXPORT_LIMIT' } : status === 403 ? { errorCode: 'COMMON.PERMISSION.DENIED' } :
      status === 400 ? { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { to: ['To must be on or after From.'] } } :
        { traceId: 'trace-export-500' };
    return route.fulfill({ status, contentType: 'application/problem+json', body: JSON.stringify(body) });
  });
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders\/export/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (400|403|422|500)/);
  await page.goto('/sal/orders');
  const error = page.getByTestId('sd-order-export-error');
  const cases: [number, Record<string, string>][] = [
    [422, { 'zh-Hant': '超過 10,000 行', en: 'exceeds 10,000 order lines', 'zh-Hans': '超过 10,000 行' }],
    [403, { 'zh-Hant': '沒有匯出銷售訂單的權限', en: 'permission to export', 'zh-Hans': '没有导出销售订单的权限' }],
    [400, { 'zh-Hant': '匯出條件無效', en: 'filters are invalid', 'zh-Hans': '导出条件无效' }],
    [500, { 'zh-Hant': '匯出失敗', en: 'The export failed', 'zh-Hans': '导出失败' }],
  ];
  for (const [code, texts] of cases) {
    status = code;
    for (const language of ['zh-Hant', 'en', 'zh-Hans'] as const) {
      await chooseLanguage(page, language);
      await page.getByTestId('sd-order-export').click();
      await expect(error).toContainText(texts[language]);
      await expect(page.getByTestId('sd-order-export')).toBeEnabled();
    }
    if (code === 500) await expect(error).toContainText('trace-export-500');
    else await expect(error).not.toContainText('Trace ID');
    if (code === 400) await expect(error).toContainText('to: To must be on or after From.');
    await capture(page, `${code}-light-zh-Hans`);
    await chooseTheme(page, 'dark'); await capture(page, `${code}-dark-zh-Hans`); await chooseTheme(page, 'light');
  }
});

test('SD-ORDER-EXPORT-UI-007 reports a stalled export as timed out and allows retry', async ({ page }) => {
  test.setTimeout(45000);
  let hang = true;
  await mockOrders(page, (route, language) => hang ? new Promise<void>(() => {}) : csvReply(1)(route, language));
  await page.goto('/sal/orders');
  const error = page.getByTestId('sd-order-export-error');
  await page.getByTestId('sd-order-export').click();
  await expect(error).toContainText('匯出逾時', { timeout: 20000 });
  await expect(page.getByTestId('sd-order-export')).toBeEnabled();
  await capture(page, 'timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'timeout-dark-zh-Hant');
  hang = false;
  const download = page.waitForEvent('download');
  await page.getByTestId('sd-order-export').click();
  await download;
  await expect(error).toHaveCount(0);
  await expect(page.getByTestId('sd-order-export-notice')).toContainText('已匯出 1 行');
});

test.describe('SD-ORDER-EXPORT-UI-005 view without export permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: ['SAL.ORDER.VIEW'].map(code => ({ code, dataScope: 'Company' })) } });
  test('hides the export action and sends no export request', async ({ page }) => {
    const exports = await mockOrders(page, csvReply(1));
    await page.goto('/sal/orders');
    await expect(page.getByTestId('sd-order-row-81')).toBeVisible();
    await expect(page.getByTestId('sd-order-export')).toHaveCount(0);
    await expect(page.getByTestId('sd-order-export-hint')).toHaveCount(0);
    await capture(page, 'view-only-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'view-only-dark-zh-Hant');
    expect(exports).toHaveLength(0);
  });
});
