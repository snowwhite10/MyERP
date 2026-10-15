import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { mockInventoryOptions } from '../support/inventory-selection';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const screenshots = resolve(fileURLToPath(new URL('.', import.meta.url)), '../../qa/INV/screenshots');
async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  await mkdir(screenshots, { recursive: true });
  await page.screenshot({ path: resolve(screenshots, `5.20-${name}.png`), fullPage: true, animations: 'disabled' });
}

test('INV-SEL-B3 transfer keeps cancelled choices, shows inactive history and MD guard reason', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await mockInventoryOptions(page, { items: [{ id: 9, code: 'NEW', name: 'Current item', trackingMode: 'None' }],
    warehouses: [{ id: 4, code: 'WH', name: 'Main' }], locations: [] });
  let writes = 0;
  await page.route('**/api/v1/inv/transfers/7**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/saved-references')) return route.fulfill({ json: {
      items: [{ id: 3, code: 'OLD', name: 'Inactive item', trackingMode: 'Lot', isActive: false }],
      warehouses: [{ id: 4, code: 'WH', name: 'Main' }, { id: 6, code: 'OUT', name: 'Out' }], locations: [] } });
    if (route.request().method() === 'PUT') { writes++;
      return route.fulfill({ status: 422, json: { errorCode: 'MD.ITEM.INACTIVE' } }); }
    return route.fulfill({ json: { id: 7, businessDate: '2026-10-14', remark: '', version: 1,
      lines: [{ itemId: 3, sourceWarehouseId: 4, sourceLocationId: null, destinationWarehouseId: 6,
        destinationLocationId: null, quantity: 1, tracking: [] }] } });
  });
  await page.goto('/inv/transfers/7/edit');
  const line = page.getByTestId('inv-transfer-line-0');
  await expect(line.getByRole('button', { name: '物料' })).toContainText('OLD · Inactive item (已停用)');
  await line.getByRole('button', { name: '物料' }).click();
  await expect(page.getByTestId('erp-select-row-3')).toHaveCount(0);
  await page.getByTestId('erp-select-row-9').click();
  await page.keyboard.press('Escape');
  await expect(line.getByRole('button', { name: '物料' })).toContainText('OLD · Inactive item');
  await page.getByTestId('inv-transfer-save').click();
  await expect(page.getByTestId('inv-transfer-error')).toContainText('此物料已停用');
  expect(writes).toBe(1);
  await capture(page, 'transfer-inactive-error-light-zh-Hant');
});

test('INV-SEL-B3 transfer and opening selection stays legible in three languages and dark mode', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 3, code: 'ITEM', name: 'Bolt', trackingMode: 'None' }],
    warehouses: [{ id: 4, code: 'WH', name: 'Main' }], locations: [] });
  for (const [path, name] of [['/inv/transfers/new', 'transfer'], ['/inv/opening-stocks/new', 'opening']] as const) {
    await page.goto(path);
    const line = page.getByTestId(name === 'transfer' ? 'inv-transfer-line-0' : 'inv-opening-line-0');
    await expect(line.getByRole('button', { name: name === 'transfer' ? '調出儲位' : '儲位', exact: true })).toBeDisabled();
    await line.getByRole('button', { name: '物料' }).click();
    await expect(page.getByTestId('erp-select-title')).toHaveText('選擇物料');
    await capture(page, `${name}-normal-light-zh-Hant`);
    await page.getByTestId('erp-select-cancel').click();
    await chooseTheme(page, 'dark');
    await line.getByRole('button', { name: '物料' }).click();
    await capture(page, `${name}-normal-dark-zh-Hant`);
    await page.getByTestId('erp-select-cancel').click();
    await chooseLanguage(page, 'zh-Hans');
    await line.getByRole('button', { name: '物料' }).click();
    await expect(page.getByTestId('erp-select-title')).toHaveText('选择物料');
    await capture(page, `${name}-normal-dark-zh-Hans`);
    await page.getByTestId('erp-select-cancel').click();
    await chooseLanguage(page, 'en');
    await line.getByRole('button', { name: 'Item', exact: true }).click();
    await expect(page.getByTestId('erp-select-title')).toHaveText('Select item');
    await capture(page, `${name}-normal-dark-en`);
    await page.getByTestId('erp-select-cancel').click();
    await chooseLanguage(page, 'zh-Hant');
    await chooseTheme(page, 'light');
  }
});
