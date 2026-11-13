import { mkdir } from 'node:fs/promises';
import type { Page, Route } from '@playwright/test';
import { expectReadableText } from '../support/contrast';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

type Limit = { id: number; customerId: number; baseCurrency: string; effectiveFrom: string; effectiveTo: string;
  amount: string; status: 'Active' | 'Deactivated'; version: number };

const customers = [{ id: 7, code: 'C007', name: 'Harbor Trading', categoryId: null, isActive: true, version: 1 },
  { id: 8, code: 'C008', name: 'North Retail', categoryId: null, isActive: true, version: 1 }];

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.17-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Server-side MD labels (ADR-077), independent of the picker; ids MD cannot label (e.g. 9) get null. */
const labels = new Map<number, { code: string; name: string }>([[7, { code: 'C007', name: 'Harbor Trading' }],
  [8, { code: 'C008', name: 'North Retail' }]]);

/** In-memory FI store: every GET re-reads saved rows; amount is kept as exact decimal text like SQL decimal(19,4). */
function json(limit: Limit): string {
  const label = labels.get(limit.customerId);
  return JSON.stringify({ ...limit, amount: 0, customerCode: label?.code ?? null, customerName: label?.name ?? null })
    .replace('"amount":0', `"amount":${limit.amount}`);
}

async function mockStore(page: Page, rows: Limit[], options: { failWrite?: (route: Route) => Promise<boolean> } = {}) {
  const writes: unknown[] = [];
  await page.route('**/api/v1/md/customers**', route => route.fulfill({ json: { items: customers, totalCount: customers.length, page: 1, pageSize: 10 } }));
  await page.route('**/api/v1/ar/credit-limits**', async route => {
    const request = route.request(); const url = new URL(request.url());
    if (request.method() === 'GET') {
      const status = url.searchParams.get('status'); const customerId = Number(url.searchParams.get('customerId') ?? 0);
      const items = rows.filter(row => (!status || row.status === status) && (!customerId || row.customerId === customerId));
      return route.fulfill({ contentType: 'application/json',
        body: `{"items":[${items.map(json).join(',')}],"totalCount":${items.length},"page":1,"pageSize":20}` });
    }
    const body = request.postDataJSON(); writes.push(body);
    if (options.failWrite && await options.failWrite(route)) return;
    const id = Number(url.pathname.split('/')[5]);
    if (url.pathname.endsWith('/deactivate')) {
      const row = rows.find(item => item.id === id)!; row.status = 'Deactivated'; row.version++;
      return route.fulfill({ contentType: 'application/json', body: json(row) });
    }
    if (request.method() === 'PUT') {
      const row = rows.find(item => item.id === id)!;
      if (body.version !== row.version) return route.fulfill({ status: 409, json: { errorCode: 'COMMON.CONCURRENCY.CONFLICT', traceId: 't-409' } });
      Object.assign(row, { effectiveFrom: body.effectiveFrom, effectiveTo: body.effectiveTo, amount: body.amount, version: row.version + 1 });
      return route.fulfill({ contentType: 'application/json', body: json(row) });
    }
    const overlap = rows.some(row => row.status === 'Active' && row.customerId === body.customerId
      && row.effectiveFrom <= body.effectiveTo && body.effectiveFrom <= row.effectiveTo);
    if (overlap) return route.fulfill({ status: 409, json: { errorCode: 'FI.CREDIT.LIMIT_OVERLAP', traceId: 't-overlap' } });
    const created: Limit = { id: 100 + rows.length, customerId: body.customerId, baseCurrency: 'TWD', effectiveFrom: body.effectiveFrom,
      effectiveTo: body.effectiveTo, amount: body.amount, status: 'Active', version: 1 };
    rows.push(created);
    return route.fulfill({ status: 201, contentType: 'application/json', body: json(created) });
  });
  return writes;
}

const seed = (): Limit[] => [
  { id: 11, customerId: 7, baseCurrency: 'TWD', effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', amount: '500000.25', status: 'Active', version: 2 },
  { id: 12, customerId: 8, baseCurrency: 'TWD', effectiveFrom: '2025-01-01', effectiveTo: '2025-12-31', amount: '0', status: 'Deactivated', version: 3 },
];

test('FI-CREDIT-019 lists saved limits in three languages and both themes; history rows explain why they are read-only', async ({ page }) => {
  await mockStore(page, seed());
  await page.goto('/ar/credit-limits');
  await expect(page.getByTestId('fi-credit-amount-11')).toHaveText('500,000.25 TWD');
  // FI-CREDIT-022: rows read without the picker still show the server's MD code and name.
  await expect(page.getByTestId('fi-credit-customer-11')).toHaveText('C007 · Harbor Trading');
  await expect(page.getByTestId('fi-credit-customer-12')).toHaveText('C008 · North Retail');
  await expect(page.getByTestId('fi-credit-history-12')).toContainText('已停用的額度只作歷史');
  await expect(page.getByTestId('fi-credit-edit-12')).toHaveCount(0);
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-credit-history-12')).toContainText('cannot be edited');
  await capture(page, 'normal-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-credit-state-12')).toHaveText('已停用');
  await capture(page, 'normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hans');
});

test('FI-CREDIT-004/011 rejects invalid input without a write, then saves an exact large amount and re-reads it', async ({ page }) => {
  const rows = seed(); const writes = await mockStore(page, rows);
  await page.goto('/ar/credit-limits');
  await chooseTheme(page, 'dark'); // a modal blocks the theme menu, so pick the theme first
  await page.getByTestId('fi-credit-create').click();
  await page.getByRole('button', { name: '保存' }).click();
  await expect(page.getByTestId('fi-credit-customer-error')).toBeVisible();
  await expect(page.getByTestId('fi-credit-form-customer-pick')).toBeFocused();
  await page.getByTestId('fi-credit-form-customer-pick').click();
  await page.getByTestId('erp-select-row-8').click();
  await page.getByTestId('erp-select-confirm').click();
  await expect(page.getByTestId('fi-credit-form-customer')).toHaveText('C008 · North Retail');
  await page.getByTestId('fi-credit-effective-from').fill('2027-03-01');
  await page.getByTestId('fi-credit-effective-to').fill('2027-02-28');
  await page.getByTestId('fi-credit-amount').fill('1.23456');
  await page.getByRole('button', { name: '保存' }).click();
  await expect(page.getByTestId('fi-credit-to-error')).toContainText('不得早於生效日');
  await expect(page.getByTestId('fi-credit-effective-to')).toBeFocused();
  await expect(page.getByTestId('fi-credit-amount-error')).toBeVisible();
  await expectReadableText(page.locator('.field-error'));
  await expect(page.getByTestId('fi-credit-effective-to')).toHaveClass(/ant-input-status-error/);
  await expect(page.getByTestId('fi-credit-amount')).toHaveClass(/ant-input-status-error/);
  await expect(page.getByTestId('fi-credit-effective-from')).not.toHaveClass(/ant-input-status-error/);
  await capture(page, 'validation-dark-zh-Hant');
  expect(writes).toHaveLength(0);
  await page.getByTestId('fi-credit-effective-to').fill('2028-02-29');
  await page.getByTestId('fi-credit-amount').fill('999999999999999.9999');
  await page.getByRole('button', { name: '保存' }).click();
  await expect(page.getByTestId('fi-credit-notice')).toContainText('已新增 C008 · North Retail 的信用額度：2027-03-01 至 2028-02-29，999,999,999,999,999.9999 TWD（版本 1）');
  expect(writes).toEqual([{ customerId: 8, effectiveFrom: '2027-03-01', effectiveTo: '2028-02-29', amount: '999999999999999.9999' }]);
  // Data: the fresh list read shows the stored row with every digit.
  await expect(page.getByTestId('fi-credit-amount-102')).toHaveText('999,999,999,999,999.9999 TWD');
  expect(rows.find(row => row.id === 102)?.amount).toBe('999999999999999.9999');
  await capture(page, 'created-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'created-light-zh-Hant');
});

test('FI-CREDIT-025 shows required fields and confirms discarding an edited limit', async ({ page }) => {
  const rows = seed(); const writes = await mockStore(page, rows);
  await page.goto('/ar/credit-limits');
  await page.getByTestId('fi-credit-edit-11').click();
  for (const id of ['fi-credit-required-customer', 'fi-credit-required-from', 'fi-credit-required-to', 'fi-credit-required-amount'])
    await expect(page.getByTestId(id)).toHaveText('*');
  await capture(page, 'required-light-zh-Hant');
  await page.getByTestId('fi-credit-amount').fill('123');
  await page.getByRole('dialog').getByRole('button', { name: '取消' }).click();
  await expect(page.getByRole('dialog').last()).toContainText('未儲存的修改將會遺失');
  await capture(page, 'unsaved-light-zh-Hant');
  await page.getByRole('dialog').last().getByRole('button', { name: '繼續編輯' }).click();
  await expect(page.getByTestId('fi-credit-amount')).toHaveValue('123');
  await page.getByRole('dialog').getByRole('button', { name: '取消' }).click();
  await page.getByRole('dialog').last().getByRole('button', { name: '離開' }).click();
  await expect(page.getByTestId('fi-credit-form')).toBeHidden();
  await chooseTheme(page, 'dark');
  await page.getByTestId('fi-credit-edit-11').click();
  await expect(page.getByTestId('fi-credit-form')).toBeVisible();
  await capture(page, 'required-dark-zh-Hant');
  await page.getByRole('dialog').getByRole('button', { name: '取消' }).click();
  await chooseLanguage(page, 'en');
  await page.getByTestId('fi-credit-edit-11').click();
  await expect(page.getByTestId('fi-credit-form')).toBeVisible();
  await capture(page, 'required-dark-en');
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
  await chooseLanguage(page, 'zh-Hans');
  await page.getByTestId('fi-credit-edit-11').click();
  await expect(page.getByTestId('fi-credit-form')).toBeVisible();
  await capture(page, 'required-dark-zh-Hans');
  await page.getByRole('dialog').getByRole('button', { name: '取消' }).click();
  await expect(page.getByTestId('fi-credit-amount-11')).toHaveText('500,000.25 TWD');
  expect(writes).toHaveLength(0);
  expect(rows[0].amount).toBe('500000.25');
});

test('FI-CREDIT-007/009 explains overlap and stale version without claiming success; saved data is unchanged', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 409/);
  const rows = seed(); await mockStore(page, rows);
  await page.goto('/ar/credit-limits');
  await page.getByTestId('fi-credit-create').click();
  await page.getByTestId('fi-credit-amount').fill('abc');
  await page.getByRole('button', { name: '保存' }).click();
  await expect(page.getByTestId('fi-credit-customer-error')).toBeVisible();
  await expect(page.getByTestId('fi-credit-amount-error')).toBeVisible();
  await expectReadableText(page.locator('.field-error'));
  await expect(page.getByTestId('fi-credit-amount')).toHaveClass(/ant-input-status-error/);
  await capture(page, 'validation-light-zh-Hant');
  await page.getByTestId('fi-credit-form-customer-pick').click();
  await page.getByTestId('erp-select-row-7').click();
  await page.getByTestId('erp-select-confirm').click();
  await page.getByTestId('fi-credit-effective-from').fill('2026-12-31');
  await page.getByTestId('fi-credit-effective-to').fill('2027-06-30');
  await page.getByTestId('fi-credit-amount').fill('10');
  await page.getByRole('button', { name: '保存' }).click();
  await expect(page.getByTestId('fi-credit-save-error')).toContainText('另有啟用中的額度涵蓋其中至少一天');
  await expect(page.getByTestId('fi-credit-notice')).toHaveCount(0);
  await capture(page, 'overlap-light-zh-Hant');
  await page.getByRole('button', { name: '取消' }).click();
  await expect(page.getByRole('dialog').last()).toContainText('未儲存的修改將會遺失');
  await page.getByRole('dialog').last().getByRole('button', { name: '離開' }).click();
  expect(rows).toHaveLength(2);
  await chooseTheme(page, 'dark');

  rows[0].version = 3; // another user saved meanwhile; the screen still holds version 2
  await page.getByTestId('fi-credit-edit-11').click();
  await expect(page.getByTestId('fi-credit-customer-locked')).toBeVisible();
  await expect(page.getByTestId('fi-credit-form-customer-pick')).toHaveCount(0);
  await page.getByTestId('fi-credit-amount').fill('1');
  await page.getByRole('button', { name: '保存' }).click();
  await expect(page.getByTestId('fi-credit-action-error')).toContainText('資料已被他人修改');
  await expect(page.getByTestId('fi-credit-form')).toBeHidden();
  // Data: nothing changed and the re-read list shows the other user's version.
  expect(rows[0].amount).toBe('500000.25');
  await expect(page.getByTestId('fi-credit-amount-11')).toHaveText('500,000.25 TWD');
  await expect(page.getByTestId('fi-credit-row-11')).toContainText('3');
  await expect(page.getByTestId('fi-credit-notice')).toHaveCount(0);
  await capture(page, 'stale-dark-zh-Hant');
});

test('FI-CREDIT-019 deactivates after confirmation and the re-read row becomes history', async ({ page }) => {
  const rows = seed(); const writes = await mockStore(page, rows);
  await page.goto('/ar/credit-limits');
  await page.getByTestId('fi-credit-deactivate-11').click();
  await expect(page.getByRole('dialog')).toContainText('停用後不可重新啟用');
  await page.getByRole('dialog').getByRole('button', { name: '停用' }).click();
  await expect(page.getByTestId('fi-credit-notice')).toContainText('已停用 C007 · Harbor Trading 在 2026-01-01 至 2026-12-31 的信用額度');
  await expect(page.getByTestId('fi-credit-state-11')).toHaveText('已停用');
  await expect(page.getByTestId('fi-credit-history-11')).toBeVisible();
  expect(writes).toEqual([{ version: 2 }]);
  expect(rows[0]).toMatchObject({ status: 'Deactivated', version: 3 });
  await capture(page, 'deactivated-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'deactivated-dark-zh-Hant');
});

test('FI-CREDIT-022/023 deep-linked customer filter shows the server label; a customer MD cannot label stays an id', async ({ page }) => {
  const rows = seed();
  rows.push({ id: 13, customerId: 9, baseCurrency: 'TWD', effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', amount: '10', status: 'Active', version: 1 });
  await mockStore(page, rows);
  await page.goto('/ar/credit-limits?customerId=8');
  await expect(page.getByTestId('fi-credit-filter-customer')).toHaveText('C008 · North Retail');
  await expect(page.getByTestId('fi-credit-customer-12')).toHaveText('C008 · North Retail');
  await expect(page.getByTestId('fi-credit-row-11')).toHaveCount(0);
  await page.getByTestId('fi-credit-reset').click();
  await expect(page.getByTestId('fi-credit-customer-13')).toHaveText('客戶 #9');
  await expect(page.getByTestId('fi-credit-customer-11')).toHaveText('C007 · Harbor Trading');
  await capture(page, 'labels-light-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-credit-customer-13')).toHaveText('Customer #9');
  await chooseTheme(page, 'dark'); await capture(page, 'labels-dark-en');
});

test('FI-CREDIT-019 distinguishes empty, filtered-empty, invalid window, server error and network loss', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/ar\/credit-limits/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  let mode: 'ok' | 'fail' | 'offline' = 'ok'; let reads = 0;
  await page.route('**/api/v1/ar/credit-limits**', route => { reads++;
    if (mode === 'fail') return route.fulfill({ status: 500, json: { traceId: 'trace-credit-500' } });
    if (mode === 'offline') return route.abort('failed');
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }); });
  await page.goto('/ar/credit-limits');
  await expect(page.getByTestId('fi-credit-table')).toContainText('目前公司尚未設定任何信用額度');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await page.getByTestId('fi-credit-status').selectOption('Deactivated');
  await page.getByTestId('fi-credit-search').click();
  await expect(page.getByTestId('fi-credit-table')).toContainText('沒有符合條件的信用額度');
  await expect(page).toHaveURL(/status=Deactivated/);
  await capture(page, 'no-results-dark-zh-Hant');
  const before = reads;
  await page.getByTestId('fi-credit-from').fill('2026-12-01');
  await page.getByTestId('fi-credit-to').fill('2026-01-01');
  await page.getByTestId('fi-credit-search').click();
  await expect(page.getByTestId('fi-credit-validation')).toContainText('不得晚於');
  expect(reads).toBe(before);
  await expectReadableText(page.getByTestId('fi-credit-validation'));
  await capture(page, 'invalid-window-dark-zh-Hant');
  await page.getByTestId('fi-credit-reset').click();
  mode = 'fail'; await page.getByTestId('fi-credit-search').click();
  await expect(page.getByTestId('fi-credit-error')).toContainText('trace-credit-500');
  await capture(page, 'server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'server-error-light-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-credit-retry').click();
  await expect(page.getByTestId('fi-credit-error')).toContainText('無法連線到伺服器');
  await capture(page, 'network-error-light-zh-Hant');
  mode = 'ok'; await page.getByTestId('fi-credit-retry').click();
  await expect(page.getByTestId('fi-credit-error')).toHaveCount(0);
});

test.describe('FI-CREDIT-012 permissions', () => {
  test.describe('view only', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'AR.CREDIT.VIEW', dataScope: 'Company' }] } });
    test('shows saved rows without write actions', async ({ page }) => {
      await mockStore(page, seed());
      await page.goto('/ar/credit-limits');
      await expect(page.getByTestId('fi-credit-row-11')).toBeVisible();
      await expect(page.getByTestId('fi-credit-create')).toHaveCount(0);
      await expect(page.getByTestId('fi-credit-edit-11')).toHaveCount(0);
      await expect(page.getByTestId('fi-credit-deactivate-11')).toHaveCount(0);
      await capture(page, 'view-only-light-zh-Hant');
    });
  });
  test.describe('no permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [] } });
    test('hides the menu and route without reading limits', async ({ page }) => {
      let reads = 0;
      await page.route('**/api/v1/ar/credit-limits**', route => { reads++; return route.abort(); });
      await page.goto('/ar/credit-limits');
      await expect(page).toHaveURL(/\/forbidden/);
      await expect(page.getByTestId('layout-menu-fi-credit-limits')).toHaveCount(0);
      expect(reads).toBe(0);
      await capture(page, 'no-permission-light-zh-Hant');
      await chooseTheme(page, 'dark'); await capture(page, 'no-permission-dark-zh-Hant');
    });
  });
});
