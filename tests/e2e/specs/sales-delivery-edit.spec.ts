import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

// Sales Outbound Edit Interface 2.9 (WU-SD-004c2b1): edit Draft/Rejected, kept lot selections, 409/422/500, delete Draft.
const order = { id: 81, documentNo: 'SO-0081', status: 'Approved', customerCode: 'C-7', customerName: 'Saved customer',
  lines: [
    { id: 91, lineNo: 1, itemCode: 'ITEM-9', itemName: 'Lot item', quantity: 12, deliveredQuantity: 2, pendingQuantity: 0,
      remainingQuantity: 10 },
    { id: 93, lineNo: 3, itemCode: 'ITEM-11', itemName: 'Plain item', quantity: 2, deliveredQuantity: 0, pendingQuantity: 0,
      remainingQuantity: 2 }] };
const saved = (status = 'Draft', version = 3, quantity1: number = 2, warehouse1 = 4) => ({ id: 61, documentNo: 'DH-0061', orderId: 81,
  orderDocumentNo: 'SO-0081', customerId: 7, customerCode: 'C-7', customerName: 'Saved customer', businessDate: '2026-11-21', status,
  remark: null, approvalInstanceId: null, postedAt: null, version, approvalHistory: [], lines: [
    { id: 71, lineNo: 1, orderLineId: 91, orderLineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Lot item', warehouseId: warehouse1,
      locationId: warehouse1 === 4 ? 12 : null, quantity: quantity1, tracking: [{ seqNo: 1, lotNo: 'LOT-A', serialNo: null, quantity: 1.5 }],
      invMovementId: null },
    { id: 72, lineNo: 2, orderLineId: 93, orderLineNo: 3, itemId: 11, itemCode: 'ITEM-11', itemName: 'Plain item', warehouseId: 4,
      locationId: null, quantity: 2, tracking: [], invMovementId: null }] });
const summary = (status: string) => ({ items: [{ id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7,
  customerName: 'Saved customer', businessDate: '2026-11-21', status, lineCount: 2, totalQuantity: 4, version: 3 }], totalCount: 1, page: 1, pageSize: 20 });

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.9-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function routeSources(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/v1/sal/orders/81', route => route.fulfill({ json: order }));
  await page.route('**/api/v1/md/warehouses**', route => route.fulfill({ json: { items: [{ id: 5, code: 'WH-2', name: 'Annex',
    address: null, contactName: null, phone: null, isActive: true, version: 1 }], totalCount: 1 } }));
}

test('SD-DEL-ED-030 opens a Rejected outbound from the list, saves changes with kept lots in three languages and two themes', async ({ page }) => {
  let current = saved('Rejected');
  let body = '';
  let writes = 0;
  await routeSources(page);
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'PUT') { writes++; body = route.request().postData() ?? '';
      current = saved('Draft', 4, 3.5, 5); return route.fulfill({ json: current }); }
    if (url.pathname.endsWith('/61')) return route.fulfill({ json: current });
    return route.fulfill({ json: summary(current.status) });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-open-61').click();
  await page.getByTestId('sd-delivery-edit-link').click();
  await expect(page).toHaveURL(/\/sal\/deliveries\/61\/edit$/);
  await expect(page.getByTestId('sd-delivery-edit-identity')).toContainText('DH-0061');
  await expect(page.getByTestId('sd-delivery-edit-rejected')).toContainText('須重新送審');
  await expect(page.getByTestId('sd-delivery-edit-tracking-1')).toContainText('LOT-A');
  await expect(page.getByTestId('sd-delivery-edit-delete')).toBeDisabled();
  await expect(page.getByTestId('sd-delivery-edit-delete-hint')).toContainText('只有草稿可以刪除');
  await capture(page, 'rejected-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'rejected-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-no-change')).toContainText('資料沒有變更');
  expect(writes).toBe(0);
  await page.getByTestId('sd-delivery-edit-quantity-1').fill('3.5');
  await page.getByTestId('sd-delivery-edit-warehouse-pick-1').click();
  await page.getByText('WH-2', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-success')).toContainText('第 4 版');
  expect(writes).toBe(1);
  expect(body).toBe('{"version":3,"draft":{"orderId":81,"businessDate":"2026-11-21","lines":[' +
    '{"lineNo":1,"orderLineId":91,"warehouseId":5,"locationId":null,"quantity":3.5,"tracking":[{"lotNo":"LOT-A","serialNo":null,"quantity":1.5}]},' +
    '{"lineNo":2,"orderLineId":93,"warehouseId":4,"locationId":null,"quantity":2}]}}');
  await expect(page.getByTestId('sd-delivery-edit-delete')).toBeEnabled();
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-edit-success')).toContainText('saved as version 4');
  await capture(page, 'success-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-delivery-edit-success')).toContainText('已保存为第 4 版');
  await capture(page, 'success-light-zh-Hans');
  await page.getByTestId('sd-delivery-edit-back').click();
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('草稿');
});

test('SD-DEL-ED-031 conflict, line 422 and 500 keep input and never claim success; Draft delete confirms then reports deletion', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/deliveries/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422|500)/);
  let current = saved();
  let deleteUrl = '';
  await routeSources(page);
  const replies = [
    { status: 409, json: { status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT', traceId: 't-409' } },
    { status: 422, json: { status: 422, errorCode: 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED', lineNo: 1, traceId: 't-422' } },
    { status: 500, json: { status: 500, traceId: 'trace-sd-edit-500' } }];
  await page.route('**/api/v1/sal/deliveries/61**', route => {
    const method = route.request().method();
    if (method === 'PUT') return route.fulfill(replies.shift()!);
    if (method === 'DELETE') { deleteUrl = route.request().url(); return route.fulfill({ status: 204 }); }
    return route.fulfill({ json: current });
  });
  await page.goto('/sal/deliveries/61/edit');
  await page.getByTestId('sd-delivery-edit-quantity-1').fill('1');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-line-1')).toContainText('少於本行已保存的批號／序號合計');
  await capture(page, 'validation-light-zh-Hant');
  await page.getByTestId('sd-delivery-edit-quantity-1').fill('9');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-stale')).toContainText('已被其他人修改');
  await expect(page.getByTestId('sd-delivery-edit-quantity-1')).toHaveValue('9');
  await expect(page.getByTestId('sd-delivery-edit-save')).toBeDisabled();
  await expect(page.getByTestId('sd-delivery-edit-stale-hint')).toContainText('請先重新整理');
  await capture(page, 'conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'conflict-dark-zh-Hant'); await chooseTheme(page, 'light');
  current = saved('Draft', 4);
  page.once('dialog', dialog => dialog.accept());
  await page.getByTestId('sd-delivery-edit-refresh').click();
  await expect(page.getByTestId('sd-delivery-edit-identity')).toContainText('4');
  await expect(page.getByTestId('sd-delivery-edit-quantity-1')).toHaveValue('2');
  await page.getByTestId('sd-delivery-edit-quantity-1').fill('9');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-line-error-1')).toContainText('超過訂單');
  await expect(page.getByTestId('sd-delivery-edit-error')).toContainText('伺服器拒絕了標示的明細行');
  await capture(page, 'line-422-light-zh-Hant');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-error')).toContainText('trace-sd-edit-500');
  await expect(page.getByTestId('sd-delivery-edit-stale')).toContainText('無法確認修改是否已儲存');
  await expect(page.getByTestId('sd-delivery-edit-success')).toHaveCount(0);
  await capture(page, 'server-error-light-zh-Hant');
  page.once('dialog', dialog => dialog.accept());
  await page.getByTestId('sd-delivery-edit-refresh').click();
  await page.getByTestId('sd-delivery-edit-delete').click();
  await expect(page.getByTestId('erp-message-title')).toContainText('DH-0061');
  await capture(page, 'delete-confirm-light-zh-Hant');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-edit-deleted')).toContainText('出貨單 DH-0061 已刪除');
  expect(new URL(deleteUrl).searchParams.get('version')).toBe('4');
  await expect(page.getByTestId('sd-delivery-edit-form')).toHaveCount(0);
  await capture(page, 'deleted-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'deleted-dark-zh-Hant');
});

test('SD-DEL-ED-032 a Posted outbound opened by URL is explained and offers no form', async ({ page }) => {
  await routeSources(page);
  await page.route('**/api/v1/sal/deliveries/61', route => route.fulfill({ json: saved('Posted') }));
  await page.goto('/sal/deliveries/61/edit');
  await expect(page.getByTestId('sd-delivery-edit-blocked')).toContainText('已過帳');
  await expect(page.getByTestId('sd-delivery-edit-form')).toHaveCount(0);
  await capture(page, 'blocked-light-zh-Hant');
});

test('SD-DEL-ED-034 a Lot line and a Serial line are edited with typed picks, incomplete Draft saves, three languages and two themes', async ({ page }) => {
  // WU-SD-004c2b2b1 (ADR-151): trackingMode from the saved line decides the editor; no MD.ITEM.VIEW needed.
  const tracked = (version: number, lots: { lotNo: string; quantity: number }[], serials: string[]) => {
    const value = saved('Draft', version);
    value.lines[0] = { ...value.lines[0], trackingMode: 'Lot',
      tracking: lots.map((lot, i) => ({ seqNo: i + 1, lotNo: lot.lotNo, serialNo: null, quantity: lot.quantity })) } as typeof value.lines[0];
    value.lines[1] = { ...value.lines[1], trackingMode: 'Serial',
      tracking: serials.map((serialNo, i) => ({ seqNo: i + 1, lotNo: null, serialNo, quantity: 1 })) } as typeof value.lines[1];
    return value;
  };
  let current = tracked(3, [{ lotNo: 'LOT-A', quantity: 1.5 }], []);
  let body = '';
  await routeSources(page);
  await page.route('**/api/v1/sal/deliveries**', route => {
    if (route.request().method() === 'PUT') { body = route.request().postData() ?? '';
      current = tracked(4, [{ lotNo: 'LOT-A', quantity: 1.5 }, { lotNo: 'LOT-B', quantity: 0.5 }], ['SN-1']);
      return route.fulfill({ json: current }); }
    return route.fulfill({ json: current });
  });
  await page.goto('/sal/deliveries/61/edit');
  await expect(page.getByTestId('sd-delivery-edit-lot-no-1-0')).toHaveValue('LOT-A');
  await expect(page.getByTestId('sd-delivery-edit-progress-1')).toContainText('已選 1.5／出貨數量 2');
  await expect(page.getByTestId('sd-delivery-edit-progress-2')).toContainText('已選 0／出貨數量 2');
  await page.getByTestId('sd-delivery-edit-lot-add-1').click();
  await expect(page.getByTestId('sd-delivery-edit-lot-no-1-1')).toBeFocused();
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-tracking-issue-1')).toContainText('每行須填一個批號');
  await expect(page.getByTestId('sd-delivery-edit-lot-no-1-0')).toBeFocused();
  await capture(page, 'tracking-invalid-light-zh-Hant');
  await page.getByTestId('sd-delivery-edit-lot-no-1-1').fill('LOT-B');
  await page.getByTestId('sd-delivery-edit-lot-quantity-1-1').fill('0.5');
  await expect(page.getByTestId('sd-delivery-edit-progress-1')).toContainText('已齊，可以送審');
  // UX P2 fix: a corrected row no longer shows its stale issue or the error count.
  await expect(page.getByTestId('sd-delivery-edit-tracking-issue-1')).toHaveCount(0);
  await expect(page.getByTestId('sd-delivery-edit-errors')).toHaveCount(0);
  await page.getByTestId('sd-delivery-edit-serials-2').fill('SN-1\n');
  await expect(page.getByTestId('sd-delivery-edit-serials-2')).toHaveValue('SN-1\n');
  await expect(page.getByTestId('sd-delivery-edit-progress-2')).toContainText('已選 1／出貨數量 2');
  await capture(page, 'tracking-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'tracking-dark-zh-Hant'); await chooseTheme(page, 'light');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-success')).toContainText('第 4 版');
  expect(body).toContain('"tracking":[{"lotNo":"LOT-A","serialNo":null,"quantity":1.5},{"lotNo":"LOT-B","serialNo":null,"quantity":0.5}]');
  expect(body).toContain('"quantity":2,"tracking":[{"lotNo":null,"serialNo":"SN-1","quantity":1}]');
  await expect(page.getByTestId('sd-delivery-edit-progress-2')).toContainText('送審前選取總數必須等於出貨數量');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-edit-progress-2')).toContainText('Selected 1 of outbound quantity 2');
  await capture(page, 'tracking-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-delivery-edit-progress-1')).toContainText('已齐，可以送审');
  await capture(page, 'tracking-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'tracking-dark-zh-Hans');
});

test('SD-DEL-ED-035 lots and serials are picked from stock at the line location, capped at the quantity, three languages and two themes', async ({ page , pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (403|409)/);
  // WU-SD-004c2b2b2b (ADR-152): GET tracking-options per opening; on-hand only; duplicates skipped; 403 explained in the dialog.
  const value = saved('Draft', 3);
  value.lines[0] = { ...value.lines[0], trackingMode: 'Lot' } as typeof value.lines[0];
  value.lines[1] = { ...value.lines[1], trackingMode: 'Serial' } as typeof value.lines[1];
  const queries: string[] = [];
  let serialCalls = 0;
  let body = '';
  await routeSources(page);
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/tracking-options')) {
      queries.push(url.search);
      if (url.searchParams.get('orderLineId') === '91') return route.fulfill({ json: { trackingMode: 'Lot', truncated: true, items: [
        { number: 'LOT-A', quantity: 4, manufactureDate: '2026-01-02', expiryDate: '2026-12-31' },
        { number: 'LOT-B', quantity: 0.3, manufactureDate: null, expiryDate: '2027-01-01' },
        { number: 'LOT-C', quantity: 5, manufactureDate: null, expiryDate: null }] } });
      if (++serialCalls === 1) return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED', traceId: 'trace-403' } });
      return route.fulfill({ json: { trackingMode: 'Serial', truncated: false, items: [
        { number: 'SN-1', quantity: 1, manufactureDate: null, expiryDate: null }, { number: 'SN-2', quantity: 1, manufactureDate: null, expiryDate: null },
        { number: 'SN-3', quantity: 1, manufactureDate: null, expiryDate: null }] } });
    }
    if (route.request().method() === 'PUT') { body = route.request().postData() ?? ''; return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY' } }); }
    return route.fulfill({ json: value });
  });
  await page.goto('/sal/deliveries/61/edit');
  await expect(page.getByTestId('sd-delivery-edit-stock-hint-1')).toContainText('按本行目前的倉庫／儲位');
  await page.getByTestId('sd-delivery-edit-stock-1').click();
  await expect(page.getByTestId('erp-select-title')).toContainText('第 1 行：從庫存挑選批號');
  await expect(page.getByTestId('erp-select-description')).toContainText('並非為此單預留');
  await expect(page.getByTestId('erp-select-notice')).toContainText('只顯示前 100 筆');
  await expect(page.getByTestId('erp-select-cell-LOT-C-expiry')).toHaveText('無到期日');
  expect(queries[0]).toBe('?orderLineId=91&warehouseId=4&locationId=12');
  await page.getByTestId('erp-select-row-LOT-A').click();
  await page.getByTestId('erp-select-row-LOT-C').click();
  await page.getByTestId('erp-select-row-LOT-B').click();
  await capture(page, 'stock-lot-dialog-light-zh-Hant');
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-delivery-edit-stock-result-1')).toContainText('已加入 2 個。 1 個已在本行，未重複加入。');
  await expect(page.getByTestId('sd-delivery-edit-lot-no-1-1')).toHaveValue('LOT-B');
  await expect(page.getByTestId('sd-delivery-edit-lot-quantity-1-1')).toHaveValue('0.3');
  await expect(page.getByTestId('sd-delivery-edit-lot-quantity-1-2')).toHaveValue('0.2');
  await expect(page.getByTestId('sd-delivery-edit-progress-1')).toContainText('已齊，可以送審');

  await page.getByTestId('sd-delivery-edit-stock-2').click();
  await expect(page.getByTestId('erp-select-error')).toBeVisible();
  await expect(page.getByTestId('erp-select-error')).toContainText('您沒有執行此操作的權限');
  await capture(page, 'stock-error-light-zh-Hant');
  await page.getByTestId('erp-select-cancel').click();
  await expect(page.getByTestId('sd-delivery-edit-serials-2')).toHaveValue('');
  await page.getByTestId('sd-delivery-edit-stock-2').click();
  expect(queries.at(-1)).toBe('?orderLineId=93&warehouseId=4');
  for (const serial of ['SN-3', 'SN-1', 'SN-2']) await page.getByTestId(`erp-select-row-${serial}`).click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-delivery-edit-serials-2')).toHaveValue('SN-1\nSN-2');
  await expect(page.getByTestId('sd-delivery-edit-stock-result-2')).toContainText('1 個因已達出貨數量而未加入');
  await capture(page, 'stock-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'stock-dark-zh-Hant');
  await page.getByTestId('sd-delivery-edit-stock-1').click();
  await expect(page.getByTestId('erp-select-notice')).toBeVisible();
  await capture(page, 'stock-lot-dialog-dark-zh-Hant');
  await page.getByTestId('erp-select-cancel').click();
  await chooseTheme(page, 'light');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-edit-stock-result-1')).toContainText('2 added. 1 already on this line, not added again.');
  await capture(page, 'stock-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-delivery-edit-stock-1')).toHaveText('从库存挑选');
  await capture(page, 'stock-light-zh-Hans');

  await page.getByTestId('sd-delivery-edit-location-clear-1').click();
  await expect(page.getByTestId('sd-delivery-edit-relocated-1')).toContainText('仓库或储位已改变');
  await capture(page, 'stock-relocated-light-zh-Hans');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-stale')).toBeVisible();
  expect(body).toContain('"locationId":null,"quantity":2,"tracking":[{"lotNo":"LOT-A","serialNo":null,"quantity":1.5},{"lotNo":"LOT-B","serialNo":null,"quantity":0.3},{"lotNo":"LOT-C","serialNo":null,"quantity":0.2}]');
  expect(body).toContain('"tracking":[{"lotNo":null,"serialNo":"SN-1","quantity":1},{"lotNo":null,"serialNo":"SN-2","quantity":1}]');
});
