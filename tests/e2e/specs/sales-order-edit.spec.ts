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
