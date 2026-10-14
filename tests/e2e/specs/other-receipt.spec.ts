import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { mockInventoryOptions, pickInventoryOption } from '../support/inventory-selection';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const row = { id: 7, documentNo: 'IR-0007', businessDate: '2026-10-13', status: 'Draft',
  remark: 'Opening', lineCount: 1, totalQuantity: 1, totalAmount: 2, version: 1 };
const qaScreenshots = resolve(fileURLToPath(new URL('.', import.meta.url)), '../../qa/INV/screenshots');
async function captureSelection(page: import('@playwright/test').Page, name: string): Promise<void> {
  await mkdir(qaScreenshots, { recursive: true });
  await page.screenshot({ path: resolve(qaScreenshots, `5.20-${name}.png`), fullPage: true, animations: 'disabled' });
}

test('INV-REC-006 creates a draft from MD selections and submits the saved row', async ({ page }) => {
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'ITEM', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [
    { id: 5, code: 'A1', name: 'Shelf A1', isActive: true }], totalCount: 1 } }));
  await mockInventoryOptions(page, { items: [{ id: 3, code: 'ITEM', name: 'Bolt' }], warehouses: [{ id: 4, code: 'WH', name: 'Main' }], locations: [{ id: 5, code: 'A1', name: 'Shelf A1' }] });
  let saved: Record<string, unknown> | null = null;
  let submitted: Record<string, unknown> | null = null;
  await page.route('**/api/v1/inv/other-receipts**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'POST' && path.endsWith('/submit')) {
      submitted = request.postDataJSON() as Record<string, unknown>; return route.fulfill({ json: { ...row, status: 'PendingApproval' } });
    }
    if (request.method() === 'POST') { saved = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 201, json: { ...row, lines: [] } }); }
    if (path.endsWith('/7')) return route.fulfill({ json: { ...row, lines: [] } });
    return route.fulfill({ json: { items: [row], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/other-receipts');
  await expect(page.getByTestId('inv-receipt-row-7')).toBeVisible();
  await page.getByTestId('inv-receipt-new').click();
  await page.getByTestId('inv-receipt-date').fill('2026-10-13');
  const line = page.getByTestId('inv-receipt-line-0');
  await pickInventoryOption(page, line, '物料', 3);
  await pickInventoryOption(page, line, '倉庫', 4);
  await pickInventoryOption(page, line, '儲位', 5);
  await line.getByRole('textbox', { name: '數量' }).fill('1.000001');
  await line.getByRole('textbox', { name: '單價' }).fill('2.500001');
  await page.getByTestId('inv-receipt-save').click();
  await expect.poll(() => saved).toMatchObject({ businessDate: '2026-10-13',
    lines: [{ itemId: 3, warehouseId: 4, locationId: 5, quantity: '1.000001', unitCost: '2.500001' }] });
  await expect(page.getByTestId('inv-receipt-row-7')).toBeVisible();
  await page.getByTestId('inv-receipt-row-7').getByRole('button', { name: '送審' }).click();
  await expect.poll(() => submitted).toEqual({ version: 1 });
});

test.describe('INV-REC-006 read only access', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.OTHER_RECEIPT.VIEW', dataScope: 'Company' }] } });
  test('hides draft actions and guards create route', async ({ page }) => {
    await page.route('**/api/v1/inv/other-receipts**', route => route.fulfill({ json: { items: [row], totalCount: 1 } }));
    await page.goto('/inv/other-receipts');
    await expect(page.getByTestId('inv-receipt-row-7')).toBeVisible();
    await expect(page.getByTestId('inv-receipt-new')).toHaveCount(0);
    await expect(page.getByTestId('inv-receipt-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
    await page.goto('/inv/other-receipts/new');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});

test('INV-REC-006 edits a rejected receipt and exports the filtered register', async ({ page }) => {
  await page.addInitScript(() => { window.print = () => sessionStorage.setItem('receipt-printed', 'yes'); });
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'ITEM', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  let updated: Record<string, unknown> | null = null;
  const rejected = { ...row, status: 'Rejected', version: 4 };
  await page.route('**/api/v1/inv/other-receipts**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path.endsWith('/saved-references')) return route.fulfill({ json: { items: [{ id: 3, code: 'ITEM', name: 'Bolt', trackingMode: 'None', isActive: false }], warehouses: [{ id: 4, code: 'WH', name: 'Main', isActive: false }], locations: [] } });
    if (request.method() === 'PUT') { updated = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ json: { ...rejected, status: 'Draft', version: 5, lines: [] } }); }
    if (path.endsWith('/7')) return route.fulfill({ json: { ...rejected,
      lines: [{ id: 9, lineNo: 1, itemId: 3, warehouseId: 4, locationId: null, quantity: 1, unitCost: 2, amount: 2 }] } });
    return route.fulfill({ json: { items: [rejected], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/other-receipts');
  await expect(page.getByTestId('inv-receipt-row-7')).toContainText('已駁回');
  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: '匯出供 Excel 使用' }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('other-receipts.csv');
  await page.getByTestId('inv-receipt-row-7').getByRole('button', { name: '列印' }).click();
  await expect(page.getByTestId('inv-receipt-detail')).toContainText('ITEM · Bolt');
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('receipt-printed'))).toBe('yes');
  await page.getByTestId('inv-receipt-row-7').getByRole('link', { name: '編輯入庫單' }).click();
  const quantity = page.getByTestId('inv-receipt-line-0').getByRole('textbox', { name: '數量' });
  await expect(quantity).toHaveValue('1');
  await quantity.fill('3');
  await expect(quantity).toHaveValue('3');
  await page.getByTestId('inv-receipt-save').click();
  await expect.poll(() => updated).toMatchObject({ version: 4, lines: [{ quantity: '3', itemId: 3, warehouseId: 4 }] });
});

test('INV-REC-006 keeps labels readable in three languages and dark mode', async ({ page }) => {
  await page.route('**/api/v1/inv/other-receipts**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/other-receipts');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('其他入庫');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Other receipts');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('其他入库');
  await chooseTheme(page, 'dark');
  await expect(page.locator('link#erp-theme-dark')).toHaveAttribute('href', 'theme-dark.css');
  await expect(page.getByTestId('inv-receipt-table')).toBeVisible();
});

test('INV-SEL-004 cancel and Esc keep the receipt draft unchanged', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 3, code: 'ITEM', name: 'Bolt', trackingMode: 'None' }], warehouses: [] });
  await page.goto('/inv/other-receipts/new');
  const line = page.getByTestId('inv-receipt-line-0');
  await expect(line.getByRole('button', { name: '儲位' })).toBeDisabled();
  await expect(line.getByRole('button', { name: '儲位' })).toHaveAttribute('title', '請先選擇倉庫');
  await line.getByRole('button', { name: '物料' }).click();
  await page.getByTestId('erp-select-row-3').click();
  await page.getByTestId('erp-select-cancel').click();
  await expect(line.getByRole('button', { name: '物料' })).toHaveText('—');
  await line.getByRole('button', { name: '物料' }).click();
  await page.getByTestId('erp-select-row-3').click();
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('erp-select-dialog')).toHaveCount(0);
  await expect(line.getByRole('button', { name: '物料' })).toHaveText('—');
});

test('INV-SEL-005 selection stays readable in three languages and dark mode', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 3, code: 'ITEM', name: 'Bolt', trackingMode: 'None' }], warehouses: [] });
  await page.goto('/inv/other-receipts/new');
  const item = page.getByTestId('inv-receipt-line-0').locator('button').first();
  await item.click();
  await expect(page.getByTestId('erp-select-title')).toHaveText('選擇物料');
  await captureSelection(page, 'receipt-normal-light-zh-Hant');
  await page.getByTestId('erp-select-cancel').click();
  await chooseTheme(page, 'dark');
  await item.click();
  await expect(page.getByTestId('erp-select-title')).toHaveText('選擇物料');
  await expect.poll(() => page.getByTestId('erp-select-dialog').evaluate(el => getComputedStyle(el).color)).toBe('rgb(230, 230, 230)');
  await captureSelection(page, 'receipt-normal-dark-zh-Hant');
  await page.getByTestId('erp-select-cancel').click();
  await chooseLanguage(page, 'zh-Hans');
  await item.click();
  await expect(page.getByTestId('erp-select-title')).toHaveText('选择物料');
  await captureSelection(page, 'receipt-normal-dark-zh-Hans');
  await page.getByTestId('erp-select-cancel').click();
  await chooseLanguage(page, 'en');
  await item.click();
  await expect(page.getByTestId('erp-select-title')).toHaveText('Select item');
  await captureSelection(page, 'receipt-normal-dark-en');
});

test('INV-SEL-006 shows a saved inactive reference and the MD rejection reason', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  let writes = 0;
  await mockInventoryOptions(page, { items: [{ id: 9, code: 'NEW', name: 'Active item', trackingMode: 'None' }], warehouses: [] });
  await page.route('**/api/v1/inv/other-receipts/7**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path.endsWith('/saved-references')) return route.fulfill({ json: { items: [{ id: 3, code: 'OLD', name: 'Inactive item', trackingMode: 'None', isActive: false }], warehouses: [{ id: 4, code: 'WH', name: 'Main', isActive: true }], locations: [] } });
    if (request.method() === 'PUT') { writes++;
      return route.fulfill({ status: 422, json: { errorCode: 'MD.ITEM.INACTIVE', title: 'Item inactive' } }); }
    return route.fulfill({ json: { ...row, lines: [{ id: 9, lineNo: 1, itemId: 3, warehouseId: 4, locationId: null, quantity: 1, unitCost: 2, amount: 2, tracking: [] }] } });
  });
  await page.goto('/inv/other-receipts/7/edit');
  const line = page.getByTestId('inv-receipt-line-0');
  await expect(line.getByRole('button', { name: '物料' })).toContainText('OLD · Inactive item (已停用)');
  await line.getByRole('button', { name: '物料' }).click();
  await expect(page.getByTestId('erp-select-row-3')).toHaveCount(0);
  await expect(page.getByTestId('erp-select-row-9')).toBeVisible();
  await page.getByTestId('erp-select-cancel').click();
  await page.getByTestId('inv-receipt-save').click();
  await expect(page.getByTestId('inv-receipt-error')).toContainText('此物料已停用');
  await captureSelection(page, 'receipt-inactive-error-light-zh-Hant');
  await expect(line.getByRole('button', { name: '物料' })).toContainText('OLD · Inactive item (已停用)');
  expect(writes).toBe(1);
});
