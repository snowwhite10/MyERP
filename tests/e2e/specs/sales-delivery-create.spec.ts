import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/** ADR-155: create/edit form options are delivery routes; generic delivery mocks fall back to the option mocks. */
const FORM_OPTIONS = /\/sal\/deliveries\/(order|warehouse)-options/;

// Sales Outbound Create Interface 2.8 (WU-SD-004c2a): Approved order balance, warehouse/location, Draft save and errors.
const order = { id: 81, documentNo: 'SO-0081', status: 'Approved', customerCode: 'C-7', customerName: 'Saved customer',
  lines: [
    { id: 91, lineNo: 1, itemCode: 'ITEM-9', itemName: 'Lot item', quantity: 12, deliveredQuantity: 2, pendingQuantity: 4.5,
      remainingQuantity: 5.500001 },
    { id: 92, lineNo: 2, itemCode: 'ITEM-10', itemName: 'Used item', quantity: 3, deliveredQuantity: 3, pendingQuantity: 0,
      remainingQuantity: 0 },
    { id: 93, lineNo: 3, itemCode: 'ITEM-11', itemName: 'Plain item', quantity: 2, deliveredQuantity: 0, pendingQuantity: 0,
      remainingQuantity: 2 }] };
const savedDraft = { id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7, customerCode: 'C-7',
  customerName: 'Saved customer', businessDate: '2026-11-21', status: 'Draft', remark: null, approvalInstanceId: null,
  postedAt: null, version: 1, approvalHistory: null, lines: [
    { id: 71, lineNo: 1, orderLineId: 91, orderLineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Lot item', warehouseId: 4,
      locationId: 12, quantity: 5.500001, tracking: [], invMovementId: null },
    { id: 72, lineNo: 2, orderLineId: 93, orderLineNo: 3, itemId: 11, itemCode: 'ITEM-11', itemName: 'Plain item', warehouseId: 4,
      locationId: null, quantity: 2, tracking: [], invMovementId: null }] };

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.8-${name}.png`, fullPage: true, animations: 'disabled' });
}

async function routeSources(page: import('@playwright/test').Page, current = order): Promise<void> {
  await page.route('**/api/v1/sal/deliveries/order-options**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/81')) return route.fulfill({ json: current });
    return route.fulfill({ json: { items: [{ id: 81, documentNo: 'SO-0081', customerCode: 'C-7', customerName: 'Saved customer',
      businessDate: '2026-11-20' }], totalCount: 1, page: 1, pageSize: 10 } });
  });
  await page.route('**/api/v1/sal/deliveries/warehouse-options**', route => {
    const url = new URL(route.request().url());
    return route.fulfill({ json: url.pathname.endsWith('/locations')
      ? { items: [{ id: 12, code: 'A-01', name: 'Aisle 1', trackingMode: null, isActive: true }], totalCount: 1 }
      : { items: [{ id: 4, code: 'WH-1', name: 'Main warehouse', trackingMode: null, isActive: true }], totalCount: 1 } });
  });
}

async function chooseOrderAndWarehouses(page: import('@playwright/test').Page): Promise<void> {
  await page.getByTestId('sd-delivery-create-order-pick').click();
  await page.getByText('SO-0081', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-delivery-create-order')).toContainText('SO-0081 · C-7 · Saved customer');
  await page.getByTestId('sd-delivery-create-date').fill('2026-11-21');
  for (const lineNo of [1, 2]) {
    await page.getByTestId(`sd-delivery-create-warehouse-pick-${lineNo}`).click();
    await page.getByText('WH-1', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
  }
  await page.getByTestId('sd-delivery-create-location-pick-1').click();
  await page.getByText('A-01', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
}

test('SD-DEL-CR-021 creates a Draft from the Approved order balance and confirms it in three languages and two themes', async ({ page }) => {
  let body = '';
  let writes = 0;
  await routeSources(page);
  await page.route('**/api/v1/sal/deliveries**', route => {
    if (FORM_OPTIONS.test(route.request().url())) return route.fallback();
    if (route.request().method() === 'POST') { writes++; body = route.request().postData() ?? '';
      return route.fulfill({ status: 201, json: savedDraft }); }
    return route.fulfill({ json: { items: writes ? [{ id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081',
      customerId: 7, customerName: 'Saved customer', businessDate: '2026-11-21', status: 'Draft', lineCount: 2,
      totalQuantity: 7.500001, version: 1 }] : [], totalCount: writes, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-new').click();
  await expect(page).toHaveURL(/\/sal\/deliveries\/new$/);
  await expect(page.getByTestId('sd-delivery-create')).toContainText('不會預留訂單餘量');
  await capture(page, 'empty-light-zh-Hant');
  await chooseOrderAndWarehouses(page);
  await expect(page.getByTestId('sd-delivery-create-balance-1')).toContainText('5.500001');
  await expect(page.getByTestId('sd-delivery-create-balance-1')).toContainText('4.5');
  await expect(page.getByTestId('sd-delivery-create-line-3')).toHaveCount(0);
  await expect(page.getByTestId('sd-delivery-create-location-1')).toHaveText('A-01 · Aisle 1');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-create-balance-1')).toContainText('Available to ship');
  await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-delivery-create-balance-1')).toContainText('可出货余量');
  await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-success')).toContainText('已儲存出貨草稿 DH-0061。庫存沒有變動。');
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
  expect(writes).toBe(1);
  expect(body).toBe('{"orderId":81,"businessDate":"2026-11-21","lines":[' +
    '{"lineNo":1,"orderLineId":91,"warehouseId":4,"locationId":12,"quantity":5.500001},' +
    '{"lineNo":2,"orderLineId":93,"warehouseId":4,"locationId":null,"quantity":2}]}');
  await page.getByTestId('sd-delivery-create-view').click();
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('草稿');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('7.500001');
});

test('SD-DEL-CR-022 local validation, line 422, balance reload and 500 never claim success', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/deliveries/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (422|500)/);
  let writes = 0;
  let current = order;
  await routeSources(page);
  await page.unroute('**/api/v1/sal/deliveries/order-options**');
  await page.route('**/api/v1/sal/deliveries/order-options**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/81')) return route.fulfill({ json: current });
    return route.fulfill({ json: { items: [{ id: 81, documentNo: 'SO-0081', customerCode: 'C-7', customerName: 'Saved customer',
      businessDate: '2026-11-20' }], totalCount: 1, page: 1, pageSize: 10 } });
  });
  const replies = [
    { status: 422, json: { status: 422, errorCode: 'SAL.DELIVERY.ORDER_LINE_INVALID', lineNo: 2, traceId: 't-422' } },
    { status: 422, json: { status: 422, errorCode: 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED', traceId: 't-422b' } },
    { status: 500, json: { status: 500, traceId: 'trace-sd-500' } }];
  await page.route('**/api/v1/sal/deliveries', route => { writes++; return route.fulfill(replies.shift()!); });
  await page.goto('/sal/deliveries/new');
  await page.getByTestId('sd-delivery-create-order-pick').click();
  await page.getByText('SO-0081', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-delivery-create-quantity-1').fill('5.500002');
  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-errors')).toContainText('4 個欄位需要修正');
  await expect(page.getByTestId('sd-delivery-create-line-1')).toContainText('合計超過可出貨餘量');
  await expect(page.getByTestId('sd-delivery-create-date')).toBeFocused();
  expect(writes).toBe(0);
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant'); await chooseTheme(page, 'light');
  await chooseOrderAndWarehouses(page);
  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-line-error-2')).toContainText('不屬於所選訂單');
  await expect(page.getByTestId('sd-delivery-create-error')).toContainText('伺服器拒絕了標示的出貨行');
  await capture(page, 'line-422-light-zh-Hant');
  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-error')).toContainText('重新載入餘量');
  current = { ...order, lines: order.lines.map(line => line.id === 91 ? { ...line, remainingQuantity: 1 } : line) };
  await page.getByTestId('sd-delivery-create-reload').click();
  await expect(page.getByTestId('sd-delivery-create-balance-1')).toContainText('1');
  await expect(page.getByTestId('sd-delivery-create-quantity-1')).toHaveValue('5.500001');
  await expect(page.getByTestId('sd-delivery-create-line-1')).toContainText('合計超過可出貨餘量');
  await capture(page, 'balance-reload-light-zh-Hant');
  await page.getByTestId('sd-delivery-create-quantity-1').fill('1');
  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-error')).toContainText('trace-sd-500');
  await expect(page.getByTestId('sd-delivery-create-uncertain')).toContainText('出貨單列表搜尋');
  await expect(page.getByTestId('sd-delivery-create-success')).toHaveCount(0);
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
  expect(writes).toBe(3);
});

test.describe('SD-DEL-CR-025 CREATE-only delivery writer (ADR-155)', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false, permissions: [{ code: 'SAL.DELIVERY.CREATE', dataScope: 'Company' }] } });
  test('picks the order, warehouse and location without order or warehouse view and never calls those modules', async ({ page }) => {
    const foreign: string[] = [];
    await page.route(/\/api\/v1\/(sal\/orders|md\/warehouses)/, route => { foreign.push(route.request().url()); return route.abort(); });
    await routeSources(page);
    await page.route('**/api/v1/sal/deliveries', route => route.fulfill({ status: 201, json: savedDraft }));
    await page.goto('/sal/deliveries/new');
    await expect(page.getByTestId('sd-delivery-create-order-pick')).toBeEnabled();
    await chooseOrderAndWarehouses(page);
    await expect(page.getByTestId('sd-delivery-create-balance-1')).toContainText('5.500001');
    await expect(page.getByTestId('sd-delivery-create-warehouse-1')).toHaveText('WH-1 · Main warehouse');
    await expect(page.getByTestId('sd-delivery-create-location-1')).toHaveText('A-01 · Aisle 1');
    await expect(page.getByTestId('sd-delivery-create-order-permission')).toHaveCount(0);
    await expect(page.getByTestId('sd-delivery-create-warehouse-permission')).toHaveCount(0);
    await capture(page, 'create-only-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'create-only-dark-zh-Hant');
    await page.getByTestId('sd-delivery-create-save').click();
    await expect(page.getByTestId('sd-delivery-create-success')).toContainText('DH-0061');
    expect(foreign).toEqual([]);
  });
});
