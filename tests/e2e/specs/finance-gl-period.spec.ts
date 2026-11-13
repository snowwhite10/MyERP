import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

type Period = { id: number; startDate: string; endDate: string; status: 'Open' | 'Closed'; version: number;
  createdAt: string; createdBy: number; updatedAt: string | null; updatedBy: number | null };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.20-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Saved periods per company (X-Company-Id), filtered like the server: window overlap, status, one sort field. */
async function mockStore(page: Page, companies: Record<string, Period[]>) {
  const reads: URL[] = [];
  await page.route('**/api/v1/gl/periods**', route => {
    const request = route.request(); const url = new URL(request.url()); reads.push(url);
    const rows = companies[request.headers()['x-company-id'] ?? '1'] ?? [];
    const status = url.searchParams.get('status'); const from = url.searchParams.get('from'); const to = url.searchParams.get('to');
    const sort = url.searchParams.get('sort') ?? '-startDate'; const field = sort.replace(/^-/, '') as 'startDate' | 'endDate';
    const items = rows.filter(row => (!status || row.status === status) && (!from || row.endDate >= from) && (!to || row.startDate <= to))
      .sort((a, b) => (sort.startsWith('-') ? -1 : 1) * a[field].localeCompare(b[field]));
    return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 20 } });
  });
  return reads;
}

const seed = (): Period[] => [
  { id: 20, startDate: '2026-10-01', endDate: '2026-10-31', status: 'Closed', version: 3, createdAt: '2026-09-20T09:15:00',
    createdBy: 1, updatedAt: '2026-11-02T18:05:44', updatedBy: 2 },
  { id: 21, startDate: '2026-11-01', endDate: '2026-11-30', status: 'Open', version: 1, createdAt: '2026-10-20T09:15:00',
    createdBy: 1, updatedAt: null, updatedBy: null },
  { id: 22, startDate: '2026-12-01', endDate: '2026-12-31', status: 'Open', version: 2, createdAt: '2026-10-20T09:16:00',
    createdBy: 1, updatedAt: '2026-11-10T08:00:00', updatedBy: 1 },
];

/** Every row on screen equals the saved row the API returned (date, state, version), in the order shown. */
async function expectRowsMatch(page: Page, saved: Period[], ids: number[]) {
  await expect(page.locator('[data-testid^="fi-gl-period-row-"]')).toHaveCount(ids.length);
  for (const [index, id] of ids.entries()) {
    const row = saved.find(item => item.id === id)!;
    await expect(page.locator('[data-testid^="fi-gl-period-row-"]').nth(index)).toHaveAttribute('data-testid', `fi-gl-period-row-${id}`);
    await expect(page.getByTestId(`fi-gl-period-start-${id}`)).toHaveText(row.startDate);
    await expect(page.getByTestId(`fi-gl-period-end-${id}`)).toHaveText(row.endDate);
    await expect(page.getByTestId(`fi-gl-period-version-${id}`)).toHaveText(String(row.version));
  }
}

test('FI-GL-UI-001/006 lists saved periods newest first in three languages and both themes', async ({ page }) => {
  const saved = seed();
  await mockStore(page, { '1': saved });
  await page.goto('/');
  await page.getByTestId('layout-menu-fi').click();
  await page.getByTestId('layout-menu-fi-gl-periods').click();
  await expect(page).toHaveURL(/\/gl\/periods/);
  await expectRowsMatch(page, saved, [22, 21, 20]);
  await expect(page.getByTestId('fi-gl-period-state-20')).toHaveText('已關閉');
  await expect(page.getByTestId('fi-gl-period-closed-hint-20')).toContainText('過帳會被拒絕');
  await expect(page.getByTestId('fi-gl-period-changed-21')).toHaveText('2026-10-20 09:15');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-gl-period-state-21')).toHaveText('Open');
  await expect(page.getByTestId('fi-gl-period-closed-hint-20')).toContainText('rejected until the period is reopened');
  await capture(page, 'normal-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-gl-period-state-20')).toHaveText('已关闭');
  await capture(page, 'normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hans');
});

test('FI-GL-UI-002/008 filters by status and inclusive window, keeps it in the URL and restores it on reload', async ({ page }) => {
  const saved = seed(); const reads = await mockStore(page, { '1': saved });
  await page.goto('/gl/periods');
  await expectRowsMatch(page, saved, [22, 21, 20]);
  await page.getByTestId('fi-gl-period-status').selectOption('Open');
  await page.getByTestId('fi-gl-period-from').fill('2026-11-30');
  await page.getByTestId('fi-gl-period-to').fill('2026-11-30');
  await page.getByTestId('fi-gl-period-search').click();
  // The one-day window touches only the last day of November.
  await expectRowsMatch(page, saved, [21]);
  await expect(page).toHaveURL(/status=Open/);
  await expect(page).toHaveURL(/from=2026-11-30/);
  expect(reads.at(-1)!.searchParams.get('to')).toBe('2026-11-30');
  await page.reload();
  await expect(page.getByTestId('fi-gl-period-status')).toHaveValue('Open');
  await expectRowsMatch(page, saved, [21]);
  await page.getByTestId('fi-gl-period-table').getByText('結束日').click();
  await expect.poll(() => reads.at(-1)!.searchParams.get('sort')).toBe('endDate');
  await page.getByTestId('fi-gl-period-reset').click();
  await expectRowsMatch(page, saved, [20, 21, 22]);
  await expect(page).not.toHaveURL(/status=/);
});

test('FI-GL-UI-004/007 distinguishes empty, filtered-empty, invalid window, server error and network loss', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/gl\/periods/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  let mode: 'ok' | 'fail' | 'offline' = 'ok'; let reads = 0;
  await page.route('**/api/v1/gl/periods**', route => { reads++;
    if (mode === 'fail') return route.fulfill({ status: 500, json: { traceId: 'trace-gl-period-500' } });
    if (mode === 'offline') return route.abort('failed');
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }); });
  await page.goto('/gl/periods');
  await expect(page.getByTestId('fi-gl-period-table')).toContainText('本公司尚未設定會計期間');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await page.getByTestId('fi-gl-period-status').selectOption('Closed');
  await page.getByTestId('fi-gl-period-search').click();
  await expect(page.getByTestId('fi-gl-period-table')).toContainText('沒有符合篩選條件的期間');
  await capture(page, 'no-results-dark-zh-Hant');
  const before = reads;
  await page.getByTestId('fi-gl-period-from').fill('2026-12-01');
  await page.getByTestId('fi-gl-period-to').fill('2026-01-01');
  await page.getByTestId('fi-gl-period-search').click();
  await expect(page.getByTestId('fi-gl-period-validation')).toContainText('起日不可晚於迄日');
  expect(reads).toBe(before);
  await capture(page, 'invalid-window-dark-zh-Hant');
  await page.getByTestId('fi-gl-period-reset').click();
  await expect(page.getByTestId('fi-gl-period-validation')).toHaveCount(0);
  mode = 'fail'; await page.getByTestId('fi-gl-period-search').click();
  await expect(page.getByTestId('fi-gl-period-error')).toContainText('trace-gl-period-500');
  await capture(page, 'server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'server-error-light-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-gl-period-retry').click();
  await expect(page.getByTestId('fi-gl-period-error')).toContainText('無法連線到伺服器');
  await capture(page, 'network-error-light-zh-Hant');
  mode = 'ok'; await page.getByTestId('fi-gl-period-retry').click();
  await expect(page.getByTestId('fi-gl-period-error')).toHaveCount(0);
  // Reset cleared the filters, so the recovered read is the unfiltered empty state.
  await expect(page.getByTestId('fi-gl-period-table')).toContainText('本公司尚未設定會計期間');
});

test('FI-GL-UI-005 company switch re-reads that company\'s saved periods and clears filters', async ({ page }) => {
  const taipei = seed();
  const hongKong: Period[] = [{ id: 90, startDate: '2026-04-01', endDate: '2027-03-31', status: 'Open', version: 1,
    createdAt: '2026-03-01T10:00:00', createdBy: 5, updatedAt: null, updatedBy: null }];
  await mockStore(page, { '1': taipei, '2': hongKong });
  await page.goto('/gl/periods?status=Closed');
  await expectRowsMatch(page, taipei, [20]);
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expectRowsMatch(page, hongKong, [90]);
  await expect(page.getByTestId('fi-gl-period-status')).toHaveValue('');
  await expect(page).not.toHaveURL(/status=Closed/);
});

test.describe('FI-GL-UI-005 permissions', () => {
  test.describe('no permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'AR.CREDIT.VIEW', dataScope: 'Company' }] } });
    test('hides the menu and route without reading periods', async ({ page }) => {
      let reads = 0;
      await page.route('**/api/v1/gl/periods**', route => { reads++; return route.abort(); });
      await page.goto('/gl/periods');
      await expect(page).toHaveURL(/\/forbidden/);
      await page.getByTestId('layout-menu-fi').click();
      await expect(page.getByTestId('layout-menu-fi-credit-limits')).toBeVisible();
      await expect(page.getByTestId('layout-menu-fi-gl-periods')).toHaveCount(0);
      expect(reads).toBe(0);
      await capture(page, 'no-permission-light-zh-Hant');
    });
  });
  test.describe('view permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.PERIOD.VIEW', dataScope: 'Company' }] } });
    test('reads saved periods with the view permission only', async ({ page }) => {
      const saved = seed(); await mockStore(page, { '1': saved });
      await page.goto('/gl/periods');
      await expectRowsMatch(page, saved, [22, 21, 20]);
      await expect(page.locator('[data-testid^="fi-gl-period-row-"] button')).toHaveCount(0);
    });
  });
});
