import { mockInventoryOptions, pickInventoryOption } from '../support/inventory-selection';
import { readFile } from 'node:fs/promises';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';
const OUT = '../../tests/qa/INV/screenshots';

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

test('INV-RPT-011 exports complete history while preserving the 1000-row preview', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await masterData(page);
  await page.route('**/api/v1/inv/tracking/lots?**', route => route.fulfill({ json: { items: [
    { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1, manufactureDate: null, expiryDate: null }], totalCount: 1 } }));
  await page.route('**/api/v1/inv/tracking/lots/8', route => route.fulfill({ json: {
    header: { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1 }, locations: [], history: [], historyTruncated: true } }));
  const rows = Array.from({ length: 1001 }, (_, i) => `Lot,L-1,2026-10-13,2026-10-13T12:00:00,${i + 1},INV_OTHER_IN,1,1,In,5,,1.000000`);
  const csv = '\uFEFFTrackingType,TrackingNumber,BusinessDate,PostedAt,MovementId,SourceType,SourceId,SourceLineNo,Direction,WarehouseId,LocationId,Quantity\n' + rows.join('\n') + '\n';
  await page.route('**/api/v1/inv/tracking/lots/8/export', route => route.fulfill({
    headers: { 'Content-Type': 'text/csv; charset=utf-8', 'X-Export-Row-Count': '1001' }, body: csv }));
  await page.goto('/inv/tracking');
  await page.getByTestId('inv-tracking-row-8').click();
  await expect(page.getByTestId('inv-tracking-detail')).toContainText('最近 1000 筆');
  await page.screenshot({ path: `${OUT}/5.23-export-normal-light-zh-Hant.png`, fullPage: true });
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('inv-tracking-export').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('lot-history.csv');
  const downloadedPath = await download.path();
  expect(downloadedPath).not.toBeNull();
  expect((await readFile(downloadedPath!)).toString('utf8').split('\n').length - 1).toBe(1002);
  await expect(page.getByTestId('inv-tracking-notice')).toContainText('1001');
  await page.screenshot({ path: `${OUT}/5.23-exported-light-zh-Hant.png`, fullPage: true });
  await page.route('**/api/v1/inv/tracking/lots/8/export', route => route.fulfill({ status: 422, json: {
    errorCode: 'INV.TRACKING.EXPORT_LIMIT' } }));
  await page.getByTestId('inv-tracking-export').click();
  await expect(page.getByTestId('inv-tracking-error')).toContainText('10,000');
  await page.screenshot({ path: `${OUT}/5.23-limit-light-zh-Hant.png`, fullPage: true });
  await chooseTheme(page, 'dark');
  await page.screenshot({ path: `${OUT}/5.23-limit-dark-zh-Hant.png`, fullPage: true });
  await page.reload();
  await page.getByTestId('inv-tracking-row-8').click();
  await page.screenshot({ path: `${OUT}/5.23-export-normal-dark-zh-Hant.png`, fullPage: true });
  for (const language of ['zh-Hans', 'en'] as const) {
    await chooseLanguage(page, language);
    await page.screenshot({ path: `${OUT}/5.23-export-normal-dark-${language}.png`, fullPage: true });
  }
});

test('INV-RPT-010/012 shows empty, no-results and server failure states in both themes', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allowApi5xx(/\/api\/v1\/inv\/tracking\/lots\/8\/export$/, 500);
  await masterData(page);
  let empty = true;
  await page.route('**/api/v1/inv/tracking/lots?**', route => route.fulfill({ json: empty
    ? { items: [], totalCount: 0 }
    : { items: [{ id: 8, itemId: 3, lotNo: 'L-1', onHand: 1, manufactureDate: null, expiryDate: null }], totalCount: 1 } }));
  await page.route('**/api/v1/inv/tracking/lots/8', route => route.fulfill({ json: {
    header: { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1 }, locations: [], history: [], historyTruncated: false } }));
  await page.route('**/api/v1/inv/tracking/lots/8/export', route => route.fulfill({ status: 500, json: {
    errorCode: 'COMMON.INTERNAL', traceId: 'TRACE-INV-017C' } }));
  await page.goto('/inv/tracking');
  for (const theme of ['light', 'dark'] as const) {
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await expect(page.getByTestId('inv-tracking-table')).toContainText('沒有資料');
    await page.screenshot({ path: `${OUT}/5.23-empty-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByRole('searchbox', { name: '搜尋號碼' }).fill('unknown');
    await page.getByTestId('inv-tracking-search').click();
    await expect(page.getByTestId('inv-tracking-table')).toContainText('沒有符合搜尋條件');
    await page.screenshot({ path: `${OUT}/5.23-no-results-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByRole('searchbox', { name: '搜尋號碼' }).fill('');
    empty = false;
    await page.getByTestId('inv-tracking-search').click();
    await page.getByTestId('inv-tracking-row-8').click();
    await page.getByTestId('inv-tracking-export').click();
    await expect(page.getByTestId('inv-tracking-error')).toBeVisible();
    await expect(page.getByText('TRACE-INV-017C')).toBeVisible();
    await page.screenshot({ path: `${OUT}/5.23-error500-${theme}-zh-Hant.png`, fullPage: true });
    empty = true;
    await page.getByTestId('inv-tracking-search').click();
    await expect(page.getByText('TRACE-INV-017C')).toHaveCount(0);
  }
});

test('INV-RPT-007 cancelling tracking export does not download', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: net::ERR_ABORTED/);
  let release: (() => void) | undefined;
  let downloads = 0;
  page.on('download', () => ++downloads);
  await masterData(page);
  await page.route('**/api/v1/inv/tracking/lots?**', route => route.fulfill({ json: { items: [
    { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1, manufactureDate: null, expiryDate: null }], totalCount: 1 } }));
  await page.route('**/api/v1/inv/tracking/lots/8', route => route.fulfill({ json: {
    header: { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1 }, locations: [], history: [], historyTruncated: false } }));
  await page.route('**/api/v1/inv/tracking/lots/8/export', async route => {
    await new Promise<void>(resolve => { release = resolve; });
    try { await route.fulfill({ status: 200, body: 'unexpected' }); } catch { /* Browser cancelled the request. */ }
  });
  await page.goto('/inv/tracking');
  await page.getByTestId('inv-tracking-row-8').click();
  await page.getByTestId('inv-tracking-export').click();
  await expect(page.getByTestId('inv-tracking-cancel')).toBeVisible();
  await page.getByTestId('inv-tracking-cancel').click();
  release?.();
  await expect(page.getByTestId('inv-tracking-notice')).toContainText('已取消匯出');
  await page.screenshot({ path: `${OUT}/5.23-cancelled-light-zh-Hant.png`, fullPage: true });
  expect(downloads).toBe(0);
});

test('INV-RPT-010 tracking export network error remains retryable in both themes', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  await masterData(page);
  await page.route('**/api/v1/inv/tracking/lots?**', route => route.fulfill({ json: { items: [
    { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1, manufactureDate: null, expiryDate: null }], totalCount: 1 } }));
  await page.route('**/api/v1/inv/tracking/lots/8', route => route.fulfill({ json: {
    header: { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1 }, locations: [], history: [], historyTruncated: false } }));
  await page.route('**/api/v1/inv/tracking/lots/8/export', route => route.abort('failed'));
  await page.goto('/inv/tracking');
  await page.getByTestId('inv-tracking-row-8').click();
  for (const theme of ['light', 'dark'] as const) {
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await page.getByTestId('inv-tracking-export').click();
    await expect(page.getByTestId('inv-tracking-error')).toContainText('無法連線到伺服器');
    await expect(page.getByTestId('inv-tracking-export')).toBeEnabled();
    await page.screenshot({ path: `${OUT}/5.23-network-${theme}-zh-Hant.png`, fullPage: true });
  }
});

test.describe('INV-RPT-012 trace view without export permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: ['INV.TRACKING.VIEW']
      .map(code => ({ code, dataScope: 'Company' })) } });
  test('hides the export action', async ({ page }) => {
    await masterData(page);
    await page.route('**/api/v1/inv/tracking/lots?**', route => route.fulfill({ json: { items: [
      { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1, manufactureDate: null, expiryDate: null }], totalCount: 1 } }));
    await page.route('**/api/v1/inv/tracking/lots/8', route => route.fulfill({ json: {
      header: { id: 8, itemId: 3, lotNo: 'L-1', onHand: 1 }, locations: [], history: [], historyTruncated: false } }));
    await page.goto('/inv/tracking');
    await page.getByTestId('inv-tracking-row-8').click();
    await expect(page.getByTestId('inv-tracking-detail')).toBeVisible();
    await expect(page.getByTestId('inv-tracking-export')).toHaveCount(0);
    await page.screenshot({ path: `${OUT}/5.23-view-only-light-zh-Hant.png`, fullPage: true });
    await chooseTheme(page, 'dark');
    await page.screenshot({ path: `${OUT}/5.23-view-only-dark-zh-Hant.png`, fullPage: true });
  });
});

test.describe('INV-LOT-UI-003 tracking permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [] } });
  test('hides the menu and guards the page', async ({ page }) => {
    await page.goto('/inv/tracking');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    await expect(page.getByTestId('layout-menu-inv-tracking')).toHaveCount(0);
    await page.screenshot({ path: `${OUT}/5.23-no-view-light-zh-Hant.png`, fullPage: true });
    await chooseTheme(page, 'dark');
    await page.screenshot({ path: `${OUT}/5.23-no-view-dark-zh-Hant.png`, fullPage: true });
  });
});
