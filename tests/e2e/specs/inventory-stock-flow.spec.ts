import { readFile } from 'node:fs/promises';
import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const OUT = '../../tests/qa/INV/screenshots';
const row = { itemId: 11, warehouseId: 3, locationId: null, openingQuantity: 10,
  inboundQuantity: 3, outboundQuantity: 4, closingQuantity: 9, negativeClosing: false };

for (const theme of ['light', 'dark'] as const) {
  test(`INV-RPT-001/003/006 stock flow ${theme}`, async ({ page }) => {
    let rows = [row]; let exportFilter = '';
    await page.route('**/api/v1/inv/selection-options/**', route => {
      const path = new URL(route.request().url()).pathname;
      const items = path.endsWith('/items') ? [{ id: 11, code: 'BOLT', name: 'Item' }]
        : path.endsWith('/warehouses') ? [{ id: 3, code: 'WH', name: 'Main' }] : [];
      return route.fulfill({ json: { items, page: 1, pageSize: 200, totalCount: items.length } });
    });
    await page.route('**/api/v1/inv/reports/stock-flow**', route => {
      const request = route.request(); const url = new URL(request.url());
      if (url.pathname.endsWith('/export')) {
        exportFilter = url.search;
        return route.fulfill({ status: 200, contentType: 'text/csv; charset=utf-8', headers: {
          'content-disposition': 'attachment; filename=stock-flow.csv', 'x-export-row-count': String(rows.length),
        }, body: '\uFEFFQueriedAt,2026-10-18T10:00:00+08:00\r\nItemId,WarehouseId,LocationId,OpeningQuantity,InboundQuantity,OutboundQuantity,ClosingQuantity,NegativeClosing\r\n'
          + rows.map(r => `${r.itemId},${r.warehouseId},,10.000000,3.000000,4.000000,9.000000,false\r\n`).join('') });
      }
      return route.fulfill({ json: { queriedAt: '2026-10-18T10:00:00+08:00', totalCount: rows.length,
        items: rows, page: 1, pageSize: 20 } });
    });
    await page.goto('/inv/reports/stock-flow');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await page.getByTestId('inv-flow-from').fill('2026-10-01');
    await page.getByTestId('inv-flow-to').fill('2026-10-18');
    await page.getByTestId('inv-flow-search').click();
    await expect(page.getByTestId('inv-flow-snapshot')).toContainText('共 1 列');
    await expect(page.getByTestId('inv-flow-table')).toContainText('9.000000');
    await expect(page.getByTestId('inv-flow-table')).toContainText('BOLT · Item');
    await page.screenshot({ path: `${OUT}/5.27-normal-${theme}-zh-Hant.png`, fullPage: true });
    const promise = page.waitForEvent('download');
    await page.getByTestId('inv-flow-export').click();
    const download = await promise;
    expect(download.suggestedFilename()).toBe('stock-flow.csv');
    const content = await readFile(await download.path(), 'utf8');
    expect(content).toContain('9.000000');
    expect(content.trim().split('\n')).toHaveLength(3);
    expect(exportFilter).toContain('from=2026-10-01');
    await expect(page.getByTestId('inv-flow-notice')).toContainText('已下載 1 列 CSV');
    await page.screenshot({ path: `${OUT}/5.27-exported-${theme}-zh-Hant.png`, fullPage: true });
    rows = [{ ...row, openingQuantity: 0, inboundQuantity: 0, outboundQuantity: 1,
      closingQuantity: -1, negativeClosing: true }];
    await page.getByTestId('inv-flow-search').click();
    await expect(page.getByTestId('inv-flow-table')).toContainText('-1.000000');
    await expect(page.getByTestId('inv-flow-table')).toContainText('負結存');
    await page.screenshot({ path: `${OUT}/5.27-negative-${theme}-zh-Hant.png`, fullPage: true });
    rows = [];
    await page.getByTestId('inv-flow-search').click();
    await expect(page.getByTestId('inv-flow-notice')).toContainText('目前條件沒有已過帳異動');
    await expect(page.getByTestId('inv-flow-export')).toBeDisabled();
    await page.screenshot({ path: `${OUT}/5.27-empty-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('inv-flow-to').fill('2026-09-01');
    await page.getByTestId('inv-flow-search').click();
    await expect(page.getByTestId('inv-flow-error')).toContainText('起始日不得晚於截止日');
    await page.screenshot({ path: `${OUT}/5.27-validation-${theme}-zh-Hant.png`, fullPage: true });
  });
  test(`INV-RPT-007/010 export rejection ${theme}`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of (403|422|500)/);
    pageErrors.allow(/Failed to load resource: net::ERR_(FAILED|TIMED_OUT)/);
    pageErrors.allowApi5xx(/\/api\/v1\/inv\/reports\/stock-flow\/export(?:\?.*)?$/, 500);
    let failure: 403 | 422 | 500 | 'network' | 'timeout' = 422;
    await page.route('**/api/v1/inv/selection-options/**', route => route.fulfill({ json: {
      items: [], page: 1, pageSize: 200, totalCount: 0,
    } }));
    await page.route('**/api/v1/inv/reports/stock-flow**', route => {
      if (new URL(route.request().url()).pathname.endsWith('/export')) {
        if (failure === 'network') return route.abort('failed');
        if (failure === 'timeout') return route.abort('timedout');
        return route.fulfill({ status: failure, contentType: 'application/problem+json',
          body: JSON.stringify({ errorCode: failure === 422 ? 'INV.REPORT.EXPORT_LIMIT' : 'COMMON.PERMISSION.DENIED',
            traceId: failure === 500 ? 'trace-flow-500' : undefined }) });
      }
      return route.fulfill({ json: { queriedAt: '2026-10-18T10:00:00+08:00', totalCount: 1,
        items: [row], page: 1, pageSize: 20 } });
    });
    await page.goto('/inv/reports/stock-flow');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await page.getByTestId('inv-flow-from').fill('2026-10-01');
    await page.getByTestId('inv-flow-to').fill('2026-10-18');
    await page.getByTestId('inv-flow-search').click();
    await expect(page.getByTestId('inv-flow-table')).toContainText('9.000000');
    await page.getByTestId('inv-flow-export').click();
    await expect(page.getByTestId('inv-flow-error')).toContainText('超過 10,000 列');
    await expect(page.getByTestId('inv-flow-table')).toContainText('9.000000');
    await page.screenshot({ path: `${OUT}/5.27-limit-${theme}-zh-Hant.png`, fullPage: true });
    failure = 403; await page.getByTestId('inv-flow-export').click();
    await expect(page.getByTestId('inv-flow-error')).toContainText('沒有匯出權限');
    await page.screenshot({ path: `${OUT}/5.27-denied-${theme}-zh-Hant.png`, fullPage: true });
    failure = 500; await page.getByTestId('inv-flow-export').click();
    await expect(page.getByTestId('inv-flow-error')).toContainText('匯出失敗');
    await expect(page.getByTestId('inv-stock-flow')).toContainText('trace-flow-500');
    await page.screenshot({ path: `${OUT}/5.27-error500-${theme}-zh-Hant.png`, fullPage: true });
    failure = 'network'; await page.getByTestId('inv-flow-export').click();
    await expect(page.getByTestId('inv-flow-error')).toContainText('無法連線');
    await page.screenshot({ path: `${OUT}/5.27-network-${theme}-zh-Hant.png`, fullPage: true });
    failure = 'timeout'; await page.getByTestId('inv-flow-export').click();
    await expect(page.getByTestId('inv-flow-error')).toContainText('無法連線');
    await page.screenshot({ path: `${OUT}/5.27-timeout-${theme}-zh-Hant.png`, fullPage: true });
  });
}

test('INV-RPT-007 cancelling a pending export never starts a download', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: net::ERR_ABORTED/);
  let release: (() => void) | undefined;
  let downloads = 0;
  page.on('download', () => ++downloads);
  await page.route('**/api/v1/inv/selection-options/**', route => route.fulfill({ json: {
    items: [], page: 1, pageSize: 200, totalCount: 0,
  } }));
  await page.route('**/api/v1/inv/reports/stock-flow**', async route => {
    if (new URL(route.request().url()).pathname.endsWith('/export')) {
      await new Promise<void>(resolve => { release = resolve; });
      try { await route.fulfill({ status: 200, body: 'unexpected' }); } catch { /* Browser cancelled the request. */ }
      return;
    }
    await route.fulfill({ json: { queriedAt: '2026-10-18T10:00:00+08:00', totalCount: 1,
      items: [row], page: 1, pageSize: 20 } });
  });
  await page.goto('/inv/reports/stock-flow');
  await page.getByTestId('inv-flow-from').fill('2026-10-01');
  await page.getByTestId('inv-flow-to').fill('2026-10-18');
  await page.getByTestId('inv-flow-search').click();
  await expect(page.getByTestId('inv-flow-table')).toContainText('9.000000');
  await page.getByTestId('inv-flow-export').click();
  await expect(page.getByTestId('inv-flow-cancel')).toBeVisible();
  await page.getByTestId('inv-flow-cancel').click();
  release?.();
  await expect(page.getByTestId('inv-flow-notice')).toContainText('已取消匯出，沒有下載檔案');
  await page.screenshot({ path: `${OUT}/5.27-cancelled-light-zh-Hant.png`, fullPage: true });
  expect(downloads).toBe(0);
});

test('INV-RPT-006 rejects a CSV response without a trustworthy row count', async ({ page }) => {
  let downloads = 0;
  page.on('download', () => ++downloads);
  await page.route('**/api/v1/inv/selection-options/**', route => route.fulfill({ json: {
    items: [], page: 1, pageSize: 200, totalCount: 0,
  } }));
  await page.route('**/api/v1/inv/reports/stock-flow**', route =>
    new URL(route.request().url()).pathname.endsWith('/export')
      ? route.fulfill({ status: 200, contentType: 'text/csv', body: '1,2,3' })
      : route.fulfill({ json: { queriedAt: '2026-10-18T10:00:00+08:00', totalCount: 1,
        items: [row], page: 1, pageSize: 20 } }));
  await page.goto('/inv/reports/stock-flow');
  await page.getByTestId('inv-flow-from').fill('2026-10-01');
  await page.getByTestId('inv-flow-to').fill('2026-10-18');
  await page.getByTestId('inv-flow-search').click();
  await expect(page.getByTestId('inv-flow-table')).toContainText('9.000000');
  await page.getByTestId('inv-flow-export').click();
  await expect(page.getByTestId('inv-flow-error')).toContainText('沒有回傳有效的匯出筆數');
  expect(downloads).toBe(0);
});

for (const language of ['zh-Hans', 'en'] as const) {
  test(`INV-RPT-001 stock flow translation ${language}`, async ({ page }) => {
    await page.route('**/api/v1/inv/selection-options/**', route => route.fulfill({ json: {
      items: [], page: 1, pageSize: 200, totalCount: 0,
    } }));
    await page.route('**/api/v1/inv/reports/stock-flow**', route => route.fulfill({ json: {
      queriedAt: '2026-10-18T10:00:00+08:00', totalCount: 1, items: [row], page: 1, pageSize: 20,
    } }));
    await page.goto('/inv/reports/stock-flow');
    await chooseLanguage(page, language);
    await page.getByTestId('inv-flow-from').fill('2026-10-01');
    await page.getByTestId('inv-flow-to').fill('2026-10-18');
    await page.getByTestId('inv-flow-search').click();
    await expect(page.getByTestId('inv-flow-table')).toContainText('9.000000');
    await expect(page.getByTestId('inv-flow-snapshot')).toContainText(language === 'en' ? 'Rows: 1' : '共 1 行');
    await page.screenshot({ path: `${OUT}/5.27-normal-light-${language}.png`, fullPage: true });
  });
}

test.describe('INV-RPT-005 permission boundary', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.REPORT.VIEW', dataScope: 'Company' }] } });
  for (const theme of ['light', 'dark'] as const) test(`VIEW role can read but has no export action ${theme}`, async ({ page }) => {
    await page.route('**/api/v1/inv/selection-options/**', route => {
      const path = new URL(route.request().url()).pathname;
      const items = path.endsWith('/items') ? [{ id: 11, code: 'BOLT', name: 'Item' }]
        : path.endsWith('/warehouses') ? [{ id: 3, code: 'WH', name: 'Main' }] : [];
      return route.fulfill({ json: { items, page: 1, pageSize: 200, totalCount: items.length } });
    });
    await page.route('**/api/v1/inv/reports/stock-flow**', route => route.fulfill({ json: {
      queriedAt: '2026-10-18T10:00:00+08:00', totalCount: 1, items: [row], page: 1, pageSize: 20,
    } }));
    await page.goto('/inv/reports/stock-flow');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await page.getByTestId('inv-flow-from').fill('2026-10-01');
    await page.getByTestId('inv-flow-to').fill('2026-10-18');
    await page.getByTestId('inv-flow-search').click();
    await expect(page.getByTestId('inv-flow-table')).toContainText('9.000000');
    await expect(page.getByTestId('inv-flow-table')).toContainText('BOLT · Item');
    await expect(page.getByTestId('inv-flow-export')).toHaveCount(0);
    await page.screenshot({ path: `${OUT}/5.27-view-only-${theme}-zh-Hant.png`, fullPage: true });
  });
});

test.describe('INV-RPT-005 no VIEW permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [] } });
  for (const theme of ['light', 'dark'] as const) test(`shows 403 before requesting a report ${theme}`, async ({ page }) => {
    let requests = 0;
    await page.route('**/api/v1/inv/reports/stock-flow**', route => { ++requests; return route.fulfill({ json: {} }); });
    await page.goto('/inv/reports/stock-flow');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
    expect(requests).toBe(0);
    await page.screenshot({ path: `${OUT}/5.27-no-view-${theme}-zh-Hant.png`, fullPage: true });
  });
});

test('INV-RPT-005 company switch discards late options from the previous company', async ({ page }) => {
  let releaseOld: (() => void) | undefined;
  await page.route('**/api/v1/inv/selection-options/**', async route => {
    const company = route.request().headers()['x-company-id'] ?? '1';
    const isItem = new URL(route.request().url()).pathname.endsWith('/items');
    if (company === '1' && isItem) await new Promise<void>(resolve => { releaseOld = resolve; });
    const rows = isItem ? [{ id: company === '1' ? 11 : 22, code: company === '1' ? 'OLD' : 'NEW', name: 'Item' }] : [];
    await route.fulfill({ json: { items: rows, page: 1, pageSize: 200, totalCount: rows.length } });
  });
  await page.goto('/inv/reports/stock-flow');
  await expect.poll(() => Boolean(releaseOld)).toBe(true);
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.locator('select[name=item]')).toContainText('NEW · Item');
  releaseOld?.();
  await expect(page.locator('select[name=item]')).not.toContainText('OLD · Item');
  await expect(page.locator('select[name=item]')).toContainText('NEW · Item');
});
