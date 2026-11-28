import { mkdir, readFile } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** 介面 4.4 採購收貨列表／詳情／CSV／列印（WU-PUR-007e1）：模擬後端；CSV 表頭／狀態與 PurchaseReceiptCsv 三語一致。 */
const row = (id: number, status = 'Posted', order: string | null = `PO-2026-${id}`) => ({ id, documentNo: `GR-2026-${id}`,
  businessDate: '2026-11-20', purchaseOrderId: 40 + id, purchaseOrderDocumentNo: order, supplierId: 5, status, currency: 'USD',
  lineCount: 2, totalQuantity: 12.5, version: 3 });
const detail = (lot = 'L-2026-11', status = 'Posted') => ({ id: 1, documentNo: 'GR-2026-1', status, version: 3, purchaseOrderId: 41,
  supplierId: 5, currency: 'USD', businessDate: '2026-11-20', lines: [
    { id: 11, lineNumber: 1, purchaseOrderLineId: 71, materialId: 77, quantity: 10.5, warehouseId: 3, unitCost: 31.123456,
      tracking: [{ seqNo: 1, lotNo: lot, serialNo: null, quantity: 10.5, manufactureDate: '2026-10-01', expiryDate: '2027-10-01' }] },
    { id: 12, lineNumber: 2, purchaseOrderLineId: 72, materialId: 78, quantity: 2, warehouseId: 4, unitCost: 5,
      tracking: [{ seqNo: 1, lotNo: null, serialNo: 'SN-0001', quantity: 1, manufactureDate: null, expiryDate: null },
        { seqNo: 2, lotNo: null, serialNo: 'SN-0002', quantity: 1, manufactureDate: null, expiryDate: null }] }] });
const HEADERS: Record<string, string> = {
  'zh-Hant': '收貨單號,業務日期,狀態,採購單號,供應商 ID,採購幣別,行號,物料 ID,收貨數量,倉庫 ID,本位幣單位成本,已入庫數量',
  'zh-Hans': '收货单号,业务日期,状态,采购单号,供应商 ID,采购币别,行号,物料 ID,收货数量,仓库 ID,本位币单位成本,已入库数量',
  en: 'Receipt no.,Business date,Status,Purchase order no.,Supplier ID,Order currency,Line no.,Item ID,Received quantity,Warehouse ID,Base unit cost,Posted quantity' };
const STATUS: Record<string, string> = { 'zh-Hant': '已過帳', 'zh-Hans': '已过账', en: 'Posted' };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await page.screenshot({ path: `${directory}/4.4-${name}.png`, fullPage: true, animations: 'disabled' });
}
/** One row per saved line; the receipt number holds a quoted comma and line break to prove RFC 4180 counting. */
function csv(language: string, lines: number): string {
  const body = Array.from({ length: lines }, (_, i) =>
    `"GR-2026-1, ""A""\r\nB",2026-11-20,${STATUS[language]},PO-2026-1,5,USD,${i + 1},77,10.5,3,31.123456,10.5`);
  return '﻿' + [HEADERS[language], ...body].join('\r\n') + '\r\n';
}
type Reply = (route: Route, language: string) => Promise<void> | void;
const csvReply = (lines: number, count = String(lines)): Reply => (route, language) =>
  route.fulfill({ status: 200, headers: { 'Content-Type': 'text/csv; charset=utf-8', 'X-Export-Row-Count': count,
    'Content-Language': language, 'Content-Disposition': 'attachment; filename="purchase-receipts.csv"' }, body: csv(language, lines) });

interface Backend { list: object[]; listStatus: number; detail: () => { status: number; body: object }; exports: URL[]; lists: URL[]; details: number }
async function mockReceipts(page: Page, reply: Reply = csvReply(1)): Promise<Backend> {
  const backend: Backend = { list: [row(1), row(2, 'PendingApproval', null), row(3, 'Draft'), row(4, 'Rejected')], listStatus: 200,
    detail: () => ({ status: 200, body: detail() }), exports: [], lists: [], details: 0 };
  await page.route('**/api/v1/pur/purchase-receipts**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/export')) {
      backend.exports.push(url);
      return reply(route, (route.request().headers()['accept-language'] ?? '').split(',')[0]);
    }
    if (/\/purchase-receipts\/\d+$/.test(url.pathname)) { backend.details++; const d = backend.detail(); return route.fulfill({ status: d.status, json: d.body }); }
    backend.lists.push(url);
    if (backend.listStatus !== 200) return route.fulfill({ status: backend.listStatus, json: { traceId: 'gr-list-trace' } });
    return route.fulfill({ json: { items: backend.list, totalCount: backend.list.length, page: 1, pageSize: 20 } });
  });
  return backend;
}
async function countPrints(page: Page): Promise<void> {
  await page.addInitScript(() => { (window as unknown as { printed: number }).printed = 0;
    window.print = () => { (window as unknown as { printed: number }).printed++; }; });
}
const printed = (page: Page) => page.evaluate(() => (window as unknown as { printed: number }).printed);

test('PUR-RECV-UI-E01 menu, list and saved detail in three languages, light and dark; detail matches the saved API data', async ({ page }) => {
  const backend = await mockReceipts(page);
  await page.goto('/');
  await page.getByTestId('layout-menu-pur').click();
  await page.getByTestId('layout-menu-pur-receipts').click();
  await expect(page).toHaveURL(/\/pur\/purchase-receipts$/);
  await expect(page.getByTestId('pur-receipt-row-1')).toContainText('PO-2026-1');
  await expect(page.getByTestId('pur-receipt-row-2')).toContainText('待審批');
  await expect(page.getByTestId('pur-receipt-row-2')).toContainText('採購單 ID 42');
  await capture(page, 'normal-light-zh-Hant');
  await page.getByRole('button', { name: 'GR-2026-1' }).click();
  // 畫面與保存資料：數量、成本、批號日期與序號逐一與 API 回應一致。
  await expect(page.getByTestId('pur-receipt-detail-order')).toHaveText('PO-2026-1');
  await expect(page.getByTestId('pur-receipt-line-1')).toContainText('31.123456');
  await expect(page.getByTestId('pur-receipt-line-1')).toContainText('批號 L-2026-11・10.5・生產 2026-10-01・有效至 2027-10-01');
  await expect(page.getByTestId('pur-receipt-line-2')).toContainText('序號 SN-0002');
  await expect(page.getByTestId('pur-receipt-stock-hint')).toContainText('已按保存的單位成本加入庫存');
  await capture(page, 'detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await expect(page.getByTestId('pur-receipt-row-2')).toContainText('Pending approval');
  await expect(page.getByTestId('pur-receipt-row-4')).toContainText('Rejected');
  await capture(page, 'detail-light-en');
  await chooseLanguage(page, 'zh-Hans'); await expect(page.getByTestId('pur-receipt-row-4')).toContainText('已拒绝');
  await capture(page, 'detail-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hans');
  expect(backend.details).toBe(1);
});

test('PUR-RECV-UI-E02 empty, no-result, invalid filter and server error states are distinct', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/purchase-receipts/, 500);
  const backend = await mockReceipts(page);
  backend.list = [];
  await page.goto('/pur/purchase-receipts');
  await expect(page.getByTestId('pur-receipt-table')).toContainText('尚未有採購收貨單');
  await expect(page.getByTestId('pur-receipt-export')).toBeDisabled();
  await expect(page.getByTestId('pur-receipt-export-hint')).toContainText('尚未有採購收貨單');
  await capture(page, 'empty-light-zh-Hant');
  await page.getByTestId('pur-receipt-status').selectOption('Rejected');
  await page.getByTestId('pur-receipt-search').click();
  await expect(page.getByTestId('pur-receipt-table')).toContainText('沒有符合篩選條件的採購收貨單');
  expect(backend.lists.at(-1)!.searchParams.get('status')).toBe('Rejected');
  await capture(page, 'no-results-light-zh-Hant');
  const before = backend.lists.length;
  await page.getByTestId('pur-receipt-from').fill('2026-12-01');
  await page.getByTestId('pur-receipt-to').fill('2026-11-01');
  await page.getByTestId('pur-receipt-search').click();
  await expect(page.getByTestId('pur-receipt-filter-error')).toHaveText('迄日不可早於起日。');
  expect(backend.lists.length).toBe(before);
  await capture(page, 'validation-light-zh-Hant');
  await page.getByTestId('pur-receipt-reset').click();
  backend.listStatus = 500;
  await page.getByTestId('pur-receipt-search').click();
  await expect(page.getByTestId('pur-receipt-error')).toContainText('gr-list-trace');
  await expect(page.getByTestId('pur-receipt-table')).toContainText('無法載入採購收貨單');
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
  await chooseLanguage(page, 'en'); await expect(page.getByTestId('pur-receipt-table')).toContainText('Could not load purchase receipts');
  await capture(page, 'server-error-dark-en');
});

test('PUR-RECV-UI-E03 CSV follows the applied filters and current language; notice count equals the downloaded rows', async ({ page }) => {
  const backend = await mockReceipts(page, csvReply(2));
  await page.goto('/pur/purchase-receipts');
  await page.getByTestId('pur-receipt-status').selectOption('Posted');
  await page.getByTestId('pur-receipt-keyword').fill('PO-2026');
  await page.getByTestId('pur-receipt-order').fill('41');
  await page.getByTestId('pur-receipt-sort').selectOption('documentNo');
  await page.getByTestId('pur-receipt-search').click();
  await expect(page.getByTestId('pur-receipt-row-1')).toBeVisible();
  for (const language of ['zh-Hant', 'en', 'zh-Hans'] as const) {
    if (language !== 'zh-Hant') await chooseLanguage(page, language);
    const download = page.waitForEvent('download');
    await page.getByTestId('pur-receipt-export').click();
    const file = await download;
    expect(file.suggestedFilename()).toBe('purchase-receipts.csv');
    const text = (await readFile((await file.path())!)).toString('utf8');
    expect(text.startsWith('﻿' + HEADERS[language] + '\r\n')).toBe(true);
    expect(text).toContain(`,${STATUS[language]},`);
    const notice = page.getByTestId('pur-receipt-export-notice');
    await expect(notice).toContainText('2');
    await expect(notice.locator('.ant-alert-success')).toHaveCount(1);
    await capture(page, `export-success-light-${language}`);
  }
  await chooseTheme(page, 'dark'); await capture(page, 'export-success-dark-zh-Hans');
  expect(backend.exports).toHaveLength(3);
  for (const url of backend.exports) {
    expect(url.searchParams.get('status')).toBe('Posted');
    expect(url.searchParams.get('keyword')).toBe('PO-2026');
    expect(url.searchParams.get('purchaseOrderId')).toBe('41');
    expect(url.searchParams.get('sort')).toBe('documentNo');
    expect(url.searchParams.has('page')).toBe(false);
  }
});

test('PUR-RECV-UI-E04 zero lines warn, a mismatched file and the 10,000-line limit are refused; nothing downloads', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let mode: 'empty' | 'mismatch' | 'limit' = 'empty';
  let downloads = 0;
  page.on('download', () => ++downloads);
  await mockReceipts(page, (route, language) => mode === 'empty' ? csvReply(0)(route, language)
    : mode === 'mismatch' ? csvReply(1, '3')(route, language)
    : route.fulfill({ status: 422, contentType: 'application/problem+json', body: JSON.stringify({ errorCode: 'PUR.RECEIPT.EXPORT_LIMIT' }) }));
  await page.goto('/pur/purchase-receipts');
  await page.getByTestId('pur-receipt-export').click();
  const notice = page.getByTestId('pur-receipt-export-notice');
  await expect(notice).toContainText('未下載檔案');
  await expect(notice.locator('.ant-alert-warning')).toHaveCount(1);
  await capture(page, 'export-empty-light-zh-Hant');
  mode = 'mismatch';
  await page.getByTestId('pur-receipt-export').click();
  await expect(page.getByTestId('pur-receipt-export-error')).toContainText('行數不符');
  mode = 'limit';
  await page.getByTestId('pur-receipt-export').click();
  await expect(page.getByTestId('pur-receipt-export-error')).toContainText('10,000');
  await capture(page, 'export-limit-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'export-limit-dark-zh-Hant');
  expect(downloads).toBe(0);
});

test('PUR-RECV-UI-E05 print re-reads the saved detail each time and never prints after a failed read', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (500|404)/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/purchase-receipts\/1$/, 500);
  await countPrints(page);
  const backend = await mockReceipts(page);
  await page.goto('/pur/purchase-receipts');
  await page.getByRole('button', { name: 'GR-2026-1' }).click();
  await expect(page.getByTestId('pur-receipt-detail')).toContainText('L-2026-11');
  backend.detail = () => ({ status: 200, body: detail('L-RELOADED') });
  await page.getByTestId('pur-receipt-print').click();
  await expect.poll(() => printed(page)).toBe(1);
  // 資料：列印前重新讀取（第二次 GET），畫面顯示的是重新讀到的保存內容。
  expect(backend.details).toBe(2);
  await expect(page.getByTestId('pur-receipt-detail')).toContainText('L-RELOADED');
  await page.emulateMedia({ media: 'print' }); await capture(page, 'print-preview-zh-Hant'); await page.emulateMedia({ media: 'screen' });
  backend.detail = () => ({ status: 500, body: { traceId: 'gr-print-trace' } });
  await page.getByTestId('pur-receipt-print').click();
  await expect(page.getByTestId('pur-receipt-error')).toContainText('gr-print-trace');
  await capture(page, 'print-failed-light-zh-Hant');
  backend.detail = () => ({ status: 404, body: { errorCode: 'PUR.RECEIPT.NOT_FOUND' } });
  backend.list = [row(2, 'PendingApproval')];
  await page.getByTestId('pur-receipt-print').click();
  await expect(page.getByTestId('pur-receipt-error')).toContainText('找不到此採購收貨單');
  await expect(page.getByTestId('pur-receipt-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-receipt-row-1')).toHaveCount(0);
  expect(await printed(page)).toBe(1);
});
