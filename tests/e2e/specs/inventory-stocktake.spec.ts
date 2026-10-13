import type { Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { E2E_AUTHORIZATION, chooseTheme, expect, test } from '../support/fixtures';

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

test('INV-STK-011 saves lot and serial counts and preserves them across a snapshot refresh', async ({ page }) => {
  await masterData(page);
  const tracked = { ...detail, lines: [
    { ...line, trackingMode: 'Lot', tracking: [{ id: 1, lotNo: 'LOT-A', serialNo: null, bookQuantity: 2, countedQuantity: null }] },
    { ...line, id: 20, lineNo: 2, itemId: 5, trackingMode: 'Serial', bookQuantity: 1,
      tracking: [{ id: 2, lotNo: null, serialNo: 'SN-A', bookQuantity: 1, countedQuantity: null }] },
  ] };
  const calls: unknown[] = [];
  await page.route('**/api/v1/inv/stocktakes**', route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON(); calls.push(body);
      return route.fulfill({ json: { ...tracked, version: 2, lines: tracked.lines.map((row, i) => ({ ...row,
        countedQuantity: i === 0 ? 1 : 0, tracking: row.tracking.map(t => ({ ...t, countedQuantity: i === 0 ? 1 : 0 })) })) } });
    }
    if (path.endsWith('/refresh-snapshot')) { calls.push(route.request().postDataJSON());
      return route.fulfill({ json: { ...tracked, version: 3, lines: tracked.lines.map((row, i) => ({ ...row,
        bookQuantity: i === 0 ? 3 : 1, countedQuantity: i === 0 ? 1 : 0, recountRequired: true,
        tracking: row.tracking.map(t => ({ ...t, countedQuantity: i === 0 ? 1 : 0 })) })) } }); }
    return route.fulfill({ json: tracked });
  });
  await page.goto('/inv/stocktakes/7/count');
  const lot = page.getByTestId('inv-stocktake-line-0'); const serial = page.getByTestId('inv-stocktake-line-1');
  await lot.getByRole('textbox', { name: '實盤數量' }).first().fill('1');
  await lot.getByRole('textbox', { name: '實盤數量' }).last().fill('1');
  await serial.getByRole('textbox', { name: '實盤數量' }).first().fill('0');
  await serial.getByRole('textbox', { name: '實盤數量' }).last().fill('0');
  await page.getByTestId('inv-stocktake-refresh').click();
  await expect.poll(() => calls).toMatchObject([{ version: 1, lines: [
    { lineId: 19, countedQuantity: '1', tracking: [{ lotNo: 'LOT-A', countedQuantity: '1' }] },
    { lineId: 20, countedQuantity: '0', tracking: [{ serialNo: 'SN-A', countedQuantity: '0' }] },
  ] }, { version: 2 }]);
  await expect(lot).toContainText('重新盤點');
  await expect(lot).toContainText('3');
});

test('INV-STK-011 shows saved variance, tracked gains and losses, and exports every filtered page safely', async ({ page }) => {
  await masterData(page);
  const summaries = [
    { id: 7, documentNo: '=ST-0007', businessDate: '2026-10-14', status: 'Draft', remark: '=HYPERLINK("x")', warehouseCount: 1, lineCount: 1, countedLineCount: 1, version: 1 },
    { id: 8, documentNo: 'ST-0008', businessDate: '2026-10-15', status: 'Posted', remark: 'checked', warehouseCount: 1, lineCount: 1, countedLineCount: 1, version: 2 },
  ];
  const tracked = { ...line, trackingMode: 'Lot', locationId: 5, bookQuantity: 2, countedQuantity: 2,
    tracking: [{ id: 1, lotNo: 'LOT-A', serialNo: null, bookQuantity: 2, countedQuantity: 1 }] };
  const report = (id: number) => ({ id, documentNo: summaries[id - 7].documentNo, businessDate: '2026-10-14', status: 'Draft',
    gainQuantity: 1, lossQuantity: 1, gainLineCount: 1, lossLineCount: 1, matchLineCount: 0, uncountedLineCount: 0,
    lines: [{ lineId: 19, lineNo: 1, itemId: 3, warehouseId: 4, locationId: 5, trackingMode: 'Lot', bookQuantity: 2,
      countedQuantity: 2, difference: 0, gainQuantity: 1, lossQuantity: 1, type: 'Match', isAdded: false,
      tracking: [{ lotNo: 'LOT-A', serialNo: null, bookQuantity: 2, countedQuantity: 1, difference: -1 },
        { lotNo: 'LOT-B', serialNo: null, bookQuantity: 0, countedQuantity: 1, difference: 1 }] }] });
  await page.route('**/api/v1/md/warehouses/4/locations?**', route => route.fulfill({ json: { items: [
    { id: 5, code: 'A1', name: 'Shelf', isActive: true }], totalCount: 1 } }));
  const pages: number[] = []; const varianceQueries: string[] = [];
  await page.route('**/api/v1/inv/stocktakes**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/variance')) { varianceQueries.push(url.searchParams.get('differenceOnly') ?? '');
      return route.fulfill({ json: report(Number(url.pathname.split('/').at(-2))) }); }
    if (url.pathname.endsWith('/7')) return route.fulfill({ json: { ...detail, documentNo: '=ST-0007', lines: [tracked] } });
    const pageNo = Number(url.searchParams.get('page') ?? 1); pages.push(pageNo);
    const size = Number(url.searchParams.get('pageSize') ?? 20);
    return route.fulfill({ json: { items: size === 200 ? (pageNo === 1 ? [summaries[0]] : [summaries[1]]) : summaries,
      totalCount: size === 200 ? 201 : 2, page: pageNo, pageSize: size } });
  });
  await page.goto('/inv/stocktakes');
  await page.getByTestId('inv-stocktake-search').fill('ST');
  await page.getByRole('button', { name: '搜尋' }).click();
  await page.getByTestId('inv-stocktake-row-7').getByRole('button', { name: '=ST-0007' }).click();
  await expect(page.getByTestId('inv-stocktake-detail')).toContainText('ITEM · Bolt');
  await expect(page.getByTestId('inv-stocktake-detail')).toContainText('A1 · Shelf');
  await expect(page.getByTestId('inv-stocktake-detail')).toContainText('LOT-B');
  await expect(page.getByTestId('inv-stocktake-detail')).toContainText('盤盈: 1');
  await page.getByRole('checkbox', { name: '只列差異' }).check();
  await expect.poll(() => varianceQueries).toEqual(['false', 'true']);
  await chooseTheme(page, 'dark');
  await expect(page.getByTestId('inv-stocktake-detail')).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByTestId('inv-stocktake-export').click();
  const file = await download; const contents = await readFile(await file.path(), 'utf8');
  expect(contents).toContain("'=ST-0007"); expect(contents).toContain("'=HYPERLINK");
  expect(contents).toContain('LOT-B'); expect(pages).toContain(2);
});

test.describe('INV-STK-011 read-only access', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.STOCKTAKE.VIEW', dataScope: 'Company' }] } });
  test('keeps the report and export but guards changes', async ({ page }) => {
    await page.route('**/api/v1/inv/stocktakes**', route => route.fulfill({ json: { items: [
      { id: 7, documentNo: 'ST-0007', businessDate: '2026-10-14', status: 'Draft', remark: null,
        lineCount: 1, countedLineCount: 0, warehouseCount: 1, version: 1 }], totalCount: 1 } }));
    await page.goto('/inv/stocktakes');
    const row = page.getByTestId('inv-stocktake-row-7'); await expect(row).toBeVisible();
    await expect(row.getByRole('button', { name: '送審' })).toHaveCount(0);
    await expect(row.getByRole('button', { name: '刪除' })).toHaveCount(0);
    await expect(row.getByRole('link', { name: '輸入實盤' })).toHaveCount(0);
    await expect(page.getByTestId('inv-stocktake-export')).toBeVisible();
    await page.goto('/inv/stocktakes/7/count'); await expect(page).toHaveURL(/\/forbidden/);
  });
});
