import type { Page } from '@playwright/test';
import { expect, test } from '../support/fixtures';

const line = { id: 19, lineNo: 1, itemId: 3, warehouseId: 4, locationId: null, trackingMode: 'None',
  bookQuantity: 2, countedQuantity: null, isAdded: false, recountRequired: false, tracking: [] };
const detail = { id: 7, documentNo: 'ST-0007', businessDate: '2026-10-14', status: 'Draft', remark: null,
  scope: { warehouseIds: [4], categoryIds: [], itemIds: [] }, snapshotAt: '2026-10-14T04:00:00', postedAt: null,
  version: 1, lines: [line], approval: null };

async function masterData(page: Page): Promise<void> {
  await page.route('**/api/v1/md/items?**', route => route.fulfill({ json: { items: [
    { id: 3, code: 'ITEM', name: 'Bolt', isActive: true, trackingMode: 'None' },
    { id: 5, code: 'FOUND', name: 'Found item', isActive: true, trackingMode: 'None' }], totalCount: 2 } }));
  await page.route('**/api/v1/md/warehouses?**', route => route.fulfill({ json: { items: [
    { id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 } }));
  await page.route('**/api/v1/md/item-categories', route => route.fulfill({ json: [] }));
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
}

test('INV-STK-011 creates a scope and saves a zero count', async ({ page }) => {
  await masterData(page);
  let created: Record<string, unknown> | null = null;
  let saved: Record<string, unknown> | null = null;
  await page.route('**/api/v1/inv/stocktakes**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'POST' && path.endsWith('/stocktakes')) {
      created = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 201, json: detail });
    }
    if (request.method() === 'PUT') { saved = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ json: { ...detail, version: 2, lines: [{ ...line, countedQuantity: 0 }] } }); }
    if (path.endsWith('/7')) return route.fulfill({ json: detail });
    return route.fulfill({ json: { items: [], totalCount: 0 } });
  });
  await page.goto('/inv/stocktakes/new');
  await page.getByTestId('inv-stocktake-date').fill('2026-10-14');
  await page.getByRole('checkbox', { name: 'WH · Main' }).check();
  await page.getByTestId('inv-stocktake-create').click();
  await expect.poll(() => created).toMatchObject({ businessDate: '2026-10-14', scope: { warehouseIds: [4] } });
  await expect(page.getByTestId('inv-stocktake-line-0')).toBeVisible();
  await page.getByTestId('inv-stocktake-line-0').getByRole('textbox', { name: '實盤數量' }).fill('0');
  await expect(page.getByTestId('inv-stocktake-line-0')).toContainText('-2');
  await page.getByTestId('inv-stocktake-save').click();
  await expect.poll(() => saved).toMatchObject({ version: 1, lines: [{ lineId: 19, countedQuantity: '0' }] });
});

test('INV-STK-011 shows changed keys when submit detects a stale snapshot', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 422/);
  await masterData(page);
  await page.route('**/api/v1/inv/stocktakes**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path.endsWith('/submit')) return route.fulfill({ status: 422,
      json: { errorCode: 'INV.STOCKTAKE.SNAPSHOT_STALE', changedKeys: [{ itemId: 3, warehouseId: 4, locationId: null }] } });
    if (request.method() === 'PUT') return route.fulfill({ json: { ...detail, version: 2, lines: [{ ...line, countedQuantity: 2 }] } });
    return route.fulfill({ json: detail });
  });
  await page.goto('/inv/stocktakes/7/count');
  await page.getByTestId('inv-stocktake-line-0').getByRole('textbox', { name: '實盤數量' }).fill('2');
  await page.getByTestId('inv-stocktake-submit').click();
  await expect(page.getByText('ITEM · Bolt · WH · Main')).toBeVisible();
  await expect(page.getByTestId('inv-stocktake-error')).toBeVisible();
});

test('INV-STK-011 saves local counts before refreshing the snapshot', async ({ page }) => {
  await masterData(page);
  const calls: string[] = [];
  await page.route('**/api/v1/inv/stocktakes**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'PUT') { calls.push(`save:${request.postDataJSON().lines[0].countedQuantity}`);
      return route.fulfill({ json: { ...detail, version: 2, lines: [{ ...line, countedQuantity: 1 }] } }); }
    if (path.endsWith('/refresh-snapshot')) { calls.push(`refresh:${request.postDataJSON().version}`);
      return route.fulfill({ json: { ...detail, version: 3, lines: [{ ...line, bookQuantity: 3, countedQuantity: 1, recountRequired: true }] } }); }
    return route.fulfill({ json: detail });
  });
  await page.goto('/inv/stocktakes/7/count');
  await page.getByTestId('inv-stocktake-line-0').getByRole('textbox', { name: '實盤數量' }).fill('1');
  await page.getByTestId('inv-stocktake-refresh').click();
  await expect.poll(() => calls).toEqual(['save:1', 'refresh:2']);
  await expect(page.getByTestId('inv-stocktake-line-0')).toContainText('重新盤點');
});

test('INV-STK-011 adds and removes a found line before saving', async ({ page }) => {
  await masterData(page);
  let saved: Record<string, unknown> | null = null;
  await page.route('**/api/v1/inv/stocktakes**', route => {
    if (route.request().method() === 'PUT') { saved = route.request().postDataJSON() as Record<string, unknown>;
      return route.fulfill({ json: { ...detail, version: 2 } }); }
    return route.fulfill({ json: detail });
  });
  await page.goto('/inv/stocktakes/7/count');
  await page.getByRole('button', { name: '新增實地行' }).click();
  const found = page.getByTestId('inv-stocktake-line-1');
  await found.getByRole('button', { name: '移除實地行' }).click();
  await expect(found).toHaveCount(0);
  await page.getByRole('button', { name: '新增實地行' }).click();
  const next = page.getByTestId('inv-stocktake-line-1');
  await next.getByRole('combobox', { name: '物料' }).selectOption({ label: 'FOUND · Found item' });
  await next.getByRole('combobox', { name: '倉庫' }).selectOption({ label: 'WH · Main' });
  await next.getByRole('textbox', { name: '實盤數量' }).fill('1.000001');
  await page.getByTestId('inv-stocktake-save').click();
  await expect.poll(() => saved).toMatchObject({ lines: [
    { lineId: 19, countedQuantity: null }, { itemId: 5, warehouseId: 4, locationId: null, countedQuantity: '1.000001' },
  ] });
});
