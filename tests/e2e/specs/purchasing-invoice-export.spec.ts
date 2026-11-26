import { mkdir, readFile } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

/** 介面 4.19 匯出 CSV（WU-PUR-008c3c2）：模擬後端；表頭／狀態與 PurchaseInvoiceCsv 三語一致。 */
const row = (id: number) => ({ id, supplierId: 5, purchaseOrderId: 70, supplierInvoiceNumber: `INV-2026-${id}`, businessDate: '2026-11-20',
  currency: 'TWD', status: 'Matched', totalNet: 1000, totalTax: 50, totalGross: 1050, lineCount: 2, version: 3 });
const HEADERS: Record<string, string> = {
  'zh-Hant': '供應商發票號,發票日期,狀態,供應商 ID,採購單 ID,幣別,行號,收貨行 ID,數量,單價,稅率(%),未稅,稅額,含稅',
  'zh-Hans': '供应商发票号,发票日期,状态,供应商 ID,采购单 ID,币别,行号,收货行 ID,数量,单价,税率(%),未税,税额,含税',
  en: 'Supplier invoice no.,Invoice date,Status,Supplier ID,Purchase order ID,Currency,Line no.,Receipt line ID,Quantity,Unit price,Tax rate (%),Net,Tax,Gross' };
const STATUS: Record<string, string> = { 'zh-Hant': '已比對', 'zh-Hans': '已比对', en: 'Matched' };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: `${directory}/4.19-export-${name}.png`, fullPage: true, animations: 'disabled' });
}
/** One row per saved line; the supplier number holds a quoted comma and line break to prove RFC 4180 counting. */
function csv(language: string, lines: number): string {
  const body = Array.from({ length: lines }, (_, i) =>
    `"INV-2026-1, ""A""\r\nB",2026-11-20,${STATUS[language]},5,70,TWD,${i + 1},900,10.000000,52.5000,5.0000,525.0000,26.2500,551.2500`);
  return '﻿' + [HEADERS[language], ...body].join('\r\n') + '\r\n';
}
type ExportReply = (route: Route, language: string) => Promise<void> | void;
async function mockInvoices(page: Page, reply: ExportReply, list = [row(1)]): Promise<URL[]> {
  const exports: URL[] = [];
  await page.route('**/api/v1/pur/invoices**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/export')) {
      exports.push(url);
      return reply(route, (route.request().headers()['accept-language'] ?? '').split(',')[0]);
    }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ items: list, totalCount: list.length, page: 1, pageSize: 20 }) });
  });
  return exports;
}
const csvReply = (lines: number, count = String(lines)): ExportReply => (route, language) =>
  route.fulfill({ status: 200, headers: { 'Content-Type': 'text/csv; charset=utf-8', 'X-Export-Row-Count': count,
    'Content-Language': language, 'Content-Disposition': 'attachment; filename="purchase-invoices.csv"' }, body: csv(language, lines) });

test('PUR-INV-EXPORT-UI-E01 downloads the applied filters in each language; file content and notice agree', async ({ page }) => {
  const exports = await mockInvoices(page, csvReply(2));
  await page.goto('/pur/purchase-invoices');
  await page.getByTestId('pur-invoice-status-filter').selectOption('Matched');
  await page.getByTestId('pur-invoice-keyword').fill('INV-2026');
  await page.getByTestId('pur-invoice-search').click();
  await expect(page.getByTestId('pur-invoice-row-1')).toBeVisible();
  await expect(page.getByTestId('pur-invoice-export-hint')).toContainText('已套用的篩選');
  await capture(page, 'ready-light-zh-Hant');
  for (const language of ['zh-Hant', 'en', 'zh-Hans'] as const) {
    if (language !== 'zh-Hant') await chooseLanguage(page, language);
    const download = page.waitForEvent('download');
    await page.getByTestId('pur-invoice-export').click();
    const file = await download;
    expect(file.suggestedFilename()).toBe('purchase-invoices.csv');
    const text = (await readFile((await file.path())!)).toString('utf8');
    // 資料：檔案表頭與狀態跟隨目前語言；每列含引號內換行，頁面依 RFC 4180 計為 2 列並與 X-Export-Row-Count 核對。
    expect(text.startsWith('﻿' + HEADERS[language] + '\r\n')).toBe(true);
    expect(text).toContain(`,${STATUS[language]},`);
    expect(text.split('\r\n').length - 2).toBe(4);
    // 畫面：成功樣式與同一行數。
    const notice = page.getByTestId('pur-invoice-export-notice');
    await expect(notice).toContainText('2');
    await expect(notice.locator('.ant-alert-success')).toHaveCount(1);
    await capture(page, `success-light-${language}`);
    await chooseTheme(page, 'dark'); await capture(page, `success-dark-${language}`); await chooseTheme(page, 'light');
  }
  await expect(page.getByTestId('pur-invoice-export-notice')).toContainText('已导出 2 行发票明细');
  expect(exports).toHaveLength(3);
  for (const url of exports) {
    expect(url.searchParams.get('status')).toBe('Matched');
    expect(url.searchParams.get('keyword')).toBe('INV-2026');
    expect(url.searchParams.has('page')).toBe(false);
    expect(url.searchParams.has('pageSize')).toBe(false);
  }
});

test('PUR-INV-EXPORT-UI-E02 zero lines warn and a mismatched file is refused; neither downloads', async ({ page }) => {
  let mode: 'empty' | 'mismatch' = 'empty';
  let downloads = 0;
  page.on('download', () => ++downloads);
  await mockInvoices(page, (route, language) => mode === 'empty' ? csvReply(0)(route, language) : csvReply(1, '3')(route, language));
  await page.goto('/pur/purchase-invoices');
  await page.getByTestId('pur-invoice-export').click();
  const notice = page.getByTestId('pur-invoice-export-notice');
  await expect(notice).toContainText('未下載檔案');
  await expect(notice.locator('.ant-alert-warning')).toHaveCount(1);
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await chooseLanguage(page, 'en'); await expect(notice).toContainText('No file was downloaded');
  mode = 'mismatch';
  await page.getByTestId('pur-invoice-export').click();
  await expect(page.getByTestId('pur-invoice-export-error')).toContainText('row count did not match');
  await expect(notice).toHaveCount(0);
  await capture(page, 'mismatch-dark-en');
  expect(downloads).toBe(0);
});

test('PUR-INV-EXPORT-UI-E03 disabled reasons and one export at a time', async ({ page }) => {
  let list = [] as ReturnType<typeof row>[];
  let release!: () => void;
  const exports: URL[] = [];
  await page.route('**/api/v1/pur/invoices**', async route => {
    const url = new URL(route.request().url());
    if (!url.pathname.endsWith('/export'))
      return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ items: list, totalCount: list.length, page: 1, pageSize: 20 }) });
    exports.push(url);
    await new Promise<void>(resolve => release = resolve);
    await csvReply(1)(route, 'zh-Hant');
  });
  await page.goto('/pur/purchase-invoices');
  await expect(page.getByTestId('pur-invoice-export')).toBeDisabled();
  await expect(page.getByTestId('pur-invoice-export-hint')).toContainText('尚未有採購發票');
  await capture(page, 'disabled-light-zh-Hant');
  await page.getByTestId('pur-invoice-status-filter').selectOption('Posted');
  await page.getByTestId('pur-invoice-search').click();
  await expect(page.getByTestId('pur-invoice-export-hint')).toContainText('沒有符合目前篩選的發票');
  list = [row(1)];
  await page.getByTestId('pur-invoice-reset').click();
  await expect(page.getByTestId('pur-invoice-export')).toBeEnabled();
  await page.getByTestId('pur-invoice-export').click();
  await expect(page.getByTestId('pur-invoice-export')).toBeDisabled();
  await expect(page.getByTestId('pur-invoice-export-hint')).toContainText('正在匯出');
  await page.getByTestId('pur-invoice-export').click({ force: true });
  const download = page.waitForEvent('download');
  release(); await download;
  await expect(page.getByTestId('pur-invoice-export-notice')).toContainText('已匯出 1 行');
  expect(exports).toHaveLength(1);
});

test('PUR-INV-EXPORT-UI-E04 shows translated 422 limit, 403, 400 and 500 reasons in three languages', async ({ page, pageErrors }) => {
  let status = 422;
  await mockInvoices(page, route => {
    const body = status === 422 ? { errorCode: 'PUR.INVOICE.EXPORT_LIMIT' } : status === 403 ? { errorCode: 'COMMON.PERMISSION.DENIED' } :
      status === 400 ? { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED', errors: { supplierId: ['SupplierId must be positive.'] } } :
        { traceId: 'trace-inv-export-500' };
    return route.fulfill({ status, contentType: 'application/problem+json', body: JSON.stringify(body) });
  });
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/invoices\/export/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (400|403|422|500)/);
  await page.goto('/pur/purchase-invoices');
  const error = page.getByTestId('pur-invoice-export-error');
  const cases: [number, Record<string, string>][] = [
    [422, { 'zh-Hant': '超過 10,000 行發票明細', en: 'exceeds 10,000 invoice lines', 'zh-Hans': '超过 10,000 行发票明细' }],
    [403, { 'zh-Hant': '沒有匯出採購發票的權限', en: 'permission to export purchase invoices', 'zh-Hans': '没有导出采购发票的权限' }],
    [400, { 'zh-Hant': '匯出條件無效', en: 'filters are invalid', 'zh-Hans': '导出条件无效' }],
    [500, { 'zh-Hant': '匯出失敗', en: 'The export failed', 'zh-Hans': '导出失败' }],
  ];
  for (const [code, texts] of cases) {
    status = code;
    for (const language of ['zh-Hant', 'en', 'zh-Hans'] as const) {
      await chooseLanguage(page, language);
      await page.getByTestId('pur-invoice-export').click();
      await expect(error).toContainText(texts[language]);
      await expect(page.getByTestId('pur-invoice-export')).toBeEnabled();
    }
    if (code === 500) await expect(error).toContainText('trace-inv-export-500');
    if (code === 400) await expect(error).toContainText('supplierId: SupplierId must be positive.');
    await capture(page, `${code}-light-zh-Hans`);
    await chooseTheme(page, 'dark'); await capture(page, `${code}-dark-zh-Hans`); await chooseTheme(page, 'light');
  }
});

test.describe('PUR-INV-EXPORT-UI-E05 view without export permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: ['PUR.INVOICE.VIEW'].map(code => ({ code, dataScope: 'Company' })) } });
  test('hides the export action and sends no export request', async ({ page }) => {
    const exports = await mockInvoices(page, csvReply(1));
    await page.goto('/pur/purchase-invoices');
    await expect(page.getByTestId('pur-invoice-row-1')).toBeVisible();
    await expect(page.getByTestId('pur-invoice-export')).toHaveCount(0);
    await expect(page.getByTestId('pur-invoice-export-hint')).toHaveCount(0);
    await capture(page, 'view-only-light-zh-Hant');
    expect(exports).toHaveLength(0);
  });
});
