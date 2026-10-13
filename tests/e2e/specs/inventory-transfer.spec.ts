import { readFile } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const row = { id: 7, documentNo: 'TR-0007', businessDate: '2026-10-14', status: 'Draft',
  remark: '=HYPERLINK("x")', lineCount: 1, totalQuantity: 2, version: 1 };
const line = { id: 9, lineNo: 1, itemId: 3, sourceWarehouseId: 4, sourceLocationId: 5, destinationWarehouseId: 6, destinationLocationId: null,
  quantity: 2, tracking: [{ lotNo: 'LOT-A', serialNo: null, quantity: 2, manufactureDate: null, expiryDate: null }] };

async function mockMasterData(page: Page): Promise<void> {
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'ITEM', name: 'Bolt', isActive: true, trackingMode: 'Lot' }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }, { id: 6, code: 'WH2', name: 'Spare', isActive: true }], totalCount: 2 } }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [
    { id: 5, code: 'A1', name: 'Shelf A1', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/6/locations?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
}

test('INV-TRF-009 creates a lot-tracked transfer between two balance keys and submits it', async ({ page }) => {
  await mockMasterData(page);
  let saved: Record<string, unknown> | null = null;
  let submitted: Record<string, unknown> | null = null;
  await page.route('**/api/v1/inv/transfers**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'POST' && path.endsWith('/submit')) {
      submitted = request.postDataJSON() as Record<string, unknown>; return route.fulfill({ json: { ...row, status: 'PendingApproval', lines: [] } });
    }
    if (request.method() === 'POST') { saved = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 201, json: { ...row, lines: [] } }); }
    return route.fulfill({ json: { items: [row], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/transfers');
  await expect(page.getByTestId('inv-transfer-row-7')).toBeVisible();
  await page.getByTestId('inv-transfer-new').click();
  await page.getByTestId('inv-transfer-date').fill('2026-10-14');
  const form = page.getByTestId('inv-transfer-line-0');
  await form.getByRole('combobox', { name: '物料' }).selectOption({ label: 'ITEM · Bolt' });
  await form.getByRole('combobox', { name: '調出倉庫' }).selectOption({ label: 'WH · Main' });
  await form.getByRole('combobox', { name: '調入倉庫' }).selectOption({ label: 'WH · Main' });
  await form.getByRole('textbox', { name: '數量' }).fill('2.000001');
  await expect(form).toContainText('調出與調入的倉庫及儲位不可完全相同。');
  await form.getByRole('combobox', { name: '調出儲位' }).selectOption({ label: 'A1 · Shelf A1' });
  await expect(form).not.toContainText('不可完全相同');
  await form.getByRole('button', { name: '新增批號' }).click();
  await form.getByRole('textbox', { name: '批號', exact: true }).fill('LOT-A');
  await form.getByRole('textbox', { name: '批號數量' }).fill('2.000001');
  await page.getByTestId('inv-transfer-save').click();
  await expect.poll(() => saved).toMatchObject({ businessDate: '2026-10-14', lines: [{ lineNo: 1, itemId: 3,
    sourceWarehouseId: 4, sourceLocationId: 5, destinationWarehouseId: 4, destinationLocationId: null, quantity: '2.000001',
    tracking: [{ lotNo: 'LOT-A', quantity: '2.000001' }] }] });
  await expect(page.getByTestId('inv-transfer-row-7')).toBeVisible();
  await page.getByTestId('inv-transfer-row-7').getByRole('button', { name: '送審' }).click();
  await expect.poll(() => submitted).toEqual({ version: 1 });
});

test.describe('INV-TRF-009 read only access', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.TRANSFER.VIEW', dataScope: 'Company' }] } });
  test('shows the menu, hides draft actions and guards the create route', async ({ page }) => {
    await page.route('**/api/v1/inv/transfers**', route => route.fulfill({ json: { items: [row], totalCount: 1 } }));
    await page.goto('/inv/transfers');
    await expect(page.getByTestId('inv-transfer-row-7')).toBeVisible();
    await expect(page.getByTestId('layout-menu-inv-transfers')).toHaveCount(1);
    await expect(page.getByTestId('inv-transfer-new')).toHaveCount(0);
    await expect(page.getByTestId('inv-transfer-row-7').getByRole('button', { name: '送審' })).toHaveCount(0);
    await expect(page.getByTestId('inv-transfer-row-7').getByRole('button', { name: '刪除' })).toHaveCount(0);
    await expect(page.getByTestId('inv-transfer-row-7').getByRole('link', { name: '編輯調撥單' })).toHaveCount(0);
    await expect(page.getByTestId('inv-transfer-export')).toBeVisible();
    await page.goto('/inv/transfers/new');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});

test('INV-TRF-009 prints, exports a filtered safe CSV and revises a rejected transfer', async ({ page }) => {
  await page.addInitScript(() => { window.print = () => sessionStorage.setItem('transfer-printed', 'yes'); });
  await mockMasterData(page);
  let updated: Record<string, unknown> | null = null;
  const listQueries: string[] = [];
  const rejected = { ...row, status: 'Rejected', version: 4 };
  await page.route('**/api/v1/inv/transfers**', route => {
    const request = route.request(); const url = new URL(request.url());
    if (request.method() === 'PUT') { updated = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ json: { ...rejected, status: 'Draft', version: 5, lines: [] } }); }
    if (url.pathname.endsWith('/7')) return route.fulfill({ json: { ...rejected, postedAt: null, approval: null, lines: [line] } });
    listQueries.push(url.search);
    return route.fulfill({ json: { items: [rejected], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/transfers');
  await expect(page.getByTestId('inv-transfer-row-7')).toContainText('已駁回');
  await page.getByTestId('inv-transfer-status').selectOption('Rejected');
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('inv-transfer-export').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('inventory-transfers.csv');
  const csv = await readFile((await download.path())!, 'utf8');
  expect(csv).toContain('"TR-0007","2026-10-14","Rejected","1","3","4","5","6","","2","LOT-A × 2"');
  expect(csv).toContain('"\'=HYPERLINK(""x"")"');
  expect(listQueries.some(query => query.includes('status=Rejected'))).toBe(true);
  await page.getByTestId('inv-transfer-row-7').getByRole('button', { name: '列印' }).click();
  const detail = page.getByTestId('inv-transfer-detail');
  await expect(detail).toContainText('WH · Main / A1 · Shelf A1');
  await expect(detail).toContainText('WH2 · Spare / —');
  await expect(detail).toContainText('LOT-A × 2');
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('transfer-printed'))).toBe('yes');
  await page.getByTestId('inv-transfer-row-7').getByRole('link', { name: '編輯調撥單' }).click();
  const quantity = page.getByTestId('inv-transfer-line-0').getByRole('textbox', { name: '數量', exact: true });
  await expect(quantity).toHaveValue('2');
  await quantity.fill('3');
  await page.getByTestId('inv-transfer-line-0').getByRole('textbox', { name: '批號數量' }).fill('3');
  await page.getByTestId('inv-transfer-save').click();
  await expect.poll(() => updated).toMatchObject({ version: 4, lines: [{ quantity: '3', sourceLocationId: 5, destinationWarehouseId: 6,
    tracking: [{ lotNo: 'LOT-A', quantity: '3' }] }] });
});

test('INV-TRF-009 keeps labels readable in three languages and dark mode', async ({ page }) => {
  await page.route('**/api/v1/inv/transfers**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/transfers');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('庫存調撥');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Inventory transfers');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('库存调拨');
  await chooseTheme(page, 'dark');
  await expect(page.locator('link#erp-theme-dark')).toHaveAttribute('href', 'theme-dark.css');
  await expect(page.getByTestId('inv-transfer-table')).toBeVisible();
});

test('INV-TRF-009 keeps the draft after insufficient lot stock on submit', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await page.route('**/api/v1/inv/transfers**', route => {
    const request = route.request();
    if (request.method() === 'POST' && new URL(request.url()).pathname.endsWith('/submit')) {
      return route.fulfill({ status: 422, json: { errorCode: 'INV.LOT.INSUFFICIENT' } });
    }
    return route.fulfill({ json: { items: [row], totalCount: 1 } });
  });
  await page.goto('/inv/transfers');
  await page.getByTestId('inv-transfer-row-7').getByRole('button', { name: '送審' }).click();
  await expect(page.getByTestId('inv-transfer-error')).toBeVisible();
  await expect(page.getByTestId('inv-transfer-row-7')).toContainText('草稿');
  await expect(page.getByTestId('inv-transfer-row-7').getByRole('button', { name: '送審' })).toBeEnabled();
});
