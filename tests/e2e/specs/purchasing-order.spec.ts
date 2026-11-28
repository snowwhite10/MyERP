import { mkdir, readFile } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** 介面 4.1 採購單列表／詳情／CSV／列印（WU-PUR-006e4）：模擬後端；CSV 表頭／狀態與 PurchaseOrderCsv 三語一致。 */
const row = (id: number, status = 'Approved') => ({ id, documentNo: `PO-2026-${id}`, businessDate: '2026-11-20', supplierId: 5, status,
  currency: 'USD', baseGross: 1050.5, lineCount: 2, version: 3 });
const detail = (remark = 'Keep original terms') => ({ id: 1, documentNo: 'PO-2026-1', status: 'Approved', version: 3, supplierId: 5, rfqId: 9,
  rfqDocumentNo: 'RFQ-0009', businessDate: '2026-11-20', currency: 'USD', baseCurrencyCode: 'TWD', exchangeRate: 31.5,
  baseNet: 1000.5, baseTax: 50, baseGross: 1050.5, remark, ownerId: 1, branchId: null, departmentId: null,
  lines: [{ id: 11, lineNo: 1, itemId: 77, requisitionLineId: 44, awardId: 21, quantity: 1.123456, unitPrice: 10, taxRateId: 3, taxRatePercent: 5,
    originalNet: 11.23456, originalTax: 0.56, originalGross: 11.79456, baseNet: 1, baseTax: 0, baseGross: 1, requisitionId: 4,
    requisitionDocumentNo: 'PR-0004', requisitionLineNo: 2, requisitionQuantity: 10, requisitionOrderedQuantity: 3.5, requisitionRemainingQuantity: 6.5 }],
  approvalHistory: [
    { instanceId: 8, status: 'Approved', submittedAt: '2026-11-21T09:00:00', steps: [{ stepNo: 1, name: 'Manager', status: 'Approved', actedBy: 2, actedAt: '2026-11-21T10:30:00', comment: 'OK to buy' }] },
    { instanceId: 7, status: 'Rejected', submittedAt: '2026-11-20T09:00:00', steps: [{ stepNo: 1, name: 'Manager', status: 'Rejected', actedBy: 2, actedAt: '2026-11-20T11:00:00', comment: 'Price too high' }] }] });
const HEADERS: Record<string, string> = {
  'zh-Hant': '採購單號,業務日期,狀態,供應商 ID,幣別,行號,物料 ID,數量,單價,稅率(%),本位幣未稅,本位幣稅額,本位幣含稅,詢價單號,請購單號,請購行號',
  'zh-Hans': '采购单号,业务日期,状态,供应商 ID,币别,行号,物料 ID,数量,单价,税率(%),本位币未税,本位币税额,本位币含税,询价单号,请购单号,请购行号',
  en: 'Purchase order no.,Business date,Status,Supplier ID,Currency,Line no.,Item ID,Quantity,Unit price,Tax rate (%),Base net,Base tax,Base gross,RFQ no.,Requisition no.,Requisition line no.' };
const STATUS: Record<string, string> = { 'zh-Hant': '已核准', 'zh-Hans': '已批准', en: 'Approved' };

async function capture(page: Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await page.screenshot({ path: `${directory}/4.1-${name}.png`, fullPage: true, animations: 'disabled' });
}
/** One row per saved line; the order number holds a quoted comma and line break to prove RFC 4180 counting. */
function csv(language: string, lines: number): string {
  const body = Array.from({ length: lines }, (_, i) =>
    `"PO-2026-1, ""A""\r\nB",2026-11-20,${STATUS[language]},5,USD,${i + 1},77,1.123456,10,5,1,0,1,RFQ-0009,PR-0004,2`);
  return '﻿' + [HEADERS[language], ...body].join('\r\n') + '\r\n';
}
type Reply = (route: Route, language: string) => Promise<void> | void;
const csvReply = (lines: number, count = String(lines)): Reply => (route, language) =>
  route.fulfill({ status: 200, headers: { 'Content-Type': 'text/csv; charset=utf-8', 'X-Export-Row-Count': count,
    'Content-Language': language, 'Content-Disposition': 'attachment; filename="purchase-orders.csv"' }, body: csv(language, lines) });

interface Backend { list: object[]; listStatus: number; detail: () => { status: number; body: object }; exports: URL[]; lists: URL[]; details: number }
async function mockOrders(page: Page, reply: Reply = csvReply(1)): Promise<Backend> {
  const backend: Backend = { list: [row(1), row(2, 'PendingApproval')], listStatus: 200, detail: () => ({ status: 200, body: detail() }),
    exports: [], lists: [], details: 0 };
  await page.route('**/api/v1/pur/purchase-orders**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/export')) {
      backend.exports.push(url);
      return reply(route, (route.request().headers()['accept-language'] ?? '').split(',')[0]);
    }
    if (/\/purchase-orders\/\d+$/.test(url.pathname)) { backend.details++; const d = backend.detail(); return route.fulfill({ status: d.status, json: d.body }); }
    backend.lists.push(url);
    if (backend.listStatus !== 200) return route.fulfill({ status: backend.listStatus, json: { traceId: 'po-list-trace' } });
    return route.fulfill({ json: { items: backend.list, totalCount: backend.list.length, page: 1, pageSize: 20 } });
  });
  return backend;
}
async function countPrints(page: Page): Promise<void> {
  await page.addInitScript(() => { (window as unknown as { printed: number }).printed = 0;
    window.print = () => { (window as unknown as { printed: number }).printed++; }; });
}
const printed = (page: Page) => page.evaluate(() => (window as unknown as { printed: number }).printed);

test('PUR-PO-UI-E01 list and saved detail in three languages, light and dark; detail matches the saved API data', async ({ page }) => {
  const backend = await mockOrders(page);
  await page.goto('/pur/purchase-orders');
  await expect(page.getByTestId('pur-order-row-1')).toContainText('PO-2026-1');
  await expect(page.getByTestId('pur-order-row-2')).toContainText('待審批');
  await capture(page, 'normal-light-zh-Hant');
  await page.getByRole('button', { name: 'PO-2026-1' }).click();
  const panel = page.getByTestId('pur-order-detail');
  // 畫面與保存資料：決標來源、請購餘量、兩輪審批意見與 API 回應一致。
  await expect(panel).toContainText('RFQ-0009');
  await expect(page.getByTestId('pur-order-line-1')).toContainText('PR-0004 #2');
  await expect(page.getByTestId('pur-order-line-1')).toContainText('1.123456');
  await expect(page.getByTestId('pur-order-line-1')).toContainText('6.5');
  await expect(page.getByTestId('pur-order-round-7')).toContainText('Price too high');
  await expect(page.getByTestId('pur-order-detail-gross')).toHaveText('1,050.5');
  await capture(page, 'detail-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hant');
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en'); await expect(page.getByTestId('pur-order-row-2')).toContainText('Pending approval');
  await capture(page, 'detail-light-en');
  await chooseLanguage(page, 'zh-Hans'); await expect(page.getByTestId('pur-order-row-2')).toContainText('待审批');
  // 審批輪次與狀態標籤用同一詞彙（UX 審查 P3 修正）。
  await expect(page.getByTestId('pur-order-round-8')).toContainText('已批准');
  await expect(page.getByTestId('pur-order-round-7')).toContainText('已拒绝');
  await capture(page, 'detail-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-dark-zh-Hans');
  expect(backend.details).toBe(1);
});

test('PUR-PO-UI-E02 empty, no-result, invalid filter and server error states are distinct', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/purchase-orders/, 500);
  const backend = await mockOrders(page);
  backend.list = [];
  await page.goto('/pur/purchase-orders');
  await expect(page.getByTestId('pur-order-table')).toContainText('尚未有採購單');
  await expect(page.getByTestId('pur-order-export')).toBeDisabled();
  await expect(page.getByTestId('pur-order-export-hint')).toContainText('尚未有採購單');
  await capture(page, 'empty-light-zh-Hant');
  await page.getByTestId('pur-order-status').selectOption('Cancelled');
  await page.getByTestId('pur-order-search').click();
  await expect(page.getByTestId('pur-order-table')).toContainText('沒有符合篩選條件的採購單');
  await capture(page, 'no-results-light-zh-Hant');
  const before = backend.lists.length;
  await page.getByTestId('pur-order-from').fill('2026-12-01');
  await page.getByTestId('pur-order-to').fill('2026-11-01');
  await page.getByTestId('pur-order-search').click();
  await expect(page.getByTestId('pur-order-filter-error')).toHaveText('迄日不可早於起日。');
  expect(backend.lists.length).toBe(before);
  await capture(page, 'validation-light-zh-Hant');
  await page.getByTestId('pur-order-reset').click();
  backend.listStatus = 500;
  await page.getByTestId('pur-order-search').click();
  await expect(page.getByTestId('pur-order-error')).toContainText('po-list-trace');
  await expect(page.getByTestId('pur-order-table')).toContainText('無法載入採購單');
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
  await chooseLanguage(page, 'en'); await expect(page.getByTestId('pur-order-table')).toContainText('Could not load purchase orders');
  await capture(page, 'server-error-dark-en');
});

test('PUR-PO-UI-E03 CSV follows the applied filters and current language; notice count equals the downloaded rows', async ({ page }) => {
  const backend = await mockOrders(page, csvReply(2));
  await page.goto('/pur/purchase-orders');
  await page.getByTestId('pur-order-status').selectOption('Approved');
  await page.getByTestId('pur-order-keyword').fill('PO-2026');
  await page.getByTestId('pur-order-sort').selectOption('documentNo');
  await page.getByTestId('pur-order-search').click();
  await expect(page.getByTestId('pur-order-row-1')).toBeVisible();
  for (const language of ['zh-Hant', 'en', 'zh-Hans'] as const) {
    if (language !== 'zh-Hant') await chooseLanguage(page, language);
    const download = page.waitForEvent('download');
    await page.getByTestId('pur-order-export').click();
    const file = await download;
    expect(file.suggestedFilename()).toBe('purchase-orders.csv');
    const text = (await readFile((await file.path())!)).toString('utf8');
    expect(text.startsWith('﻿' + HEADERS[language] + '\r\n')).toBe(true);
    expect(text).toContain(`,${STATUS[language]},`);
    const notice = page.getByTestId('pur-order-export-notice');
    await expect(notice).toContainText('2');
    await expect(notice.locator('.ant-alert-success')).toHaveCount(1);
    await capture(page, `export-success-light-${language}`);
  }
  await chooseTheme(page, 'dark'); await capture(page, 'export-success-dark-zh-Hans');
  expect(backend.exports).toHaveLength(3);
  for (const url of backend.exports) {
    expect(url.searchParams.get('status')).toBe('Approved');
    expect(url.searchParams.get('keyword')).toBe('PO-2026');
    expect(url.searchParams.get('sort')).toBe('documentNo');
    expect(url.searchParams.has('page')).toBe(false);
  }
});

test('PUR-PO-UI-E04 zero lines warn, a mismatched file and the 10,000-line limit are refused; nothing downloads', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let mode: 'empty' | 'mismatch' | 'limit' = 'empty';
  let downloads = 0;
  page.on('download', () => ++downloads);
  await mockOrders(page, (route, language) => mode === 'empty' ? csvReply(0)(route, language)
    : mode === 'mismatch' ? csvReply(1, '3')(route, language)
    : route.fulfill({ status: 422, contentType: 'application/problem+json', body: JSON.stringify({ errorCode: 'PUR.ORDER.EXPORT_LIMIT' }) }));
  await page.goto('/pur/purchase-orders');
  await page.getByTestId('pur-order-export').click();
  const notice = page.getByTestId('pur-order-export-notice');
  await expect(notice).toContainText('未下載檔案');
  await expect(notice.locator('.ant-alert-warning')).toHaveCount(1);
  await capture(page, 'export-empty-light-zh-Hant');
  mode = 'mismatch';
  await page.getByTestId('pur-order-export').click();
  await expect(page.getByTestId('pur-order-export-error')).toContainText('行數不符');
  mode = 'limit';
  await page.getByTestId('pur-order-export').click();
  await expect(page.getByTestId('pur-order-export-error')).toContainText('10,000');
  await capture(page, 'export-limit-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'export-limit-dark-zh-Hant');
  expect(downloads).toBe(0);
});

test('PUR-PO-UI-E05 print re-reads the saved detail each time and never prints after a failed read', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (500|404)/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/purchase-orders\/1$/, 500);
  await countPrints(page);
  const backend = await mockOrders(page);
  await page.goto('/pur/purchase-orders');
  await page.getByRole('button', { name: 'PO-2026-1' }).click();
  await expect(page.getByTestId('pur-order-detail')).toContainText('Keep original terms');
  backend.detail = () => ({ status: 200, body: detail('Saved after another user edited') });
  await page.getByTestId('pur-order-print').click();
  await expect.poll(() => printed(page)).toBe(1);
  // 資料：列印前重新讀取（第二次 GET），畫面顯示的是重新讀到的保存內容。
  expect(backend.details).toBe(2);
  await expect(page.getByTestId('pur-order-detail')).toContainText('Saved after another user edited');
  await page.emulateMedia({ media: 'print' }); await capture(page, 'print-preview-zh-Hant'); await page.emulateMedia({ media: 'screen' });
  backend.detail = () => ({ status: 500, body: { traceId: 'po-print-trace' } });
  await page.getByTestId('pur-order-print').click();
  await expect(page.getByTestId('pur-order-error')).toContainText('po-print-trace');
  await capture(page, 'print-failed-light-zh-Hant');
  backend.detail = () => ({ status: 404, body: { errorCode: 'PUR.ORDER.NOT_FOUND' } });
  backend.list = [row(2, 'PendingApproval')];
  await page.getByTestId('pur-order-print').click();
  await expect(page.getByTestId('pur-order-error')).toContainText('找不到此採購單');
  await expect(page.getByTestId('pur-order-detail')).toHaveCount(0);
  await expect(page.getByTestId('pur-order-row-1')).toHaveCount(0);
  expect(await printed(page)).toBe(1);
});
