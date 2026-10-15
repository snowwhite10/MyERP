import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';
import { mockInventoryOptions, pickInventoryOption } from '../support/inventory-selection';

const summary = { id: 11, requestKey: 'k-1', openingDate: '2026-10-01', remark: '=SUM(A1)', lineCount: 1,
  totalQuantity: 5, totalAmount: 50, postedAt: '2026-10-13T20:00:00', createdAt: '2026-10-13T20:00:00' };
const detail = { ...summary, replayed: false, lines: [
  { id: 1, lineNo: 1, itemId: 3, warehouseId: 4, locationId: 5, quantity: 5, unitCost: 10, amount: 50 }] };

async function mockMasterData(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'ITEM', name: 'Bolt', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [
    { id: 5, code: 'A1', name: 'Shelf A1', isActive: true }], totalCount: 1 } }));
}

test('INV-OPN-007 lists filtered batches with MD names, detail, print and formula-safe CSV', async ({ page }) => {
  await page.addInitScript(() => { window.print = () => sessionStorage.setItem('opening-printed', 'yes'); });
  await mockMasterData(page);
  const queries: URLSearchParams[] = [];
  await page.route('**/api/v1/inv/opening-stocks?**', route => {
    queries.push(new URL(route.request().url()).searchParams);
    return route.fulfill({ json: { items: [summary], totalCount: 1 } });
  });
  await page.route('**/api/v1/inv/opening-stocks/11', route => route.fulfill({ json: detail }));
  await page.goto('/inv/opening-stocks');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('期初庫存');
  await expect(page.getByTestId('inv-opening-row-11')).toContainText('2026-10-01');
  await page.getByTestId('inv-opening-from').fill('2026-09-01');
  await page.getByTestId('inv-opening-to').fill('2026-10-31');
  await page.getByTestId('inv-opening-item').selectOption({ label: 'ITEM · Bolt' });
  await page.getByTestId('inv-opening-search').click();
  await expect.poll(() => queries.at(-1)?.get('itemId')).toBe('3');
  expect(queries.at(-1)!.get('from')).toBe('2026-09-01');
  expect(queries.at(-1)!.get('to')).toBe('2026-10-31');
  await page.getByTestId('inv-opening-row-11').getByRole('button', { name: '2026-10-01' }).click();
  const shown = page.getByTestId('inv-opening-detail');
  await expect(shown).toContainText('ITEM · Bolt');
  await expect(shown).toContainText('A1 · Shelf A1');
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('inv-opening-export').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('opening-stocks.csv');
  const chunks: Buffer[] = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk as Buffer);
  const csv = Buffer.concat(chunks).toString('utf8');
  expect(csv).toContain('"\'=SUM(A1)"');
  expect(csv).toContain('"50"');
  await page.getByTestId('inv-opening-row-11').getByRole('button', { name: '列印' }).click();
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('opening-printed'))).toBe('yes');
});

test('INV-OPN-007 previews line errors, then submits a clean batch once with a stable request key', async ({ page, pageErrors }) => {
  pageErrors.allow(/ERR_FAILED|Failed to load resource/);
  await mockMasterData(page);
  await mockInventoryOptions(page, { items: [{ id: 3, code: 'ITEM', name: 'Bolt', trackingMode: 'None' }],
    warehouses: [{ id: 4, code: 'WH', name: 'Main' }], locations: [{ id: 5, code: 'A1', name: 'Shelf A1' }] });
  let previews = 0; const keys: string[] = [];
  await page.route('**/api/v1/inv/opening-stocks?**', route => route.fulfill({ json: { items: [summary], totalCount: 1 } }));
  await page.route('**/api/v1/inv/opening-stocks/preview', route => {
    previews++;
    const errors = previews === 1 ? [{ field: 'openingDate', code: 'INV.OPENING_STOCK.AFTER_MOVEMENT' }] : [];
    return route.fulfill({ json: { lines: [{ lineNo: 1, amount: 50, errors }], errorCount: errors.length, totalQuantity: 5, totalAmount: 50 } });
  });
  await page.route('**/api/v1/inv/opening-stocks', route => {
    keys.push(route.request().postDataJSON().requestKey);
    return keys.length === 1 ? route.abort('failed') : route.fulfill({ status: 200, json: { ...detail, replayed: true } });
  });
  await page.goto('/inv/opening-stocks');
  await page.getByTestId('inv-opening-new').click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('新增期初批次');
  await page.getByTestId('inv-opening-date').fill('2026-10-01');
  const line = page.getByTestId('inv-opening-line-0');
  await pickInventoryOption(page, line, '物料', 3);
  await pickInventoryOption(page, line, '倉庫', 4);
  await pickInventoryOption(page, line, '儲位', 5);
  await line.getByRole('textbox', { name: '數量' }).fill('5');
  await line.getByRole('textbox', { name: '單價' }).fill('10');
  await expect(page.getByTestId('inv-opening-submit')).toBeDisabled();
  await page.getByTestId('inv-opening-preview-button').click();
  await expect(page.getByTestId('inv-opening-line-errors-0')).toContainText('啟用日期晚於此物料在該倉庫的既有異動');
  await expect(page.getByTestId('inv-opening-submit')).toBeDisabled();
  await page.getByTestId('inv-opening-date').fill('2026-09-01');
  await page.getByTestId('inv-opening-preview-button').click();
  await expect(page.getByTestId('inv-opening-preview')).toContainText('預覽通過');
  await page.getByTestId('inv-opening-submit').click();
  await expect(page.getByTestId('inv-opening-error')).toContainText('無法連線');
  await page.getByTestId('inv-opening-submit').click();
  await expect(page.getByTestId('inv-opening-posted')).toContainText('未重複過帳');
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
});

test.describe('INV-OPN-007 view-only access', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.OPENING_STOCK.VIEW', dataScope: 'Company' }] } });
  test('hides the create button and guards the batch input route', async ({ page }) => {
    await page.route('**/api/v1/md/**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await page.route('**/api/v1/inv/opening-stocks?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await page.goto('/inv/opening-stocks');
    await expect(page.getByTestId('inv-opening-table')).toBeVisible();
    await expect(page.getByTestId('inv-opening-new')).toHaveCount(0);
    await page.goto('/inv/opening-stocks/new');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});

test('INV-OPN-007 keeps labels readable in three languages and dark mode', async ({ page }) => {
  await mockMasterData(page);
  await page.route('**/api/v1/inv/opening-stocks?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.goto('/inv/opening-stocks');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('期初庫存');
  await chooseLanguage(page, 'en');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Opening stock');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('期初库存');
  await chooseTheme(page, 'dark');
  await expect(page.locator('link#erp-theme-dark')).toHaveAttribute('href', 'theme-dark.css');
  await expect(page.getByTestId('inv-opening-table')).toBeVisible();
});
