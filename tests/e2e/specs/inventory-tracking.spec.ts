import { mockInventoryOptions, pickInventoryOption } from '../support/inventory-selection';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

async function masterData(page: import('@playwright/test').Page): Promise<void> {
  await mockInventoryOptions(page, { items: [{ id: 3, code: 'LOT', name: 'Lot Item', trackingMode: 'Lot' }, { id: 4, code: 'SER', name: 'Serial Item', trackingMode: 'Serial' }], warehouses: [{ id: 5, code: 'WH', name: 'Main' }], locations: [{ id: 6, code: 'A1', name: 'Shelf A1' }] });
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'LOT', name: 'Lot Item', trackingMode: 'Lot', isActive: true },
    { id: 4, code: 'SER', name: 'Serial Item', trackingMode: 'Serial', isActive: true }], totalCount: 2 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 5, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/5/locations?**', route => route.fulfill({ json: { items: [
    { id: 6, code: 'A1', name: 'Shelf A1', isActive: true }], totalCount: 1 } }));
}

test('INV-LOT-UI-001 captures lot quantities and pasted serials on receipts', async ({ page }) => {
  await masterData(page);
  const sent: unknown[] = [];
  await page.route('**/api/v1/inv/other-receipts', route => {
    sent.push(route.request().postDataJSON());
    return route.fulfill({ status: 201, json: { id: sent.length } });
  });
  await page.route('**/api/v1/inv/other-receipts?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/other-receipts/new');
  await page.getByTestId('inv-receipt-date').fill('2026-10-13');
  const line = page.getByTestId('inv-receipt-line-0');
  await pickInventoryOption(page, line, '物料', 3);
  await pickInventoryOption(page, line, '倉庫', 5);
  await line.getByRole('textbox', { name: '數量' }).fill('2');
  await line.getByRole('button', { name: '新增批號' }).click();
  await line.getByRole('textbox', { name: '批號', exact: true }).fill('L-1');
  await line.getByRole('textbox', { name: '批號數量' }).fill('1');
  await page.getByTestId('inv-receipt-save').click();
  await expect(page.locator('nz-alert')).toContainText('數量合計');
  await line.getByRole('textbox', { name: '批號數量' }).fill('2');
  await page.getByTestId('inv-receipt-save').click();
  await expect.poll(() => sent.length).toBe(1);
  expect((sent[0] as { lines: { tracking: { lotNo: string; quantity: string }[] }[] }).lines[0].tracking)
    .toMatchObject([{ lotNo: 'L-1', quantity: '2' }]);

  await page.goto('/inv/other-receipts/new');
  await page.getByTestId('inv-receipt-date').fill('2026-10-13');
  const serialLine = page.getByTestId('inv-receipt-line-0');
  await pickInventoryOption(page, serialLine, '物料', 4);
  await pickInventoryOption(page, serialLine, '倉庫', 5);
  await serialLine.getByRole('textbox', { name: '數量' }).fill('2');
  await serialLine.getByRole('textbox', { name: '序號（每行一號）' }).fill('S-1\nS-2');
  await page.getByTestId('inv-receipt-save').click();
  await expect.poll(() => sent.length).toBe(2);
  expect((sent[1] as { lines: { tracking: { serialNo: string; quantity: string }[] }[] }).lines[0].tracking)
    .toMatchObject([{ serialNo: 'S-1', quantity: '1' }, { serialNo: 'S-2', quantity: '1' }]);
});

test('INV-LOT-UI-002 searches and traces lots and serials in dark mode', async ({ page }) => {
  await masterData(page);
  const queries: URLSearchParams[] = [];
  await page.route('**/api/v1/inv/tracking/lots?**', route => {
    queries.push(new URL(route.request().url()).searchParams);
    return route.fulfill({ json: { items: [{ id: 8, itemId: 3, lotNo: 'L-1', onHand: 2, manufactureDate: null, expiryDate: null }], totalCount: 1 } });
  });
  await page.route('**/api/v1/inv/tracking/lots/8', route => route.fulfill({ json: { header: { id: 8, itemId: 3, lotNo: 'L-1', onHand: 2 },
    locations: [{ warehouseId: 5, locationId: 6, quantity: 2 }], history: [{ movementId: 9, businessDate: '2026-10-13',
      sourceType: 'OTHER_RECEIPT', sourceId: 7, sourceLineNo: 1, direction: 'In', warehouseId: 5, locationId: 6, quantity: 2 }],
    historyTruncated: true } }));
  await page.route('**/api/v1/inv/tracking/serials?**', route => route.fulfill({ json: { items: [
    { id: 10, itemId: 4, serialNo: 'S-1', inStock: true }], totalCount: 1 } }));
  await page.goto('/inv/tracking');
  await expect(page.getByTestId('inv-tracking-row-8')).toBeVisible();
  await page.getByRole('checkbox', { name: '只列在庫' }).check();
  await page.getByTestId('inv-tracking-search').click();
  await expect.poll(() => queries.at(-1)?.get('inStockOnly')).toBe('true');
  await page.getByTestId('inv-tracking-row-8').click();
  await expect(page.getByTestId('inv-tracking-detail')).toContainText('最近 1000 筆');
  await expect(page.getByTestId('inv-tracking-detail')).toContainText('WH · Main');
  await expect(page.getByTestId('inv-tracking-detail')).toContainText('A1 · Shelf A1');
  await page.getByTestId('inv-tracking-serials').click();
  await expect(page.getByTestId('inv-tracking-row-10')).toContainText('S-1');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Lot and serial trace');
  await chooseTheme(page, 'dark');
  await expect(page.locator('link#erp-theme-dark')).toHaveAttribute('href', 'theme-dark.css');
});

test.describe('INV-LOT-UI-003 tracking permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [] } });
  test('hides the menu and guards the page', async ({ page }) => {
    await page.goto('/inv/tracking');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    await expect(page.getByTestId('layout-menu-inv-tracking')).toHaveCount(0);
  });
});
