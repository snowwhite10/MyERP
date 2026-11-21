import { mkdir } from 'node:fs/promises';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

// Sales Outbound View Interface 2.7 (WU-SD-004c1): saved list, posted/pending detail, empty and error states.
const summary = { id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7,
  customerName: 'Saved customer', businessDate: '2026-11-20', status: 'Posted', lineCount: 2,
  totalQuantity: 12.000001, version: 3 };
const posted = { id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7,
  customerCode: 'C-7', customerName: 'Saved customer', businessDate: '2026-11-20', status: 'Posted',
  remark: 'Dock 3', approvalInstanceId: 52, postedAt: '2026-11-20T15:42:10', version: 3,
  lines: [
    { id: 71, lineNo: 1, orderLineId: 91, orderLineNo: 1, itemId: 9, itemCode: 'ITEM-9', itemName: 'Lot item',
      warehouseId: 4, warehouseCode: 'WH-MAIN', warehouseName: 'Main warehouse', warehouseActive: true,
      locationId: 12, locationCode: 'BIN-12', locationName: 'Bin 12', locationActive: false, quantity: 10.000001, invMovementId: 501,
      tracking: [{ seqNo: 1, lotNo: 'LOT-A', serialNo: null, quantity: 6 },
        { seqNo: 2, lotNo: 'LOT-B', serialNo: null, quantity: 4.000001 }] },
    { id: 72, lineNo: 2, orderLineId: 92, orderLineNo: 2, itemId: 10, itemCode: 'ITEM-10', itemName: 'Serial item',
      warehouseId: 4, locationId: null, quantity: 2, invMovementId: 502,
      tracking: [{ seqNo: 1, lotNo: null, serialNo: 'SN-001', quantity: 1 },
        { seqNo: 2, lotNo: null, serialNo: 'SN-002', quantity: 1 }] }],
  approvalHistory: [{ instanceId: 52, status: 'Approved', submittedAt: '2026-11-20T09:30:00',
    steps: [{ stepNo: 1, name: 'Warehouse manager', status: 'Approved', actedBy: 2,
      actedAt: '2026-11-20T15:42:00', comment: 'Ship today' }] }] };
const pending = { ...posted, status: 'Pending', postedAt: null, version: 2,
  lines: posted.lines.map(line => ({ ...line, invMovementId: null })),
  approvalHistory: [{ instanceId: 52, status: 'Pending', submittedAt: '2026-11-20T09:30:00',
    steps: [{ stepNo: 1, name: 'Warehouse manager', status: 'Pending', actedBy: null, actedAt: null, comment: null }] }] };

async function capture(page: import('@playwright/test').Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/2.7-${name}.png`, fullPage: true, animations: 'disabled' });
}

test('SD-DEL-UI-011 shows saved deliveries and posted INV/tracking detail in three languages and two themes', async ({ page }) => {
  const requests: string[] = [];
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url()); requests.push(url.pathname);
    return route.fulfill({ json: url.pathname.endsWith('/61') ? posted :
      { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/deliveries');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('DH-0061');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('12.000001');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('已過帳');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('Posted');
  await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('已过账');
  await capture(page, 'normal-light-zh-Hans');
  await chooseLanguage(page, 'zh-Hant');
  await page.getByTestId('sd-delivery-open-61').click();
  const detail = page.getByTestId('sd-delivery-detail');
  await expect(detail).toContainText('Dock 3');
  await expect(page.getByTestId('sd-delivery-state')).toContainText('庫存異動 ID');
  await expect(page.getByTestId('sd-delivery-posted-at')).toHaveText('2026-11-20 15:42');
  await expect(page.getByTestId('sd-delivery-tracking-71')).toContainText('批號 LOT-B × 4.000001');
  await expect(page.getByTestId('sd-delivery-tracking-72')).toContainText('序號 SN-002 × 1');
  await expect(page.getByTestId('sd-delivery-movement-71')).toHaveText('501');
  await expect(page.getByTestId('sd-delivery-approval-history')).toContainText('Ship today');
  await capture(page, 'detail-posted-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-posted-dark-zh-Hant');
  await chooseLanguage(page, 'en'); await capture(page, 'detail-posted-dark-en');
  expect(requests).toEqual(['/api/v1/sal/deliveries', '/api/v1/sal/deliveries/61']);
});

test('SD-DEL-UI-012 explains a Pending reservation without claiming stock movement', async ({ page }) => {
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    return route.fulfill({ json: url.pathname.endsWith('/61') ? pending :
      { items: [{ ...summary, status: 'Pending', version: 2 }], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-open-61').click();
  await expect(page.getByTestId('sd-delivery-state')).toContainText('預留訂單餘量');
  await expect(page.getByTestId('sd-delivery-movement-71')).toHaveText('未過帳');
  await expect(page.getByTestId('sd-delivery-posted-at')).toHaveCount(0);
  await capture(page, 'detail-pending-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'detail-pending-dark-zh-Hant');
});

test('SD-DEL-UI-013 separates empty, filtered no-result, invalid dates, missing detail and server error', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/sal\/deliveries/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of (404|500)/);
  let mode: 'empty' | 'saved' | 'failure' = 'empty';
  const queries: URL[] = [];
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/61')) return route.fulfill({ status: 404, json: { errorCode: 'SAL.DELIVERY.NOT_FOUND' } });
    queries.push(url);
    if (mode === 'failure') return route.fulfill({ status: 500, json: { traceId: 'trace-delivery-500' } });
    const items = mode === 'saved' && url.searchParams.get('status') !== 'Rejected' ? [summary] : [];
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/deliveries');
  await expect(page.getByTestId('sd-delivery-table')).toContainText('尚未有出貨單');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');

  mode = 'saved';
  await page.getByTestId('sd-delivery-status').selectOption('Rejected');
  await page.getByTestId('sd-delivery-search').click();
  await expect(page.getByTestId('sd-delivery-table')).toContainText('沒有符合篩選條件的出貨單');
  await expect(page).toHaveURL(/status=Rejected/);
  await capture(page, 'no-results-dark-zh-Hant');

  const reads = queries.length;
  await page.goto('/sal/deliveries?from=2026-02-30');
  await expect(page.getByTestId('sd-delivery-validation')).toBeVisible();
  expect(queries.length).toBe(reads);
  await capture(page, 'invalid-date-dark-zh-Hant');

  await page.locator('input[name="from"]').fill('');
  await page.getByTestId('sd-delivery-search').click();
  await page.getByTestId('sd-delivery-open-61').click();
  await expect(page.getByTestId('sd-delivery-detail-error')).toContainText('找不到出貨單');
  await expect(page.getByTestId('sd-delivery-detail')).toHaveCount(0);
  await capture(page, 'detail-404-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'detail-404-light-zh-Hant');

  mode = 'failure';
  await page.getByTestId('sd-delivery-search').click();
  await expect(page.getByTestId('sd-delivery-error')).toContainText('trace-delivery-500');
  await expect(page.getByTestId('sd-delivery-table')).not.toContainText('DH-0061');
  await capture(page, 'server-error-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'server-error-dark-zh-Hant');
  mode = 'saved';
  await page.getByTestId('sd-delivery-retry').click();
  await expect(page.getByTestId('sd-delivery-row-61')).toBeVisible();
});

test('SD-DEL-UI-015 names line warehouses and locations, marks inactive ones and falls back to the ID when deleted', async ({ page }) => {
  // WU-SD-004c2b2c (ADR-153): line 72's warehouse label is missing, as when MD has soft-deleted it.
  await page.route('**/api/v1/sal/deliveries**', route => {
    const url = new URL(route.request().url());
    return route.fulfill({ json: url.pathname.endsWith('/61') ? posted :
      { items: [summary], totalCount: 1, page: 1, pageSize: 20 } });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-open-61').click();
  await expect(page.getByTestId('sd-delivery-warehouse-71')).toHaveText('WH-MAIN · Main warehouse');
  await expect(page.getByTestId('sd-delivery-location-71')).toHaveText('BIN-12 · Bin 12（已停用）');
  await expect(page.getByTestId('sd-delivery-warehouse-72')).toHaveText('已刪除倉庫（ID 4）');
  await expect(page.getByTestId('sd-delivery-location-72')).toHaveText('—');
  await expect(page.getByTestId('sd-delivery-detail')).toContainText('倉庫');
  await expect(page.getByTestId('sd-delivery-detail')).not.toContainText('倉庫 ID');
  await capture(page, 'references-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'references-dark-zh-Hant');
  await chooseTheme(page, 'light'); await chooseLanguage(page, 'en');
  await expect(page.getByTestId('sd-delivery-location-71')).toHaveText('BIN-12 · Bin 12 (inactive)');
  await expect(page.getByTestId('sd-delivery-warehouse-72')).toHaveText('Deleted warehouse (ID 4)');
  await capture(page, 'references-light-en');
});
