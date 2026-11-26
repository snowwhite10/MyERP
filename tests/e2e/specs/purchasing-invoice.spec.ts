import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** 介面 4.19 Purchase Invoice View（WU-PUR-008c1）：唯讀列表／明細／差異決定歷史，三語亮暗截圖。 */
const row = (id: number, status: string, over: object = {}) => ({ id, supplierId: 5, purchaseOrderId: 70,
  supplierInvoiceNumber: `INV-2026-${id}`, businessDate: '2026-11-20', currency: 'TWD', status, totalNet: 1000, totalTax: 50,
  totalGross: 1050, lineCount: 2, version: 3, ...over });
const rows = [row(1, 'Draft'), row(2, 'Matched'), row(3, 'Disputed', { totalGross: 'BIG' }), row(4, 'Rejected'), row(5, 'Posted')];
/** Exact server digits: a 19-digit gross that a JavaScript double would round. */
const listBody = (items: object[]) => JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 20 })
  .replace('"totalGross":"BIG"', '"totalGross":999999999999999.9999');
const detail = { ...row(3, 'Draft'), createdAt: '2026-11-20T08:00:00Z', createdBy: 3, updatedAt: '2026-11-22T10:15:00Z',
  lines: [
    { id: 11, lineNo: 1, receiptLineId: 900, quantity: 10, unitPrice: 52.5, taxRatePercent: 5, net: 525, tax: 26.25, gross: 551.25 },
    { id: 12, lineNo: 2, receiptLineId: 901, quantity: 5, unitPrice: 95, taxRatePercent: 5, net: 475, tax: 23.75, gross: 498.75 }],
  resolutions: [
    { id: 41, invoiceVersion: 2, lineNo: 1, action: 'Accept', reason: '供應商附加運費，已與採購確認', netDifference: 25, taxDifference: 1.25,
      grossDifference: 26.25, decidedBy: 8, decidedAt: '2026-11-22T10:15:00Z' },
    { id: 42, invoiceVersion: 2, lineNo: 2, action: 'Reject', reason: '單價未經同意', netDifference: -5, taxDifference: -0.25,
      grossDifference: -5.25, decidedBy: 8, decidedAt: '2026-11-22T10:15:00Z' }] };

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const directory = '../../tests/qa/PUR/screenshots';
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: `${directory}/4.19-${name}.png`, fullPage: true, animations: 'disabled' });
}
async function both(page: import('@playwright/test').Page, name: string): Promise<void> {
  await capture(page, `${name}-light-zh-Hant`);
  await chooseTheme(page, 'dark'); await capture(page, `${name}-dark-zh-Hant`); await chooseTheme(page, 'light');
}

test('PUR-INV-UI-E01 list, exact amounts, five states, detail lines and decision history in three languages', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/api/v1/pur/invoices**', route => {
    methods.push(route.request().method());
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/invoices/3')) return route.fulfill({ contentType: 'application/json', body: JSON.stringify(detail) });
    return route.fulfill({ contentType: 'application/json', body: listBody(rows) });
  });
  await page.goto('/pur/purchase-invoices');
  await expect(page.getByTestId('pur-invoice-gross-3')).toHaveText('999,999,999,999,999.9999 TWD');
  await expect(page.getByTestId('pur-invoice-status-3')).toHaveText('有差異');
  await expect(page.getByTestId('pur-invoice-status-2')).toHaveText('已比對');
  await page.getByTestId('pur-invoice-view-3').click();
  await expect(page.getByTestId('pur-invoice-state-hint')).toHaveText('已保存，尚未與採購單及收貨比對。');
  await expect(page.getByTestId('pur-invoice-decision-41')).toContainText('+26.25');
  await expect(page.getByTestId('pur-invoice-decision-42')).toContainText('拒絕');
  await expect(page.getByTestId('pur-invoice-decision-42')).toContainText('-5.25');
  await both(page, 'normal');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('pur-invoice-decision-42')).toContainText('Rejected');
  await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('采购发票');
  await capture(page, 'normal-light-zh-Hans');
  expect(methods.every(m => m === 'GET')).toBe(true);
});

test('PUR-INV-UI-E02 empty, search-empty, invalid filter, 404 detail and server error each explain themselves', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (404|500)/);
  pageErrors.allowApi5xx(/\/api\/v1\/pur\/invoices/, 500);
  let mode: 'empty' | 'normal' | 'server' = 'empty';
  const queries: string[] = [];
  await page.route('**/api/v1/pur/invoices**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/invoices/4'))
      return route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ errorCode: 'PUR.INVOICE.NOT_FOUND' }) });
    queries.push(url.search);
    if (mode === 'server') return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ traceId: 'inv-trace-500' }) });
    return route.fulfill({ contentType: 'application/json', body: listBody(mode === 'empty' || url.searchParams.has('keyword') ? [] : rows) });
  });
  await page.goto('/pur/purchase-invoices');
  await expect(page.getByTestId('pur-invoice-table')).toContainText('尚未有採購發票。');
  await both(page, 'empty');
  mode = 'normal';
  await page.getByTestId('pur-invoice-keyword').fill('NOPE');
  await page.getByTestId('pur-invoice-search').click();
  await expect(page.getByTestId('pur-invoice-table')).toContainText('沒有符合篩選條件的採購發票。');
  await both(page, 'no-results');
  const sent = queries.length;
  await page.getByTestId('pur-invoice-from').fill('2026-11-30');
  await page.getByTestId('pur-invoice-to').fill('2026-11-01');
  await page.getByTestId('pur-invoice-search').click();
  await expect(page.getByTestId('pur-invoice-filter-error')).toHaveText('結束日期不可早於開始日期。');
  expect(queries.length).toBe(sent);
  await both(page, 'validation');
  await page.getByTestId('pur-invoice-reset').click();
  await expect(page.getByTestId('pur-invoice-row-4')).toBeVisible();
  await page.getByTestId('pur-invoice-view-4').click();
  await expect(page.getByTestId('pur-invoice-error')).toContainText('找不到這張採購發票');
  await expect(page.getByTestId('pur-invoice-detail')).toHaveCount(0);
  await both(page, 'not-found');
  mode = 'server';
  await page.getByTestId('pur-invoice-search').click();
  await expect(page.getByTestId('pur-invoice-error')).toContainText('inv-trace-500');
  await expect(page.getByTestId('pur-invoice-table')).toContainText('無法載入採購發票，請重試。');
  await expect(page.getByTestId('pur-invoice-row-1')).toHaveCount(0);
  await both(page, 'server-error');
});

/** WU-PUR-008c3b: Draft → MATCH (Disputed) → per-line RESOLVE; screen messages are checked against the requests actually sent. */
test('PUR-INV-UI-E03 MATCH and RESOLVE explain each step and send exactly what the screen shows', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let state: 'Draft' | 'Disputed' = 'Draft'; let version = 3; let resolveReply: 'lock' | 'ok' = 'lock';
  const posts: { path: string; version: string | null; body: unknown }[] = [];
  const disputedLines = [
    { id: 11, lineNo: 1, receiptLineId: 900, quantity: 10, unitPrice: 55, taxRatePercent: 5, net: 550, tax: 27.5, gross: 577.5,
      differences: [{ kind: 'UNIT_PRICE', baseline: 52.5, invoice: 55, difference: 2.5 }, { kind: 'NET', baseline: 525, invoice: 550, difference: 25 },
        { kind: 'GROSS', baseline: 551.25, invoice: 577.5, difference: 26.25 }] },
    { id: 12, lineNo: 2, receiptLineId: 901, quantity: 5, unitPrice: 95, taxRatePercent: 5, net: 475, tax: 23.75, gross: 498.75, differences: [] }];
  const body = () => JSON.stringify({ ...detail, id: 1, supplierInvoiceNumber: 'INV-2026-1', status: state, version, resolutions: [],
    lines: state === 'Disputed' ? disputedLines : detail.lines.map(l => ({ ...l, differences: [] })) });
  await page.route('**/api/v1/pur/invoices**', route => {
    const request = route.request(); const url = new URL(request.url());
    if (request.method() === 'POST') {
      posts.push({ path: url.pathname, version: url.searchParams.get('version'), body: request.postDataJSON() });
      if (url.pathname.endsWith('/match')) {
        if (posts.length === 1) return route.fulfill({ status: 422, contentType: 'application/json',
          body: JSON.stringify({ errorCode: 'PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY' }) });
        state = 'Disputed'; version = 4;
        return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ id: 1, version, status: state, amount: {} }) });
      }
      if (resolveReply === 'lock') return route.fulfill({ status: 409, contentType: 'application/json',
        body: JSON.stringify({ errorCode: 'PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE' }) });
      state = 'Draft'; version = 5;
      return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ id: 1, version, status: state, decisions: [] }) });
    }
    if (url.pathname.endsWith('/invoices/1')) return route.fulfill({ contentType: 'application/json', body: body() });
    return route.fulfill({ contentType: 'application/json', body: listBody([row(1, state, { version })]) });
  });
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  await page.goto('/pur/purchase-invoices');
  await page.getByTestId('pur-invoice-view-1').click();
  await expect(page.getByTestId('pur-invoice-match')).toHaveText('比對');
  await both(page, 'match-draft');
  // 422: nothing changed, the reason is shown and the invoice stays Draft.
  await page.getByTestId('pur-invoice-match').click();
  await expect(page.getByRole('dialog')).toContainText('不會過帳到應付帳款');
  await page.getByRole('dialog').getByRole('button', { name: '比對' }).click();
  await expect(page.getByTestId('pur-invoice-action-error')).toContainText('收貨可開票數量不足');
  await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('草稿');
  await both(page, 'match-422');
  await page.getByTestId('pur-invoice-match').click();
  await page.getByRole('dialog').getByRole('button', { name: '比對' }).click();
  await expect(page.getByText('發票 INV-2026-1 有單價或稅額差異，請在下方逐項接受或拒絕。')).toBeVisible();
  await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('有差異');
  await expect(page.getByTestId('pur-invoice-status-1')).toHaveText('有差異');
  await expect(page.getByTestId('pur-invoice-dispute-1')).toContainText('單價 52.5 → 55 (+2.5)');
  await expect(page.getByTestId('pur-invoice-dispute-2')).toHaveCount(0);
  expect(posts.map(p => [p.path.split('/').pop(), p.version, p.body])).toEqual([['match', '3', null], ['match', '3', null]]);
  await page.getByTestId('pur-invoice-resolve-submit').click();
  await expect(page.getByTestId('pur-invoice-issue-1')).toHaveText('請選擇接受或拒絕。');
  await expect(page.getByTestId('pur-invoice-accept-1')).toBeFocused();
  expect(posts).toHaveLength(2);
  await both(page, 'resolve-validation');
  await page.getByTestId('pur-invoice-reject-1').check();
  await page.getByTestId('pur-invoice-reason-1').fill('  單價未經同意  ');
  await both(page, 'resolve-filled');
  await page.getByTestId('pur-invoice-resolve-submit').click();
  await expect(page.getByRole('dialog')).toContainText('接受 0 項、拒絕 1 項');
  await page.getByRole('dialog').getByRole('button', { name: '提交決定' }).click();
  // The reason is shown inside the decision form, next to the submit button the user just pressed.
  await expect(page.getByTestId('pur-invoice-resolve').getByTestId('pur-invoice-action-error')).toContainText('正在進行其他操作');
  await expect(page.getByTestId('pur-invoice-reason-1')).toHaveValue('  單價未經同意  ');
  await both(page, 'resolve-409');
  resolveReply = 'ok';
  await page.getByTestId('pur-invoice-resolve-submit').click();
  await page.getByRole('dialog').getByRole('button', { name: '提交決定' }).click();
  await expect(page.getByText('已保存決定。發票 INV-2026-1 已退回草稿並釋回預留數量，請修訂發票後再比對。')).toBeVisible();
  await expect(page.getByTestId('pur-invoice-detail-status')).toHaveText('草稿');
  await expect(page.getByTestId('pur-invoice-resolve')).toHaveCount(0);
  expect(posts.slice(2).map(p => [p.path.split('/').pop(), p.version, p.body])).toEqual([
    ['resolve', '4', { lines: [{ lineNo: 1, action: 'Reject', reason: '單價未經同意' }] }],
    ['resolve', '4', { lines: [{ lineNo: 1, action: 'Reject', reason: '單價未經同意' }] }]]);
  await both(page, 'resolved-draft');
  // Translations of the Disputed form in the other two languages.
  state = 'Disputed'; version = 6;
  await page.getByTestId('pur-invoice-view-1').click();
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('pur-invoice-dispute-1')).toContainText('Unit price 52.5 → 55 (+2.5)');
  await capture(page, 'resolve-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('pur-invoice-resolve-submit')).toHaveText('提交决定');
  await capture(page, 'resolve-light-zh-Hans');
});
