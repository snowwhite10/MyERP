import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const balance = { itemId: 3, warehouseId: 4, locationId: 5, quantity: 12.5 };
const movement = { id: 9, itemId: 3, warehouseId: 4, locationId: 5, quantity: -2, businessDate: '2026-10-12',
  postedAt: '2026-10-12T10:00:00+08:00', sourceType: 'OtherIssue', sourceId: 7, sourceLineNo: 1, direction: 'Out',
  itemCode: 'ITEM', warehouseCode: 'WH', locationCode: 'A1', unitCost: 2.5 };

async function mockMasterData(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: '@ITEM', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [
    { id: 5, code: 'A1', name: 'Shelf A1', isActive: true }], totalCount: 1 } }));
}

test('INV-QRY-004 shows balances and filtered movements with MD names', async ({ page }) => {
  await mockMasterData(page);
  const movementQueries: URLSearchParams[] = [];
  await page.route('**/api/v1/inv/balances?**', route => route.fulfill({ json: { items: [balance], totalCount: 1 } }));
  await page.route('**/api/v1/inv/movements?**', route => {
    movementQueries.push(new URL(route.request().url()).searchParams);
    return route.fulfill({ json: { items: [movement], totalCount: 1 } });
  });
  await page.goto('/inv/stock-query');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('庫存查詢');
  const table = page.getByTestId('inv-query-table');
  await expect(table).toContainText('@ITEM · Bolt');
  await expect(table).toContainText('A1 · Shelf A1');
  await expect(table).toContainText('12.5');
  await page.getByTestId('inv-query-movements').click();
  await expect(table).toContainText('OtherIssue #7/1');
  await page.getByRole('combobox', { name: '倉庫' }).selectOption({ label: 'WH · Main' });
  await page.getByRole('combobox', { name: '儲位' }).selectOption({ label: 'A1 · Shelf A1' });
  await page.getByLabel('起始日').fill('2026-10-01');
  await page.getByLabel('截止日').fill('2026-10-13');
  await page.getByRole('combobox', { name: '方向' }).selectOption({ label: '出庫' });
  await page.getByTestId('inv-query-search').click();
  await expect.poll(() => movementQueries.at(-1)?.toString()).toContain('locationId=5');
  const last = movementQueries.at(-1)!;
  expect(last.get('warehouseId')).toBe('4');
  expect(last.get('from')).toBe('2026-10-01');
  expect(last.get('to')).toBe('2026-10-13');
  expect(last.get('direction')).toBe('Out');
});

test('INV-QRY-004 rejects an inverted date range before calling the API', async ({ page }) => {
  await mockMasterData(page);
  let movementCalls = 0;
  await page.route('**/api/v1/inv/balances?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route('**/api/v1/inv/movements?**', route => { movementCalls++; return route.fulfill({ json: { items: [], totalCount: 0 } }); });
  await page.goto('/inv/stock-query');
  await page.getByTestId('inv-query-movements').click();
  await expect.poll(() => movementCalls).toBe(1);
  await page.getByLabel('起始日').fill('2026-10-13');
  await page.getByLabel('截止日').fill('2026-10-01');
  await page.getByTestId('inv-query-search').click();
  await expect(page.getByText('截止日不可早於起始日。')).toBeVisible();
  expect(movementCalls).toBe(1);
});

test('INV-QRY-004 exports a formula-safe CSV and prints every filtered row', async ({ page }) => {
  await page.addInitScript(() => { window.print = () => sessionStorage.setItem('stock-printed', 'yes'); });
  await mockMasterData(page);
  await page.route('**/api/v1/inv/balances?**', route => route.fulfill({ json: { items: [balance], totalCount: 1 } }));
  await page.route('**/api/v1/inv/movements?**', route => route.fulfill({ json: { items: [movement], totalCount: 1 } }));
  await page.goto('/inv/stock-query');
  await page.getByTestId('inv-query-movements').click();
  await expect(page.getByTestId('inv-query-table')).toContainText('OtherIssue #7/1');
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('inv-query-export').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('inventory-movements.csv');
  const stream = await download.createReadStream();
  const chunks: Buffer[] = [];
  for await (const chunk of stream) chunks.push(chunk as Buffer);
  const csv = Buffer.concat(chunks).toString('utf8');
  expect(csv).toContain('"\'@ITEM · Bolt"');
  expect(csv).toContain('"-2"');
  expect(csv).toContain('"OtherIssue"');
  await page.getByRole('button', { name: '列印' }).click();
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('stock-printed'))).toBe('yes');
});

test.describe('INV-QRY-004 movement-only access', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.MOVEMENT.VIEW', dataScope: 'Company' }] } });
  test('hides the balance tab and opens movements directly', async ({ page }) => {
    let balanceCalls = 0;
    await page.route('**/api/v1/md/**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await page.route('**/api/v1/inv/balances?**', route => { balanceCalls++; return route.fulfill({ json: { items: [], totalCount: 0 } }); });
    await page.route('**/api/v1/inv/movements?**', route => route.fulfill({ json: { items: [movement], totalCount: 1 } }));
    await page.goto('/inv/stock-query');
    await expect(page.getByTestId('inv-query-table')).toContainText('OtherIssue #7/1');
    await expect(page.getByTestId('inv-query-balances')).toHaveCount(0);
    await expect(page.getByTestId('inv-query-movements')).toBeVisible();
    expect(balanceCalls).toBe(0);
  });
});

test.describe('INV-QRY-004 without stock permissions', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.OTHER_RECEIPT.VIEW', dataScope: 'Company' }] } });
  test('guards the route and hides the menu entry', async ({ page }) => {
    await page.goto('/inv/stock-query');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    await expect(page.getByTestId('layout-menu-inv-stock-query')).toHaveCount(0);
  });
});

test('INV-QRY-004 keeps labels readable in three languages and dark mode', async ({ page }) => {
  await mockMasterData(page);
  await page.route('**/api/v1/inv/balances?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/stock-query');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('庫存查詢');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Stock query');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('库存查询');
  await chooseTheme(page, 'dark');
  await expect(page.locator('link#erp-theme-dark')).toHaveAttribute('href', 'theme-dark.css');
  await expect(page.getByTestId('inv-query-table')).toBeVisible();
});
