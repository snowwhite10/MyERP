import { E2E_AUTHORIZATION, chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';
import { mockInventoryOptions } from '../support/inventory-selection';

const OUT = '../../tests/qa/INV/screenshots';
const sample = { id: 7, itemId: 11, itemCode: 'BOLT', itemName: 'Bolt', itemActive: true,
  warehouseId: 3, warehouseCode: 'WH', warehouseName: 'Main', warehouseActive: true,
  minimumQuantity: 5, onHand: 3, isLow: true, shortage: 2, version: 1 };

for (const theme of ['light', 'dark'] as const) {
  test(`INV-SAF-001/011/012 safety stock ${theme}`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of (400|409|500)/);
    pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
    pageErrors.allowApi5xx(/\/api\/v1\/inv\/safety-stock-thresholds(?:\?.*)?$/, 500);
    let rows = [sample]; let failure: 0 | 400 | 500 | 'network' = 0; let conflict = false; let lastBody: unknown;
    await page.route('**/api/v1/inv/safety-stock-thresholds**', route => {
      const request = route.request();
      if (request.method() === 'PUT') {
        lastBody = request.postDataJSON();
        if (conflict) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT' } });
        rows = [{ ...rows[0], minimumQuantity: 6, shortage: 3, version: 2 }];
        return route.fulfill({ json: { id: 7, itemId: 11, warehouseId: 3, minimumQuantity: 6, version: 2 } });
      }
      if (request.method() === 'GET') {
        if (failure === 'network') return route.abort('failed');
        if (failure) return route.fulfill({ status: failure, json: { errorCode: 'COMMON.SERVER.ERROR', traceId: 'trace-safety-7' } });
        const url = new URL(request.url());
        const visible = url.searchParams.get('keyword') ? [] : rows;
        return route.fulfill({ json: { items: visible, page: 1, pageSize: 20, totalCount: visible.length,
          queriedAt: '2026-10-16T20:00:00' } });
      }
      return route.fulfill({ status: 400, json: {} });
    });
    await page.goto('/inv/safety-stock');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await expect(page.getByTestId('inv-safety-row-7')).toContainText('2');
    await expect(page.getByTestId('inv-safety-page')).toContainText('尚未建立採購單');
    await page.screenshot({ path: `${OUT}/5.26-normal-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('inv-safety-row-7').getByRole('button', { name: '修改門檻' }).click();
    await page.getByTestId('inv-safety-quantity').fill('0');
    await page.getByTestId('inv-safety-save').click();
    await expect(page.getByTestId('inv-safety-form-error')).toContainText('請輸入大於 0、最多六位小數的門檻');
    await expect(page.getByTestId('inv-safety-quantity')).toBeFocused();
    await page.screenshot({ path: `${OUT}/5.26-validation-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('inv-safety-quantity').fill('6'); conflict = true;
    await page.getByTestId('inv-safety-save').click();
    await expect(page.getByTestId('inv-safety-form-error')).toContainText('資料已被他人修改');
    await expect(page.getByTestId('inv-safety-row-7')).toContainText('5');
    await page.screenshot({ path: `${OUT}/5.26-conflict-${theme}-zh-Hant.png`, fullPage: true });
    conflict = false; await page.getByTestId('inv-safety-save').click();
    await expect(page.getByText('門檻已儲存。')).toBeVisible();
    expect(lastBody).toEqual({ version: 1, minimumQuantity: '6' });
    await expect(page.getByTestId('inv-safety-row-7')).toContainText('6');
    await page.waitForTimeout(250); // Let the nz-message entrance animation finish before the QA capture.
    await page.screenshot({ path: `${OUT}/5.26-success-${theme}-zh-Hant.png`, fullPage: true });
    await expect(page.getByText('門檻已儲存。')).toHaveCount(0);
    await page.getByTestId('inv-safety-keyword').fill('missing');
    await page.getByTestId('inv-safety-search').click();
    await expect(page.getByTestId('inv-safety-table')).toContainText('目前條件沒有符合的門檻');
    await page.screenshot({ path: `${OUT}/5.26-no-results-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('inv-safety-keyword').fill(''); rows = [];
    await page.getByTestId('inv-safety-search').click();
    await expect(page.getByTestId('inv-safety-table')).toContainText('尚未設定安全庫存門檻');
    await page.screenshot({ path: `${OUT}/5.26-empty-${theme}-zh-Hant.png`, fullPage: true });
    failure = 500; await page.getByTestId('inv-safety-search').click();
    await expect(page.getByTestId('inv-safety-error')).toBeVisible();
    await expect(page.getByTestId('inv-safety-page')).toContainText('trace-safety-7');
    await expect(page.getByTestId('inv-safety-table')).toContainText('載入失敗');
    await expect(page.getByTestId('inv-safety-table')).not.toContainText('尚未設定安全庫存門檻');
    await page.screenshot({ path: `${OUT}/5.26-error500-${theme}-zh-Hant.png`, fullPage: true });
    failure = 'network'; await page.getByRole('button', { name: '重試' }).click();
    await expect(page.getByTestId('inv-safety-error')).toContainText('無法連線到伺服器');
    await page.screenshot({ path: `${OUT}/5.26-network-${theme}-zh-Hant.png`, fullPage: true });
    failure = 400; await page.getByRole('button', { name: '重試' }).click();
    await expect(page.getByTestId('inv-safety-error')).toContainText('輸入資料不正確');
    await page.screenshot({ path: `${OUT}/5.26-error400-${theme}-zh-Hant.png`, fullPage: true });
    if (theme === 'light') {
      failure = 0; rows = [sample]; await page.getByRole('button', { name: '重試' }).click();
      await chooseLanguage(page, 'en');
      await expect(page.getByRole('heading', { level: 1 })).toHaveText('Safety stock');
      await page.screenshot({ path: `${OUT}/5.26-normal-light-en.png`, fullPage: true });
      await chooseLanguage(page, 'zh-Hans');
      await expect(page.getByRole('heading', { level: 1 })).toHaveText('安全库存');
      await page.screenshot({ path: `${OUT}/5.26-normal-light-zh-Hans.png`, fullPage: true });
    }
  });
}

test.describe('INV-SAF-006 permissions', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false,
    permissions: [{ code: 'INV.SAFETY_STOCK.VIEW', dataScope: 'Company' }] } });
  test('VIEW role can read but cannot edit', async ({ page }) => {
    await page.route('**/api/v1/inv/safety-stock-thresholds**', route => route.fulfill({ json: {
      items: [sample], totalCount: 1, page: 1, pageSize: 20, queriedAt: '2026-10-16T20:00:00' } }));
    await page.goto('/inv/safety-stock');
    await expect(page.getByTestId('inv-safety-row-7')).toBeVisible();
    await expect(page.getByTestId('inv-safety-new')).toHaveCount(0);
    await expect(page.getByTestId('inv-safety-row-7').getByRole('button')).toHaveCount(0);
  });
});

test.describe('INV-SAF-006 no VIEW permission', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [] } });
  for (const theme of ['light', 'dark'] as const) {
    test(`shows 403 before requesting thresholds ${theme}`, async ({ page }) => {
      let requests = 0;
      await page.route('**/api/v1/inv/safety-stock-thresholds**', route => {
        requests++;
        return route.fulfill({ json: { items: [], totalCount: 0 } });
      });
      await page.goto('/inv/safety-stock');
      if (theme === 'dark') await chooseTheme(page, 'dark');
      await expect(page.getByTestId('error-page-403')).toBeVisible();
      expect(requests).toBe(0);
      await page.screenshot({ path: `${OUT}/5.26-denied-${theme}-zh-Hant.png`, fullPage: true });
    });
  }
});

test('INV-SAF-003/004/007 creates, rejects inactive reference, then removes without stock change', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
  await mockInventoryOptions(page, { items: [{ id: 11, code: 'BOLT', name: 'Bolt' }],
    warehouses: [{ id: 3, code: 'WH', name: 'Main' }] });
  let rows: typeof sample[] = []; let inactive = true; let writes = 0;
  await page.route('**/api/v1/inv/safety-stock-thresholds**', route => {
    const method = route.request().method();
    if (method === 'POST') {
      if (inactive) return route.fulfill({ status: 422, json: { errorCode: 'MD.ITEM.INACTIVE' } });
      rows = [sample]; writes++;
      return route.fulfill({ status: 201, json: { id: 7, itemId: 11, warehouseId: 3, minimumQuantity: 5, version: 1 } });
    }
    if (method === 'DELETE') { rows = []; writes++; return route.fulfill({ status: 204, body: '' }); }
    return route.fulfill({ json: { items: rows, page: 1, pageSize: 20, totalCount: rows.length,
      queriedAt: '2026-10-16T20:00:00' } });
  });
  await page.goto('/inv/safety-stock');
  await page.getByTestId('inv-safety-new').click();
  await page.getByTestId('inv-safety-save').click();
  await expect(page.getByTestId('inv-safety-form-error')).toBeVisible();
  expect(writes).toBe(0);
  await page.getByTestId('inv-safety-pick-item').click();
  await page.getByTestId('erp-select-row-11').click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('inv-safety-pick-warehouse').click();
  await page.getByTestId('erp-select-row-3').click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('inv-safety-quantity').fill('5');
  await page.getByTestId('inv-safety-save').click();
  await expect(page.getByTestId('inv-safety-form-error')).toContainText('物料已停用');
  expect(writes).toBe(0);
  inactive = false;
  await page.getByTestId('inv-safety-save').click();
  await expect(page.getByTestId('inv-safety-row-7')).toContainText('BOLT');
  expect(writes).toBe(1);
  await page.getByTestId('inv-safety-row-7').getByRole('button', { name: '移除' }).click();
  await expect(page.getByTestId('erp-message-content')).toContainText('庫存不會變更');
  await page.getByTestId('erp-message-cancel').click();
  expect(writes).toBe(1);
  await page.getByTestId('inv-safety-row-7').getByRole('button', { name: '移除' }).click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('inv-safety-table')).toContainText('尚未設定安全庫存門檻');
  expect(writes).toBe(2);
});

for (const theme of ['light', 'dark'] as const) {
test(`INV-SAF-011 times out and retries without claiming an empty threshold set ${theme}`, async ({ page }) => {
  test.setTimeout(45000);
  let slow = true;
  await page.route('**/api/v1/inv/safety-stock-thresholds**', async route => {
    if (slow) {
      await new Promise(resolve => setTimeout(resolve, 16000));
      await route.fulfill({ json: { items: [], totalCount: 0 } }).catch(() => undefined);
      return;
    }
    await route.fulfill({ json: { items: [sample], totalCount: 1, page: 1, pageSize: 20,
      queriedAt: '2026-10-16T20:00:00' } });
  });
  await page.goto('/inv/safety-stock');
  if (theme === 'dark') await chooseTheme(page, 'dark');
  await expect(page.getByTestId('inv-safety-error')).toContainText('請求逾時', { timeout: 22000 });
  await expect(page.getByTestId('inv-safety-table')).toContainText('載入失敗');
  await page.screenshot({ path: `${OUT}/5.26-timeout-${theme}-zh-Hant.png`, fullPage: true });
  slow = false;
  await page.getByRole('button', { name: '重試' }).click();
  await expect(page.getByTestId('inv-safety-row-7')).toContainText('BOLT');
});
}

test('INV-SAF-007/008 keeps inactive history readable and sends filters before paging', async ({ page }) => {
  await mockInventoryOptions(page, { items: [{ id: 11, code: 'BOLT', name: 'Bolt' }],
    warehouses: [{ id: 3, code: 'WH', name: 'Main' }] });
  const requests: URL[] = [];
  const inactive = { ...sample, id: 8, itemCode: 'OLD', itemName: 'Legacy part', itemActive: false,
    warehouseCode: 'OLDWH', warehouseName: 'Closed', warehouseActive: false };
  await page.route('**/api/v1/inv/safety-stock-thresholds**', route => {
    const url = new URL(route.request().url()); requests.push(url);
    return route.fulfill({ json: { items: [sample, inactive], totalCount: 2, page: 1, pageSize: 20,
      queriedAt: '2026-10-16T20:00:00' } });
  });
  await page.goto('/inv/safety-stock');
  const old = page.getByTestId('inv-safety-row-8');
  await expect(old).toContainText('Legacy part');
  await expect(old).toContainText('物料已停用');
  await expect(old).toContainText('倉庫已停用');
  await expect(old.getByRole('button', { name: '修改門檻' })).toHaveCount(0);
  await expect(old.getByRole('button', { name: '移除' })).toBeVisible();
  await page.getByTestId('inv-safety-sort').selectOption('-shortage');
  await expect.poll(() => requests.at(-1)?.searchParams.get('sort')).toBe('-shortage');
  await page.getByTestId('inv-safety-low-only').check();
  await expect.poll(() => requests.at(-1)?.searchParams.get('lowOnly')).toBe('true');
  await page.getByTestId('inv-safety-warehouse').click();
  await page.getByTestId('erp-select-row-3').click();
  await page.getByTestId('erp-select-confirm').click();
  await expect.poll(() => requests.at(-1)?.searchParams.get('warehouseId')).toBe('3');
  expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
});

test('INV-SAF-002 zero stock is low and equality has no replenishment suggestion', async ({ page }) => {
  const zero = { ...sample, id: 9, itemCode: 'ZERO', onHand: 0, shortage: 5 };
  const equal = { ...sample, id: 10, itemCode: 'EQUAL', onHand: 5, isLow: false, shortage: 0 };
  await page.route('**/api/v1/inv/safety-stock-thresholds**', route => route.fulfill({ json: {
    items: [zero, equal], totalCount: 2, page: 1, pageSize: 20, queriedAt: '2026-10-16T20:00:00' } }));
  await page.goto('/inv/safety-stock');
  await expect(page.getByTestId('inv-safety-row-9')).toContainText('ZERO');
  await expect(page.getByTestId('inv-safety-row-9')).toContainText('低量');
  await expect(page.getByTestId('inv-safety-row-9')).toContainText('僅供參考');
  await expect(page.getByTestId('inv-safety-row-10')).toContainText('未低量');
  await expect(page.getByTestId('inv-safety-row-10')).not.toContainText('僅供參考');
  await expect(page.getByTestId('inv-safety-table')).toContainText('EQUAL');
});

test('INV-SAF-008 uses server paging and shows only the requested page', async ({ page }) => {
  const requestedPages: string[] = [];
  await page.route('**/api/v1/inv/safety-stock-thresholds**', route => {
    const requestedPage = new URL(route.request().url()).searchParams.get('page') ?? '1';
    requestedPages.push(requestedPage);
    const items = requestedPage === '1'
      ? Array.from({ length: 20 }, (_, index) => ({ ...sample, id: 7 + index,
        itemCode: index === 0 ? 'FIRST' : `ITEM${index}` }))
      : [{ ...sample, id: 27, itemCode: 'LAST' }];
    return route.fulfill({ json: { items, totalCount: 21,
      page: Number(requestedPage), pageSize: 20, queriedAt: '2026-10-16T20:00:00' } });
  });
  await page.goto('/inv/safety-stock');
  await expect(page.getByTestId('inv-safety-row-7')).toContainText('FIRST');
  await page.getByRole('listitem', { name: '2', exact: true }).click();
  await expect(page.getByTestId('inv-safety-row-27')).toContainText('LAST');
  await expect(page.getByTestId('inv-safety-row-7')).toHaveCount(0);
  expect(requestedPages).toContain('2');
});

for (const theme of ['light', 'dark'] as const) {
  test(`INV-SAF-007 inactive reference is rejected ${theme}`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 422/);
    let writes = 0;
    await page.route('**/api/v1/inv/safety-stock-thresholds**', route => {
      if (route.request().method() === 'PUT') {
        writes++;
        return route.fulfill({ status: 422, json: { errorCode: 'MD.WAREHOUSE.INACTIVE' } });
      }
      return route.fulfill({ json: { items: [sample], totalCount: 1, page: 1, pageSize: 20,
        queriedAt: '2026-10-16T20:00:00' } });
    });
    await page.goto('/inv/safety-stock');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await page.getByTestId('inv-safety-row-7').getByRole('button', { name: '修改門檻' }).click();
    await page.getByTestId('inv-safety-quantity').fill('6');
    await page.getByTestId('inv-safety-save').click();
    await expect(page.getByTestId('inv-safety-form-error')).toContainText('倉庫已停用');
    await expect(page.getByTestId('inv-safety-row-7')).toContainText('5');
    expect(writes).toBe(1);
    await page.screenshot({ path: `${OUT}/5.26-error422-${theme}-zh-Hant.png`, fullPage: true });
  });
}
