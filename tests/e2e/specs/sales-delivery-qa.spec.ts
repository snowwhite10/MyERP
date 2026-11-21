import { mkdir } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

/**
 * WU-SD-004c3: Sales outbound 2.7 (list/detail/actions), 2.8 (create), 2.9 (edit) QA round —
 * 403 / 409 / timeout / offline across the three screens, three languages × light/dark,
 * and a stateful create → edit → submit flow asserting that what the screen says matches what the mock server stored.
 */
const LANGUAGES = ['zh-Hant', 'en', 'zh-Hans'] as const;
const UNTRANSLATED = /sd\.(delivery|deliveryCreate|deliveryEdit)\.|errors\.[A-Z]|undefined/;
const FORM_OPTIONS = /\/sal\/deliveries\/(order|warehouse)-options/;
/** The delivery service gives up after 6 s; the held request is released (aborted) once the test has seen the timeout. */
const HOLD_MS = 6_800;

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/SD/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${name}.png`, fullPage: true, animations: 'disabled' });
}

/** One state in every language and theme; the region must never show an untranslated key. */
async function captureAll(page: Page, name: string, region: string): Promise<void> {
  // Theme outside, language inside: two theme switches per state keep the header menus (WU-P0-E2E-FLAKE-MENU) out of the way.
  for (const theme of ['light', 'dark'] as const) {
    await page.evaluate(() => window.scrollTo(0, 0));
    await chooseTheme(page, theme);
    for (const language of LANGUAGES) {
      await page.evaluate(() => window.scrollTo(0, 0));
      await chooseLanguage(page, language);
      await expect(page.getByTestId(region)).not.toContainText(UNTRANSLATED);
      await capture(page, `${name}-${theme}-${language}`);
    }
  }
  await page.evaluate(() => window.scrollTo(0, 0));
  await chooseLanguage(page, 'zh-Hant'); await chooseTheme(page, 'light');
}

async function hold(route: Route): Promise<void> {
  await new Promise(resolve => setTimeout(resolve, HOLD_MS));
  await route.abort('timedout').catch(() => undefined);
}

const order = { id: 81, documentNo: 'SO-0081', status: 'Approved', customerCode: 'C-7', customerName: 'Saved customer',
  lines: [
    { id: 91, lineNo: 1, itemCode: 'ITEM-9', itemName: 'Plain item', quantity: 12, deliveredQuantity: 2, pendingQuantity: 0,
      remainingQuantity: 10 },
    { id: 93, lineNo: 3, itemCode: 'ITEM-11', itemName: 'Other item', quantity: 2, deliveredQuantity: 0, pendingQuantity: 0,
      remainingQuantity: 2 }] };
const warehouse = { id: 4, code: 'WH-1', name: 'Main warehouse', trackingMode: null, isActive: true };

interface StoredLine { lineNo: number; orderLineId: number; warehouseId: number; locationId: number | null; quantity: number }
interface Stored { status: 'Draft' | 'Pending'; version: number; businessDate: string; lines: StoredLine[] }

function detailOf(stored: Stored) {
  return { id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7, customerCode: 'C-7',
    customerName: 'Saved customer', businessDate: stored.businessDate, status: stored.status, remark: null,
    approvalInstanceId: stored.status === 'Pending' ? 501 : null, postedAt: null, version: stored.version,
    approvalHistory: stored.status === 'Pending' ? [{ instanceId: 501, status: 'Pending', submittedAt: '2026-11-21T09:00:00',
      steps: [{ stepNo: 1, name: 'Manager', status: 'Pending', actedBy: null, actedAt: null, comment: null }] }] : [],
    lines: stored.lines.map((line, index) => {
      const source = order.lines.find(candidate => candidate.id === line.orderLineId)!;
      return { id: 71 + index, lineNo: line.lineNo, orderLineId: line.orderLineId, orderLineNo: source.lineNo,
        itemId: 9 + index, itemCode: source.itemCode, itemName: source.itemName, trackingMode: 'None',
        warehouseId: line.warehouseId, warehouseCode: 'WH-1', warehouseName: 'Main warehouse', warehouseActive: true,
        locationId: line.locationId, quantity: line.quantity, tracking: [], invMovementId: null };
    }) };
}
function summaryOf(stored: Stored | null) {
  const items = stored ? [{ id: 61, documentNo: 'DH-0061', orderId: 81, orderDocumentNo: 'SO-0081', customerId: 7,
    customerName: 'Saved customer', businessDate: stored.businessDate, status: stored.status, lineCount: stored.lines.length,
    totalQuantity: stored.lines.reduce((sum, line) => sum + line.quantity, 0), version: stored.version }] : [];
  return { items, totalCount: items.length, page: 1, pageSize: 20 };
}

async function routeOptions(page: Page): Promise<void> {
  await page.route('**/api/v1/sal/deliveries/order-options**', route => {
    if (new URL(route.request().url()).pathname.endsWith('/81')) return route.fulfill({ json: order });
    return route.fulfill({ json: { items: [{ id: 81, documentNo: 'SO-0081', customerCode: 'C-7', customerName: 'Saved customer',
      businessDate: '2026-11-20' }], totalCount: 1, page: 1, pageSize: 10 } });
  });
  await page.route('**/api/v1/sal/deliveries/warehouse-options**', route => route.fulfill({ json: { items: [warehouse], totalCount: 1 } }));
}

async function chooseOrderAndWarehouses(page: Page): Promise<void> {
  await page.getByTestId('sd-delivery-create-order-pick').click();
  await page.getByText('SO-0081', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-delivery-create-order')).toContainText('SO-0081');
  await page.getByTestId('sd-delivery-create-date').fill('2026-11-21');
  for (const lineNo of [1, 2]) {
    await page.getByTestId(`sd-delivery-create-warehouse-pick-${lineNo}`).click();
    await page.getByText('WH-1', { exact: true }).last().click();
    await page.getByTestId('erp-select-confirm').click();
  }
}

test('SD-DEL-QA-001 create → list → edit → submit: every screen shows exactly what the server stored', async ({ page }) => {
  test.setTimeout(180_000);
  let stored: Stored | null = null;
  const writes: string[] = [];
  await routeOptions(page);
  await page.route('**/api/v1/sal/deliveries**', route => {
    const request = route.request();
    if (FORM_OPTIONS.test(request.url())) return route.fallback();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'POST' && path.endsWith('/sal/deliveries')) {
      const body = JSON.parse(request.postData() ?? '{}');
      writes.push('POST');
      stored = { status: 'Draft', version: 1, businessDate: body.businessDate, lines: body.lines };
      return route.fulfill({ status: 201, json: detailOf(stored) });
    }
    if (request.method() === 'PUT') {
      const body = JSON.parse(request.postData() ?? '{}');
      writes.push(`PUT v${body.version}`);
      if (body.version !== stored!.version) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
      stored = { ...stored!, version: stored!.version + 1, businessDate: body.draft.businessDate,
        lines: body.draft.lines.map(({ tracking: _tracking, ...line }: StoredLine & { tracking?: unknown }) => line) };
      return route.fulfill({ json: detailOf(stored) });
    }
    if (request.method() === 'POST' && path.endsWith('/submit')) {
      const body = JSON.parse(request.postData() ?? '{}');
      writes.push(`SUBMIT v${body.version}`);
      stored = { ...stored!, status: 'Pending', version: stored!.version + 1 };
      return route.fulfill({ json: { ...detailOf(stored), approvalHistory: null } });
    }
    if (path.endsWith('/61')) return route.fulfill({ json: detailOf(stored!) });
    return route.fulfill({ json: summaryOf(stored) });
  });

  // 2.8 create: 5 + 2 saved as a Draft.
  await page.goto('/sal/deliveries/new');
  await chooseOrderAndWarehouses(page);
  await page.getByTestId('sd-delivery-create-quantity-1').fill('5.25');
  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-success')).toContainText('DH-0061');
  expect(stored).toMatchObject({ status: 'Draft', version: 1, lines: [{ quantity: 5.25, warehouseId: 4 }, { quantity: 2 }] });
  await captureAll(page, '2.8-qa-success', 'sd-delivery-create');

  // 2.7 list and detail show the stored total 7.25 and both lines.
  await page.goto('/sal/deliveries');
  const row = page.getByTestId('sd-delivery-row-61');
  await expect(row).toContainText('DH-0061');
  await expect(row).toContainText('7.25');
  await expect(row).toContainText('草稿');
  await page.getByTestId('sd-delivery-open-61').click();
  await expect(page.getByTestId('sd-delivery-detail')).toContainText('5.25');
  await expect(page.getByTestId('sd-delivery-warehouse-71')).toHaveText('WH-1 · Main warehouse');
  await captureAll(page, '2.7-qa-detail', 'sd-delivery-detail');

  // 2.9 edit: the form loads the stored values; change line 1 to 6 and save as version 2.
  await page.getByTestId('sd-delivery-edit-link').click();
  await expect(page.getByTestId('sd-delivery-edit-quantity-1')).toHaveValue('5.25');
  await page.getByTestId('sd-delivery-edit-quantity-1').fill('6');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-success')).toContainText('2');
  expect(stored).toMatchObject({ version: 2, lines: [{ quantity: 6 }, { quantity: 2 }] });
  await captureAll(page, '2.9-qa-success', 'sd-delivery-edit');

  // Back on 2.7: total 8, version 2; submit → Pending, version 3, on the row and in the detail.
  await page.getByTestId('sd-delivery-edit-back').click();
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('8');
  await page.getByTestId('sd-delivery-open-61').click();
  await page.getByTestId('sd-delivery-submit').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('sd-delivery-action-notice')).toContainText('版本 3');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('審批中');
  await expect(page.getByTestId('sd-delivery-detail-status')).toContainText('審批中');
  expect(stored).toMatchObject({ status: 'Pending', version: 3 });
  expect(writes).toEqual(['POST', 'PUT v1', 'SUBMIT v2']);
  await captureAll(page, '2.7-qa-submitted', 'sd-delivery-detail');
});

test('SD-DEL-QA-002 2.7 list: 403, offline and timeout explain the failure and never show an empty list', async ({ page, pageErrors }) => {
  test.setTimeout(120_000);
  pageErrors.allow(/Failed to load resource: (the server responded with a status of 403|net::ERR_FAILED|net::ERR_TIMED_OUT)/);
  let mode: 'ok' | 'forbidden' | 'offline' | 'timeout' = 'forbidden';
  const stored: Stored = { status: 'Draft', version: 3, businessDate: '2026-11-21', lines: [
    { lineNo: 1, orderLineId: 91, warehouseId: 4, locationId: null, quantity: 2 }] };
  await page.route('**/api/v1/sal/deliveries**', route => {
    if (mode === 'forbidden') return route.fulfill({ status: 403, json: { status: 403, errorCode: 'COMMON.PERMISSION.DENIED' } });
    if (mode === 'offline') return route.abort('failed');
    if (mode === 'timeout') return hold(route);
    return route.fulfill({ json: new URL(route.request().url()).pathname.endsWith('/61') ? detailOf(stored) : summaryOf(stored) });
  });
  await page.goto('/sal/deliveries');
  // ADR-156: the list did not name COMMON.PERMISSION.DENIED and once showed "unexpected error" for a server 403.
  await expect(page.getByTestId('sd-delivery-error')).toContainText('您沒有執行此操作的權限');
  await expect(page.getByTestId('sd-delivery-table')).not.toContainText('尚未有出貨單');
  await captureAll(page, '2.7-qa-forbidden', 'sd-delivery-error');

  mode = 'offline';
  await page.getByTestId('sd-delivery-retry').click();
  await expect(page.getByTestId('sd-delivery-error')).toContainText('無法連線');
  await expect(page.getByTestId('sd-delivery-table')).not.toContainText('尚未有出貨單');
  await captureAll(page, '2.7-qa-offline', 'sd-delivery-error');

  mode = 'timeout';
  await page.getByTestId('sd-delivery-retry').click();
  await expect(page.getByTestId('sd-delivery-error')).toBeVisible({ timeout: 10_000 });
  await expect(page.getByTestId('sd-delivery-table')).not.toContainText('尚未有出貨單');
  await capture(page, '2.7-qa-timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.7-qa-timeout-dark-zh-Hant'); await chooseTheme(page, 'light');

  mode = 'ok';
  await page.getByTestId('sd-delivery-retry').click();
  await expect(page.getByTestId('sd-delivery-row-61')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-error')).toHaveCount(0);

  // Detail: offline then timeout keep the list, explain the failure and offer a retry.
  mode = 'offline';
  await page.getByTestId('sd-delivery-open-61').click();
  await expect(page.getByTestId('sd-delivery-detail-error')).toContainText('無法連線');
  await expect(page.getByTestId('sd-delivery-row-61')).toBeVisible();
  await capture(page, '2.7-qa-detail-offline-light-zh-Hant');
  mode = 'timeout';
  await page.getByTestId('sd-delivery-detail-retry').click();
  await expect(page.getByTestId('sd-delivery-detail-error')).toBeVisible({ timeout: 10_000 });
  await capture(page, '2.7-qa-detail-timeout-light-zh-Hant');
  mode = 'forbidden';
  await page.getByTestId('sd-delivery-detail-retry').click();
  await expect(page.getByTestId('sd-delivery-detail-error')).toContainText('您沒有執行此操作的權限');
  await capture(page, '2.7-qa-detail-forbidden-light-zh-Hant');
  mode = 'ok';
  await page.getByTestId('sd-delivery-detail-retry').click();
  await expect(page.getByTestId('sd-delivery-detail')).toBeVisible();
});

test('SD-DEL-QA-003 2.7 actions: 403, 409, offline and timeout change nothing and never claim success', async ({ page, pageErrors }) => {
  test.setTimeout(120_000);
  pageErrors.allow(/Failed to load resource: (the server responded with a status of (403|409)|net::ERR_FAILED|net::ERR_TIMED_OUT)/);
  let stored: Stored = { status: 'Draft', version: 3, businessDate: '2026-11-21', lines: [
    { lineNo: 1, orderLineId: 91, warehouseId: 4, locationId: null, quantity: 2 }] };
  const replies: (number | 'offline' | 'timeout')[] = [403, 409, 'offline', 'timeout'];
  let submits = 0;
  await page.route('**/api/v1/sal/deliveries**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'POST') {
      submits++;
      const reply = replies.shift()!;
      if (reply === 'offline') return route.abort('failed');
      if (reply === 'timeout') return hold(route);
      return route.fulfill({ status: reply, json: { status: reply,
        errorCode: reply === 403 ? 'COMMON.PERMISSION.DENIED' : 'COMMON.CONCURRENCY.CONFLICT', traceId: `t-${reply}` } });
    }
    return route.fulfill({ json: path.endsWith('/61') ? detailOf(stored) : summaryOf(stored) });
  });
  await page.goto('/sal/deliveries');
  await page.getByTestId('sd-delivery-open-61').click();
  const submit = async () => { await page.getByTestId('sd-delivery-submit').click(); await page.getByTestId('erp-message-ok').click(); };

  await submit();
  await expect(page.getByTestId('sd-delivery-action-error')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-action-unchanged')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-action-notice')).toHaveCount(0);
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('草稿');
  await captureAll(page, '2.7-qa-action-forbidden', 'sd-delivery-detail');

  await submit();
  await expect(page.getByTestId('sd-delivery-action-stale')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-action-refresh')).toBeFocused();
  await expect(page.getByTestId('sd-delivery-submit')).toBeDisabled();
  await capture(page, '2.7-qa-action-conflict-light-zh-Hant');
  stored = { ...stored, version: 4 };
  await page.getByTestId('sd-delivery-action-refresh').click();
  await expect(page.getByTestId('sd-delivery-submit')).toBeEnabled();

  await submit();
  await expect(page.getByTestId('sd-delivery-action-stale')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-action-notice')).toHaveCount(0);
  await captureAll(page, '2.7-qa-action-offline', 'sd-delivery-detail');
  await page.getByTestId('sd-delivery-action-refresh').click();

  await submit();
  await expect(page.getByTestId('sd-delivery-action-stale')).toBeVisible({ timeout: 10_000 });
  await expect(page.getByTestId('sd-delivery-action-notice')).toHaveCount(0);
  await capture(page, '2.7-qa-action-timeout-light-zh-Hant');
  await expect(page.getByTestId('sd-delivery-row-61')).toContainText('草稿');
  expect(submits).toBe(4);
});

test('SD-DEL-QA-004 2.8 create: options 403/timeout and save 403/offline/timeout never claim success', async ({ page, pageErrors }) => {
  test.setTimeout(120_000);
  pageErrors.allow(/Failed to load resource: (the server responded with a status of 403|net::ERR_FAILED|net::ERR_TIMED_OUT)/);
  let source: 'forbidden' | 'timeout' | 'ok' = 'forbidden';
  const saves: (number | 'offline' | 'timeout')[] = [403, 'offline', 'timeout'];
  let posts = 0;
  await routeOptions(page);
  await page.route('**/api/v1/sal/deliveries/order-options/81', route => source === 'forbidden'
    ? route.fulfill({ status: 403, json: { status: 403, errorCode: 'COMMON.PERMISSION.DENIED' } })
    : source === 'timeout' ? hold(route) : route.fulfill({ json: order }));
  await page.route('**/api/v1/sal/deliveries', route => {
    posts++;
    const reply = saves.shift()!;
    if (reply === 'offline') return route.abort('failed');
    if (reply === 'timeout') return hold(route);
    return route.fulfill({ status: reply, json: { status: reply, errorCode: 'COMMON.PERMISSION.DENIED', traceId: 't-403' } });
  });
  await page.goto('/sal/deliveries/new');
  await page.getByTestId('sd-delivery-create-order-pick').click();
  await page.getByText('SO-0081', { exact: true }).last().click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('sd-delivery-create-order-error')).toBeVisible();
  await captureAll(page, '2.8-qa-order-forbidden', 'sd-delivery-create');
  source = 'timeout';
  await page.getByTestId('sd-delivery-create-order-retry').click();
  await expect(page.getByTestId('sd-delivery-create-order-error')).toBeVisible({ timeout: 10_000 });
  await capture(page, '2.8-qa-order-timeout-light-zh-Hant');
  source = 'ok';
  await page.getByTestId('sd-delivery-create-order-retry').click();
  await expect(page.getByTestId('sd-delivery-create-order-error')).toHaveCount(0);
  await chooseOrderAndWarehouses(page);

  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-error')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-delivery-create-quantity-1')).toHaveValue('10');
  await capture(page, '2.8-qa-save-forbidden-light-zh-Hant');

  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-uncertain')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-create-success')).toHaveCount(0);
  await captureAll(page, '2.8-qa-save-offline', 'sd-delivery-create');
  await expect(page.getByTestId('sd-delivery-create-save')).toBeDisabled();
  expect(posts).toBe(2);
});

test('SD-DEL-QA-005 2.8 save timeout is uncertain and locks the form against a duplicate', async ({ page, pageErrors }) => {
  test.setTimeout(60_000);
  pageErrors.allow(/Failed to load resource: net::ERR_TIMED_OUT/);
  let posts = 0;
  await routeOptions(page);
  await page.route('**/api/v1/sal/deliveries', route => { posts++; return hold(route); });
  await page.goto('/sal/deliveries/new');
  await chooseOrderAndWarehouses(page);
  await page.getByTestId('sd-delivery-create-save').click();
  await expect(page.getByTestId('sd-delivery-create-uncertain')).toBeVisible({ timeout: 10_000 });
  await expect(page.getByTestId('sd-delivery-create-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-delivery-create-save')).toBeDisabled();
  await capture(page, '2.8-qa-save-timeout-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, '2.8-qa-save-timeout-dark-zh-Hant');
  expect(posts).toBe(1);
});

test('SD-DEL-QA-006 2.9 edit: load 403/timeout and save 403/offline/timeout keep input and never claim success', async ({ page, pageErrors }) => {
  test.setTimeout(120_000);
  pageErrors.allow(/Failed to load resource: (the server responded with a status of 403|net::ERR_FAILED|net::ERR_TIMED_OUT)/);
  const stored: Stored = { status: 'Draft', version: 3, businessDate: '2026-11-21', lines: [
    { lineNo: 1, orderLineId: 91, warehouseId: 4, locationId: null, quantity: 2 },
    { lineNo: 2, orderLineId: 93, warehouseId: 4, locationId: null, quantity: 2 }] };
  let load: 'forbidden' | 'timeout' | 'ok' = 'forbidden';
  const saves: (number | 'offline' | 'timeout')[] = [403, 'offline', 'timeout'];
  let puts = 0;
  await routeOptions(page);
  await page.route('**/api/v1/sal/deliveries/61', route => {
    if (route.request().method() === 'PUT') {
      puts++;
      const reply = saves.shift()!;
      if (reply === 'offline') return route.abort('failed');
      if (reply === 'timeout') return hold(route);
      return route.fulfill({ status: reply, json: { status: reply, errorCode: 'COMMON.PERMISSION.DENIED', traceId: 't-403' } });
    }
    if (load === 'forbidden') return route.fulfill({ status: 403, json: { status: 403, errorCode: 'COMMON.PERMISSION.DENIED' } });
    if (load === 'timeout') return hold(route);
    return route.fulfill({ json: detailOf(stored) });
  });
  await page.goto('/sal/deliveries/61/edit');
  await expect(page.getByTestId('sd-delivery-edit-load-error')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-edit-form')).toHaveCount(0);
  await captureAll(page, '2.9-qa-load-forbidden', 'sd-delivery-edit');
  load = 'timeout';
  await page.getByTestId('sd-delivery-edit-load-retry').click();
  await expect(page.getByTestId('sd-delivery-edit-load-error')).toBeVisible({ timeout: 10_000 });
  await capture(page, '2.9-qa-load-timeout-light-zh-Hant');
  load = 'ok';
  await page.getByTestId('sd-delivery-edit-load-retry').click();
  await expect(page.getByTestId('sd-delivery-edit-quantity-1')).toHaveValue('2');

  await page.getByTestId('sd-delivery-edit-quantity-1').fill('3');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-error')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-edit-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-delivery-edit-quantity-1')).toHaveValue('3');
  await capture(page, '2.9-qa-save-forbidden-light-zh-Hant');

  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-stale')).toBeVisible();
  await expect(page.getByTestId('sd-delivery-edit-success')).toHaveCount(0);
  await expect(page.getByTestId('sd-delivery-edit-quantity-1')).toHaveValue('3');
  await captureAll(page, '2.9-qa-save-offline', 'sd-delivery-edit');
  page.once('dialog', dialog => dialog.accept());
  await page.getByTestId('sd-delivery-edit-refresh').click();
  await expect(page.getByTestId('sd-delivery-edit-quantity-1')).toHaveValue('2');

  await page.getByTestId('sd-delivery-edit-quantity-1').fill('4');
  await page.getByTestId('sd-delivery-edit-save').click();
  await expect(page.getByTestId('sd-delivery-edit-stale')).toBeVisible({ timeout: 10_000 });
  await expect(page.getByTestId('sd-delivery-edit-success')).toHaveCount(0);
  await capture(page, '2.9-qa-save-timeout-light-zh-Hant');
  expect(puts).toBe(3);
});

test.describe('SD-DEL-QA-007 no delivery permission', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false, permissions: [{ code: 'SAL.ORDER.VIEW', dataScope: 'Company' }] } });
  test('2.7, 2.8 and 2.9 routes are refused without reading deliveries and the menu has no delivery entry', async ({ page }) => {
    let reads = 0;
    await page.route('**/api/v1/sal/deliveries**', route => { reads++; return route.abort(); });
    for (const path of ['/sal/deliveries', '/sal/deliveries/new', '/sal/deliveries/61/edit']) {
      await page.goto(path);
      await expect(page).toHaveURL(/\/forbidden/);
    }
    await capture(page, '2.7-qa-no-permission-light-zh-Hant');
    expect(reads).toBe(0);
  });
});
