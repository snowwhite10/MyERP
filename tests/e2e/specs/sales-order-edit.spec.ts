import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.6-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-ORDER-EDIT-001/004 saves a rejected order after a fresh receipt and retains input on conflict', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  let version = 3;
  let writes = 0;
  let conflict = true;
  let remark = 'Original';
  const saved = () => ({ id: 81, documentNo: 'SO-0081', status: version === 3 ? 'Rejected' : 'Draft', version,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark, lines: [{ id: 1, lineNo: 1, itemId: 9,
      itemCode: 'I-9', itemName: 'Item Nine', quantity: 1.000001, unitPrice: 0.000001,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] });
  await page.route('**/api/v1/sal/orders/81', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved() });
    writes++;
    if (conflict) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
    remark = JSON.parse(route.request().postData() ?? '{}').draft.remark;
    version++;
    return route.fulfill({ json: saved() });
  });
  await page.goto('/sal/orders/81/edit');
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Original');
  await expect(page.getByTestId('sd-order-create')).not.toContainText('undefined');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans'); await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-order-create-remark').fill('Changed');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-edit-conflict')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Changed');
  await expect(page.getByTestId('sd-order-create-save')).toBeDisabled();
  expect({ writes, remark }).toEqual({ writes: 1, remark: 'Original' });
  await capture(page, 'conflict-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'conflict-dark-zh-Hant');
  await chooseTheme(page, 'light');
  conflict = false;
  await page.getByTestId('sd-order-edit-refresh').click();
  await page.getByTestId('sd-order-create-remark').fill('Changed');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0081');
  expect({ writes, remark, version }).toEqual({ writes: 2, remark: 'Changed', version: 4 });
  await capture(page, 'success-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'success-dark-zh-Hant');
});

test('SD-ORDER-EDIT-002/006/008 blocks immutable and unchanged writes with an explanation', async ({ page }) => {
  let writes = 0;
  let status = 'Approved';
  const saved = () => ({ id: 82, documentNo: 'SO-0082', status, version: 2,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Same', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] });
  await page.route('**/api/v1/sal/orders/82', route => {
    if (route.request().method() === 'PUT') writes++;
    return route.fulfill({ json: saved() });
  });
  await page.goto('/sal/orders/82/edit');
  await expect(page.getByTestId('sd-order-edit-blocked')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
  await capture(page, 'blocked-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'blocked-dark-zh-Hant');
  expect(writes).toBe(0);
  status = 'Draft';
  await page.reload();
  await chooseTheme(page, 'light');
  await expect(page.getByTestId('sd-order-create-save')).toBeEnabled();
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-edit-no-change')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  expect(writes).toBe(0);
  await page.getByTestId('sd-order-create-date').fill('');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-errors')).toBeVisible();
  expect(writes).toBe(0);
  await capture(page, 'validation-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-dark-zh-Hant');
});

test('SD-ORDER-EDIT-010/011 500 preserves input and blocks repeat PUT until saved read', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/orders\/83$/, 500);
  let writes = 0;
  const saved = { id: 83, documentNo: 'SO-0083', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/83', route => {
    if (route.request().method() === 'PUT') { writes++; return route.fulfill({ status: 500,
      json: { errorCode: 'COMMON.UNEXPECTED', traceId: 'trace-order-edit-500' } }); }
    return route.fulfill({ json: saved });
  });
  await page.goto('/sal/orders/83/edit');
  await page.getByTestId('sd-order-create-remark').fill('My unsaved edit');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-error')).toContainText('trace-order-edit-500');
  await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('My unsaved edit');
  await expect(page.getByTestId('sd-order-create-save')).toBeDisabled();
  expect({ writes, savedRemark: saved.remark }).toEqual({ writes: 1, savedRemark: 'Original' });
  await capture(page, 'server-500-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-500-dark-zh-Hant');
});

test('SD-ORDER-EDIT-009/010 422 names the rejected field and preserves the saved order', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let writes = 0;
  const saved = { id: 84, documentNo: 'SO-0084', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/84', route => route.request().method() === 'GET'
    ? route.fulfill({ json: saved })
    : (writes++, route.fulfill({ status: 422, json: { errorCode: 'SAL.ORDER.REFERENCE.INVALID',
      errors: { customerId: ['Customer is inactive'] } } })));
  await page.goto('/sal/orders/84/edit');
  await page.getByTestId('sd-order-create-remark').fill('Changed');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-error')).toContainText('伺服器拒絕修改訂單');
  await expect(page.getByTestId('sd-order-create-error')).toContainText('客戶');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Changed');
  expect({ writes, savedRemark: saved.remark, version: saved.version }).toEqual({ writes: 1, savedRemark: 'Original', version: 1 });
  await capture(page, 'business-422-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'business-422-dark-zh-Hant');
});

test.describe('SD-ORDER-EDIT-007 edit permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'SAL.ORDER.VIEW', dataScope: 'All' }] } });
  test('blocks direct navigation without a saved read', async ({ page }) => {
    let reads = 0;
    await page.route('**/api/v1/sal/orders/85', route => { reads++; return route.fulfill({ json: {} }); });
    await page.goto('/sal/orders/85/edit');
    await expect(page).toHaveURL(/\/forbidden$/);
    await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
    expect(reads).toBe(0);
    await capture(page, 'no-permission-light-zh-Hant');
    await chooseTheme(page, 'dark'); await capture(page, 'no-permission-dark-zh-Hant');
  });
});

test('SD-ORDER-EDIT-010 400 names the invalid field without claiming a save', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 400/);
  let writes = 0;
  const saved = { id: 86, documentNo: 'SO-0086', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/86', route => route.request().method() === 'GET'
    ? route.fulfill({ json: saved })
    : (writes++, route.fulfill({ status: 400, json: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { businessDate: ['Date is outside the allowed period'] } } })));
  await page.goto('/sal/orders/86/edit');
  await page.getByTestId('sd-order-create-remark').fill('Unsaved revision');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-error')).toContainText('日期');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Unsaved revision');
  expect({ writes, savedRemark: saved.remark, version: saved.version }).toEqual({ writes: 1, savedRemark: 'Original', version: 1 });
  await capture(page, 'validation-400-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'validation-400-dark-zh-Hant');
});

test('SD-ORDER-EDIT-011 timed-out saved read offers retry without exposing an empty form', async ({ page }) => {
  let reads = 0;
  let writes = 0;
  const saved = { id: 87, documentNo: 'SO-0087', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/87', async route => {
    if (route.request().method() !== 'GET') { writes++; return route.fulfill({ json: saved }); }
    reads++;
    if (reads === 1) return new Promise<void>(() => {});
    return route.fulfill({ json: saved });
  });
  await page.goto('/sal/orders/87/edit');
  await expect(page.getByTestId('sd-order-edit-load-error')).toBeVisible({ timeout: 10000 });
  await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
  await capture(page, 'timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'timeout-dark-zh-Hant');
  expect({ reads, writes }).toEqual({ reads: 1, writes: 0 });
  await page.getByTestId('sd-order-edit-retry').click();
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Original');
  expect({ reads, writes }).toEqual({ reads: 2, writes: 0 });
});

test('SD-ORDER-EDIT-006/012 mismatched saved receipt leaves the write outcome uncertain', async ({ page }) => {
  let writes = 0;
  let reads = 0;
  const saved = { id: 88, documentNo: 'SO-0088', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/88', route => {
    if (route.request().method() === 'GET') { reads++; return route.fulfill({ json: saved }); }
    writes++;
    return route.fulfill({ json: { ...saved, version: 2, remark: 'Changed' } });
  });
  await page.goto('/sal/orders/88/edit');
  await page.getByTestId('sd-order-create-remark').fill('Changed');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Changed');
  await expect(page.getByTestId('sd-order-create-save')).toBeDisabled();
  expect({ writes, reads, savedRemark: saved.remark }).toEqual({ writes: 1, reads: 2, savedRemark: 'Original' });
});

test('SD-ORDER-EDIT-011 timed-out write retains input and prevents a duplicate PUT', async ({ page }) => {
  let writes = 0;
  const saved = { id: 89, documentNo: 'SO-0089', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/89', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved });
    writes++;
    return new Promise<void>(() => {});
  });
  await page.goto('/sal/orders/89/edit');
  await page.getByTestId('sd-order-create-remark').fill('Unsaved revision');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible({ timeout: 10000 });
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Unsaved revision');
  await expect(page.getByTestId('sd-order-create-save')).toBeDisabled();
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await capture(page, 'write-timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'write-timeout-dark-zh-Hant');
  expect({ writes, savedRemark: saved.remark, version: saved.version }).toEqual({ writes: 1, savedRemark: 'Original', version: 1 });
});

test('SD-ORDER-EDIT-005/011 disconnected saved read shows a retry without an editable form', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: net::ERR_INTERNET_DISCONNECTED/);
  let reads = 0;
  let writes = 0;
  const saved = { id: 90, documentNo: 'SO-0090', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/90', route => {
    if (route.request().method() === 'PUT') { writes++; return route.fulfill({ json: saved }); }
    reads++;
    return reads === 1 ? route.abort('internetdisconnected') : route.fulfill({ json: saved });
  });
  await page.goto('/sal/orders/90/edit');
  await expect(page.getByTestId('sd-order-edit-load-error')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
  await capture(page, 'read-network-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'read-network-dark-zh-Hant');
  await page.getByTestId('sd-order-edit-retry').click();
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Original');
  expect({ reads, writes }).toEqual({ reads: 2, writes: 0 });
});

test('SD-ORDER-EDIT-011 disconnected write retains input and prevents a duplicate PUT', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: net::ERR_INTERNET_DISCONNECTED/);
  let writes = 0;
  const saved = { id: 91, documentNo: 'SO-0091', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/91', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved });
    writes++;
    return route.abort('internetdisconnected');
  });
  await page.goto('/sal/orders/91/edit');
  await page.getByTestId('sd-order-create-remark').fill('Unsaved revision');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-uncertain')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Unsaved revision');
  await expect(page.getByTestId('sd-order-create-save')).toBeDisabled();
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await capture(page, 'write-network-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'write-network-dark-zh-Hant');
  expect({ writes, savedRemark: saved.remark, version: saved.version }).toEqual({ writes: 1, savedRemark: 'Original', version: 1 });
});

test('SD-ORDER-EDIT-005/007 hidden order returns 404 without exposing saved details or a write', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let reads = 0;
  let writes = 0;
  await page.route('**/api/v1/sal/orders/92', route => {
    if (route.request().method() === 'PUT') { writes++; return route.fulfill({ status: 404 }); }
    reads++;
    return route.fulfill({ status: 404, json: { errorCode: 'SAL.ORDER.NOT_FOUND' } });
  });
  await page.goto('/sal/orders/92/edit');
  await expect(page.getByTestId('sd-order-edit-load-error')).toBeVisible();
  await expect(page.getByTestId('sd-order-edit-load-error')).toContainText('找不到這張訂單');
  await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await capture(page, 'hidden-404-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'hidden-404-dark-zh-Hant');
  expect({ reads, writes }).toEqual({ reads: 1, writes: 0 });
});

test('SD-ORDER-EDIT-008 blocks pending, cancelled, converted and delivered orders without a PUT', async ({ page }) => {
  let writes = 0;
  let status = 'Pending';
  let sourceQuotationId: number | null = null;
  let deliveredQuantity = 0;
  const saved = () => ({ id: 93, documentNo: 'SO-0093', status, version: 2,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 2, unitPrice: 3,
      discountRate: 0, taxRateId: 4, deliveredQuantity, pendingQuantity: 0 }] });
  await page.route('**/api/v1/sal/orders/93', route => {
    if (route.request().method() === 'PUT') writes++;
    return route.fulfill({ json: saved() });
  });
  for (const state of ['Pending', 'Cancelled', 'converted', 'delivered']) {
    status = state === 'converted' || state === 'delivered' ? 'Draft' : state;
    sourceQuotationId = state === 'converted' ? 17 : null;
    deliveredQuantity = state === 'delivered' ? 1 : 0;
    await page.goto('/sal/orders/93/edit');
    await expect(page.getByTestId('sd-order-edit-blocked')).toBeVisible();
    const reason: Record<string, string> = { Pending: '正在審批', Cancelled: '已取消',
      converted: '來源報價', delivered: '已有交貨' };
    await expect(page.getByTestId('sd-order-edit-blocked')).toContainText(reason[state]);
    await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
    await capture(page, `blocked-${state}-light-zh-Hant`);
    await chooseTheme(page, 'dark');
    await capture(page, `blocked-${state}-dark-zh-Hant`);
    await chooseTheme(page, 'light');
    if (state === 'Pending') {
      await chooseLanguage(page, 'en');
      await expect(page.getByTestId('sd-order-edit-blocked')).toContainText('pending approval');
      await capture(page, 'blocked-Pending-light-en');
      await chooseLanguage(page, 'zh-Hans');
      await expect(page.getByTestId('sd-order-edit-blocked')).toContainText('正在审批');
      await capture(page, 'blocked-Pending-light-zh-Hans');
      await chooseLanguage(page, 'zh-Hant');
    }
  }
  expect(writes).toBe(0);
});

test('SD-ORDER-EDIT-008 a status change after GET is rejected without a success receipt', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let writes = 0;
  let storedStatus = 'Draft';
  const saved = () => ({ id: 95, documentNo: 'SO-0095', status: storedStatus, version: storedStatus === 'Draft' ? 1 : 2,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] });
  await page.route('**/api/v1/sal/orders/95', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved() });
    writes++;
    storedStatus = 'Pending';
    return route.fulfill({ status: 422, json: { errorCode: 'SAL.ORDER.INVALID_STATUS',
      errors: { status: ['Order is pending approval'] } } });
  });
  await page.goto('/sal/orders/95/edit');
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Original');
  await page.getByTestId('sd-order-create-remark').fill('Unapproved change');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-error')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-error')).toContainText('正在審批');
  await expect(page.getByTestId('sd-order-edit-conflict')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-save')).toBeDisabled();
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Unapproved change');
  expect({ writes, storedStatus, storedRemark: saved().remark }).toEqual({
    writes: 1, storedStatus: 'Pending', storedRemark: 'Original',
  });
  await capture(page, 'status-changed-422-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'status-changed-422-dark-zh-Hant');
  await page.getByTestId('sd-order-edit-refresh').click();
  await expect(page.getByTestId('sd-order-edit-blocked')).toContainText('正在審批');
  await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
  expect(writes).toBe(1);
});

test.describe('SD-ORDER-EDIT-007 requires VIEW as well as EDIT', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'SAL.ORDER.EDIT', dataScope: 'All' }] } });
  test('blocks the route before a saved read or PUT', async ({ page }) => {
    let reads = 0;
    let writes = 0;
    await page.route('**/api/v1/sal/orders/94', route => {
      if (route.request().method() === 'PUT') writes++;
      else reads++;
      return route.fulfill({ status: 403, json: { errorCode: 'COMMON.PERMISSION.DENIED' } });
    });
    await page.goto('/sal/orders/94/edit');
    await expect(page).toHaveURL(/\/forbidden$/);
    await expect(page.getByText('沒有權限')).toBeVisible();
    await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    await capture(page, 'no-view-light-zh-Hant');
    await chooseTheme(page, 'dark');
    await capture(page, 'no-view-dark-zh-Hant');
    expect({ reads, writes }).toEqual({ reads: 0, writes: 0 });
  });
});

test('SD-ORDER-EDIT-007 list offers Edit only for visible editable statuses', async ({ page }) => {
  const items = ['Draft', 'Rejected', 'Pending', 'Approved', 'Cancelled'].map((status, index) => ({
    id: 101 + index, documentNo: `SO-0${101 + index}`, customerName: 'Customer Seven',
    businessDate: '2028-02-29', originalGross: 2, status, version: 1,
  }));
  await page.route('**/api/v1/sal/orders**', route => route.fulfill({
    json: { items, totalCount: items.length, page: 1, pageSize: 20 },
  }));
  await page.goto('/sal/orders');
  await expect(page.getByTestId('sd-order-row-101')).toContainText('SO-0101');
  await expect(page.getByTestId('sd-order-edit-101')).toHaveAttribute('href', '/sal/orders/101/edit');
  await expect(page.getByTestId('sd-order-edit-102')).toHaveAttribute('href', '/sal/orders/102/edit');
  for (const id of [103, 104, 105]) await expect(page.getByTestId(`sd-order-edit-${id}`)).toHaveCount(0);
});

test.describe('SD-ORDER-EDIT-007 list without EDIT', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'SAL.ORDER.VIEW', dataScope: 'All' }] } });
  test('omits Edit link from a visible Draft', async ({ page }) => {
    await page.route('**/api/v1/sal/orders**', route => route.fulfill({ json: {
      items: [{ id: 106, documentNo: 'SO-0106', customerName: 'Customer Seven',
        businessDate: '2028-02-29', originalGross: 2, status: 'Draft', version: 1 }],
      totalCount: 1, page: 1, pageSize: 20,
    } }));
    await page.goto('/sal/orders');
    await expect(page.getByTestId('sd-order-row-106')).toContainText('SO-0106');
    await expect(page.getByTestId('sd-order-edit-106')).toHaveCount(0);
  });
});

test('SD-ORDER-EDIT-007 company switch removes the prior company draft and never writes it', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  const reads: string[] = [];
  const writes: string[] = [];
  const saved = { id: 107, documentNo: 'SO-0107', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Home company only', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/107', route => {
    const company = route.request().headers()['x-company-id'] ?? '';
    if (route.request().method() === 'PUT') { writes.push(company); return route.fulfill({ status: 404 }); }
    reads.push(company);
    return company === '1' ? route.fulfill({ json: saved })
      : route.fulfill({ status: 404, json: { errorCode: 'SAL.ORDER.NOT_FOUND' } });
  });
  await page.goto('/sal/orders/107/edit');
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Home company only');
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('sd-order-edit-load-error')).toContainText('找不到這張訂單');
  await expect(page.getByTestId('sd-order-create-form')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  expect(reads[0]).toBe('1');
  expect(reads.slice(1).length).toBeGreaterThan(0);
  expect(reads.slice(1).every(company => company === '2')).toBe(true);
  expect(writes).toEqual([]);
  await capture(page, 'cross-company-404-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'cross-company-404-dark-zh-Hant');
});

test('SD-ORDER-EDIT-007 a readable order outside EDIT scope cannot be changed', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  let writes = 0;
  const saved = { id: 113, documentNo: 'SO-0113', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Outside edit scope', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/113', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved });
    writes++;
    return route.fulfill({ status: 404, json: { errorCode: 'SAL.ORDER.NOT_FOUND' } });
  });
  await page.goto('/sal/orders/113/edit');
  await page.getByTestId('sd-order-create-remark').fill('Should stay unsaved');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-edit-unavailable')).toContainText('修改資料範圍');
  await expect(page.getByTestId('sd-order-create-error')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Should stay unsaved');
  await expect(page.getByTestId('sd-order-create-save')).toBeDisabled();
  await expect(page.getByTestId('sd-order-edit-refresh')).toBeVisible();
  await page.getByTestId('sd-order-create-save').click({ force: true });
  expect({ writes, savedRemark: saved.remark, version: saved.version }).toEqual({
    writes: 1, savedRemark: 'Outside edit scope', version: 1,
  });
  await capture(page, 'edit-scope-404-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'edit-scope-404-dark-zh-Hant');
});

test('SD-ORDER-EDIT-009 a changed business date rejected after GET keeps the saved date', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let writes = 0;
  const saved = { id: 114, documentNo: 'SO-0114', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/114', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved });
    writes++;
    return route.fulfill({ status: 422, json: { errorCode: 'SAL.ORDER.REFERENCE.INVALID',
      errors: { businessDate: ['Date is no longer available'] } } });
  });
  await page.goto('/sal/orders/114/edit');
  await page.getByTestId('sd-order-create-date').fill('2028-03-01');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-error')).toContainText('日期');
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-order-create-date')).toHaveValue('2028-03-01');
  expect({ writes, savedDate: saved.businessDate, version: saved.version }).toEqual({
    writes: 1, savedDate: '2028-02-29', version: 1,
  });
  await capture(page, 'date-changed-422-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'date-changed-422-dark-zh-Hant');
});

test('SD-ORDER-EDIT-002 rejects empty date, overlong remark and excess decimal places before PUT', async ({ page }) => {
  let writes = 0;
  const saved = { id: 115, documentNo: 'SO-0115', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/115', route => {
    if (route.request().method() === 'PUT') writes++;
    return route.fulfill({ json: saved });
  });
  await page.goto('/sal/orders/115/edit');
  await page.getByTestId('sd-order-create-date').fill('');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByText('請輸入有效的單據日期。')).toBeVisible();
  await page.getByTestId('sd-order-create-date').fill('2028-02-29');
  await page.getByTestId('sd-order-create-remark').fill('x'.repeat(501));
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByText('備註不得超過 500 字。')).toBeVisible();
  await page.getByTestId('sd-order-create-remark').fill('Revised');
  await page.getByTestId('sd-order-create-quantity-0').fill('1.0000001');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByText('數量須大於零、低於 10^13，最多 6 位小數。')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  expect({ writes, version: saved.version, savedRemark: saved.remark }).toEqual({ writes: 0, version: 1, savedRemark: 'Original' });
});

test('SD-ORDER-EDIT-003/012 confirms every edited commercial field in a fresh saved receipt', async ({ page }) => {
  let writes = 0;
  let putBody = '';
  const saved = { id: 108, documentNo: 'SO-0108', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/108', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved });
    writes++;
    putBody = route.request().postData() ?? '';
    const body = JSON.parse(putBody) as { version: number; draft: { businessDate: string; currencyCode: string;
      remark: string; lines: { quantity: number; unitPrice: number; discountRate: number }[] } };
    expect(body.version).toBe(1);
    saved.businessDate = body.draft.businessDate;
    saved.currencyCode = body.draft.currencyCode;
    saved.remark = body.draft.remark;
    saved.lines[0].quantity = body.draft.lines[0].quantity;
    saved.lines[0].unitPrice = body.draft.lines[0].unitPrice;
    saved.lines[0].discountRate = body.draft.lines[0].discountRate;
    saved.version = 2;
    return route.fulfill({ json: saved });
  });
  await page.goto('/sal/orders/108/edit');
  await page.getByTestId('sd-order-create-date').fill('2028-03-01');
  await page.getByTestId('sd-order-create-currency').fill('HKD');
  await page.getByTestId('sd-order-create-remark').fill('Revised');
  await page.getByTestId('sd-order-create-quantity-0').fill('1.000001');
  await page.getByTestId('sd-order-create-price-0').fill('0.000001');
  await page.getByTestId('sd-order-create-discount-0').fill('0.25');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0108');
  await expect(page.getByTestId('sd-order-create-receipt')).toContainText('Customer Seven');
  expect(writes).toBe(1);
  expect(putBody).toContain('1.000001');
  expect(putBody).toContain('0.000001');
  expect(saved).toMatchObject({ version: 2, businessDate: '2028-03-01', currencyCode: 'HKD', remark: 'Revised',
    lines: [{ lineNo: 1, itemId: 9, quantity: 1.000001, unitPrice: 0.000001, discountRate: 0.25, taxRateId: 4 }] });
  await capture(page, 'commercial-receipt-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'commercial-receipt-dark-zh-Hant');
});

test('SD-ORDER-EDIT-002 browser caps a loaded draft at 1000 lines and rejects incomplete replacement', async ({ page }) => {
  test.setTimeout(60_000);
  let writes = 0;
  const saved = { id: 109, documentNo: 'SO-0109', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/sal/orders/109', route => {
    if (route.request().method() === 'PUT') writes++;
    return route.fulfill({ json: saved });
  });
  await page.goto('/sal/orders/109/edit');
  await expect(page.getByTestId('sd-order-create-add-line')).toBeVisible();
  await page.evaluate(() => {
    const button = document.querySelector<HTMLButtonElement>('[data-testid="sd-order-create-add-line"]');
    if (!button) throw new Error('Add-line button is missing');
    for (let index = 1; index < 1000; index++) button.click();
  });
  await expect(page.getByTestId('sd-order-create-item-pick-999')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-add-line')).toBeDisabled();
  await expect(page.getByTestId('sd-order-create-line-limit')).toBeVisible();
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-errors')).toBeVisible();
  await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
  expect({ writes, version: saved.version, lineCount: saved.lines.length }).toEqual({ writes: 0, version: 1, lineCount: 1 });
});

for (const reference of [
  { name: 'item', field: 'lines[0].itemId', label: '物料' },
  { name: 'tax', field: 'lines[0].taxRateId', label: '銷項稅率' },
  { name: 'currency', field: 'currencyCode', label: '幣別代碼' },
] as const) {
  test(`SD-ORDER-EDIT-009 invalid ${reference.name} reference names its field and leaves the saved draft intact`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
    let writes = 0;
    const saved = { id: 110, documentNo: 'SO-0110', status: 'Draft', version: 1,
      customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
      currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
        itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
        discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
    await page.route('**/api/v1/sal/orders/110', route => {
      if (route.request().method() === 'GET') return route.fulfill({ json: saved });
      writes++;
      return route.fulfill({ status: 422, json: { errorCode: 'SAL.ORDER.REFERENCE.INVALID',
        errors: { [reference.field]: ['Reference is no longer available'] } } });
    });
    await page.goto('/sal/orders/110/edit');
    await page.getByTestId('sd-order-create-remark').fill('Unsaved revision');
    await page.getByTestId('sd-order-create-save').click();
    await expect(page.getByTestId('sd-order-create-error')).toContainText(reference.label);
    await expect(page.getByTestId('sd-order-create-success')).toHaveCount(0);
    await expect(page.getByTestId('sd-order-create-remark')).toHaveValue('Unsaved revision');
    expect({ writes, savedRemark: saved.remark, version: saved.version }).toEqual({ writes: 1, savedRemark: 'Original', version: 1 });
    await capture(page, `reference-${reference.name}-422-light-zh-Hant`);
    await chooseTheme(page, 'dark');
    await capture(page, `reference-${reference.name}-422-dark-zh-Hant`);
  });
}

test('SD-ORDER-EDIT-012 confirms selected customer, item and tax identities in the saved receipt', async ({ page }) => {
  let writes = 0;
  const saved = { id: 111, documentNo: 'SO-0111', status: 'Draft', version: 1,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark: 'Original', lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: 1, unitPrice: 2,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] };
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: { items: [
    { id: 8, code: 'C-8', name: 'Customer Eight', isActive: true, reviewStatus: 'Approved' }], totalCount: 1 } }));
  await page.route('**/api/v1/md/items**', route => route.fulfill({ json: { items: [
    { id: 10, code: 'I-10', name: 'Item Ten', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/tax-codes**', route => route.fulfill({ json: { items:
    route.request().url().includes('/rates') ? [{ id: 5, taxCodeId: 3, effectiveFrom: '2028-01-01', ratePercent: 8 }]
      : [{ id: 3, code: 'OUT8', name: 'Output VAT 8', taxType: 'Output', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/sal/orders/111', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: saved });
    writes++;
    const body = JSON.parse(route.request().postData() ?? '{}') as { draft: { customerId: number;
      lines: { itemId: number; taxRateId: number }[] } };
    expect(body.draft.customerId).toBe(8);
    expect(body.draft.lines[0]).toMatchObject({ itemId: 10, taxRateId: 5 });
    saved.customerId = 8; saved.customerCode = 'C-8'; saved.customerName = 'Customer Eight';
    saved.lines[0].itemId = 10; saved.lines[0].itemCode = 'I-10'; saved.lines[0].itemName = 'Item Ten';
    saved.lines[0].taxRateId = 5; saved.version = 2;
    return route.fulfill({ json: saved });
  });
  await page.goto('/sal/orders/111/edit');
  await page.getByTestId('sd-order-create-customer-pick').click();
  await page.getByText('C-8', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-item-pick-0').click();
  await page.getByText('I-10', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-tax-pick-0').click();
  await page.getByText('OUT8', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByText('8%', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0111');
  await expect(page.getByTestId('sd-order-create-receipt')).toContainText('Customer Eight');
  expect({ writes, version: saved.version, customerId: saved.customerId, itemId: saved.lines[0].itemId,
    taxRateId: saved.lines[0].taxRateId }).toEqual({ writes: 1, version: 2, customerId: 8, itemId: 10, taxRateId: 5 });
  await capture(page, 'picker-receipt-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'picker-receipt-dark-zh-Hant');
});

test('SD-ORDER-EDIT-003 keeps a DECIMAL(19,6) boundary token through GET, PUT and saved receipt', async ({ page }) => {
  const boundary = '9999999999999.999999';
  let version = 1;
  let remark = 'Original';
  let writes = 0;
  let putBody = '';
  const body = () => JSON.stringify({ id: 112, documentNo: 'SO-0112', status: 'Draft', version,
    customerId: 7, customerCode: 'C-7', customerName: 'Customer Seven', businessDate: '2028-02-29',
    currencyCode: 'TWD', sourceQuotationId: null, remark, lines: [{ id: 1, lineNo: 1,
      itemId: 9, itemCode: 'I-9', itemName: 'Item Nine', quantity: '__QUANTITY__', unitPrice: 0.000001,
      discountRate: 0, taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] })
    .replace('"__QUANTITY__"', boundary);
  await page.route('**/api/v1/sal/orders/112', route => {
    if (route.request().method() === 'GET') return route.fulfill({ body: body(), contentType: 'application/json' });
    writes++;
    putBody = route.request().postData() ?? '';
    remark = 'Revised'; version = 2;
    return route.fulfill({ body: body(), contentType: 'application/json' });
  });
  await page.goto('/sal/orders/112/edit');
  await expect(page.getByTestId('sd-order-create-quantity-0')).toHaveValue(boundary);
  await page.getByTestId('sd-order-create-remark').fill('Revised');
  await page.getByTestId('sd-order-create-save').click();
  await expect(page.getByTestId('sd-order-create-success')).toContainText('SO-0112');
  expect(putBody).toContain(`"quantity":${boundary}`);
  expect({ writes, version, remark }).toEqual({ writes: 1, version: 2, remark: 'Revised' });
  await capture(page, 'decimal-boundary-light-zh-Hant');
  await chooseTheme(page, 'dark');
  await capture(page, 'decimal-boundary-dark-zh-Hant');
});
