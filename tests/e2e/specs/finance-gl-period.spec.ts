import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { expectReadableText } from '../support/contrast';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

type Period = { id: number; startDate: string; endDate: string; status: 'Open' | 'Closed'; version: number;
  createdAt: string; createdBy: number; updatedAt: string | null; updatedBy: number | null };

async function capture(page: Page, name: string, screen = '6.20'): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${screen}-${name}.png`, fullPage: true, animations: 'disabled' });
}

type Write = { method: string; path: string; body: unknown };
const problem = (status: number, errorCode: string) => ({ status, json: { status, errorCode } });

/**
 * Saved periods per company (X-Company-Id), read and written like the server: window overlap, status, one sort
 * field; writes check version, inclusive overlap and Posted dates (`posted`), and return `{ period, changed }`.
 */
async function mockStore(page: Page, companies: Record<string, Period[]>, posted: string[] = [], writes: Write[] = []) {
  const reads: URL[] = [];
  let nextId = 100;
  await page.route('**/api/v1/gl/periods**', route => {
    const request = route.request(); const url = new URL(request.url());
    const rows = companies[request.headers()['x-company-id'] ?? '1'] ??= [];
    if (request.method() !== 'GET') {
      const body = request.postDataJSON() as { version?: number; startDate?: string; endDate?: string } | null;
      writes.push({ method: request.method(), path: url.pathname + url.search, body });
      const match = /\/periods\/(\d+)(?:\/(close|reopen))?$/.exec(url.pathname);
      const row = match ? rows.find(item => item.id === Number(match[1])) : undefined;
      const overlaps = (start: string, end: string, self?: number) => rows.some(item => item.id !== self && item.startDate <= end && start <= item.endDate);
      const stamp = (item: Period) => { item.version++; item.updatedAt = '2026-11-13T19:30:00'; item.updatedBy = 1; };
      if (!match) {
        if (overlaps(body!.startDate!, body!.endDate!)) return route.fulfill(problem(409, 'FI.GL_PERIOD.OVERLAP'));
        const created: Period = { id: nextId++, startDate: body!.startDate!, endDate: body!.endDate!, status: 'Open', version: 1,
          createdAt: '2026-11-13T19:30:00', createdBy: 1, updatedAt: null, updatedBy: null };
        rows.push(created);
        return route.fulfill({ status: 201, json: { period: created, changed: true } });
      }
      if (!row) return route.fulfill(problem(404, 'FI.GL_PERIOD.NOT_FOUND'));
      const version = request.method() === 'DELETE' ? Number(url.searchParams.get('version')) : body!.version;
      if (version !== row.version) return route.fulfill(problem(409, 'COMMON.CONCURRENCY.CONFLICT'));
      const holds = (start: string, end: string) => posted.filter(date => row.startDate <= date && date <= row.endDate)
        .every(date => start <= date && date <= end);
      if (request.method() === 'DELETE') {
        if (!holds('9999-12-31', '0000-01-01')) return route.fulfill(problem(422, 'FI.GL_PERIOD.CONTAINS_POSTED'));
        rows.splice(rows.indexOf(row), 1);
        return route.fulfill({ status: 204 });
      }
      if (match[2]) {
        const target = match[2] === 'close' ? 'Closed' : 'Open';
        const changed = row.status !== target;
        if (changed) { row.status = target; stamp(row); }
        return route.fulfill({ json: { period: row, changed } });
      }
      if (!holds(body!.startDate!, body!.endDate!)) return route.fulfill(problem(422, 'FI.GL_PERIOD.EXCLUDES_POSTED'));
      if (overlaps(body!.startDate!, body!.endDate!, row.id)) return route.fulfill(problem(409, 'FI.GL_PERIOD.OVERLAP'));
      const changed = row.startDate !== body!.startDate || row.endDate !== body!.endDate;
      if (changed) { row.startDate = body!.startDate!; row.endDate = body!.endDate!; stamp(row); }
      return route.fulfill({ json: { period: row, changed } });
    }
    reads.push(url);
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
  await expectReadableText(page.getByTestId('fi-gl-period-validation'));
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

test('FI-GL-UI-010/011/014 creates, changes dates, closes, reopens and deletes; unchanged saves are warnings', async ({ page }) => {
  const saved = seed(); const writes: Write[] = [];
  await mockStore(page, { '1': saved }, [], writes);
  await page.goto('/gl/periods');
  await expectRowsMatch(page, saved, [22, 21, 20]);
  await page.getByTestId('fi-gl-period-create').click();
  await expect(page.getByTestId('fi-gl-period-form-state')).toContainText('新期間一律為「開啟」');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('fi-gl-period-start-error')).toContainText('請輸入有效的開始日');
  await expect(page.getByTestId('fi-gl-period-form-start')).toBeFocused();
  await page.getByTestId('fi-gl-period-form-start').fill('2027-01-31');
  await page.getByTestId('fi-gl-period-form-end').fill('2027-01-01');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('fi-gl-period-end-error')).toContainText('不可早於開始日');
  await expectReadableText(page.locator('.field-error'));
  await expect(page.getByTestId('fi-gl-period-form-end')).toHaveClass(/ant-input-status-error/);
  await expect(page.getByTestId('fi-gl-period-form-start')).not.toHaveClass(/ant-input-status-error/);
  await capture(page, 'validation-light-zh-Hant', '6.21');
  expect(writes).toHaveLength(0);
  await page.getByTestId('fi-gl-period-form-start').fill('2027-01-01');
  await page.getByTestId('fi-gl-period-form-end').fill('2027-01-31');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  // Screen: success style with saved dates and version. Data: the store holds exactly this Open row and the list shows it.
  await expect(page.getByTestId('fi-gl-period-notice')).toHaveAttribute('data-type', 'success');
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('已新增期間 2027-01-01 – 2027-01-31，狀態為「開啟」（第 1 版）');
  expect(writes.at(-1)).toEqual({ method: 'POST', path: '/api/v1/gl/periods', body: { startDate: '2027-01-01', endDate: '2027-01-31' } });
  expect(saved.find(row => row.id === 100)).toMatchObject({ status: 'Open', version: 1 });
  await expectRowsMatch(page, saved, [100, 22, 21, 20]);
  await capture(page, 'created-light-zh-Hant', '6.21');

  // Same dates again: valid, nothing saved -> warning, version unchanged.
  await page.getByTestId('fi-gl-period-edit-22').click();
  await expect(page.getByTestId('fi-gl-period-form-state')).toContainText('第 2 版');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('fi-gl-period-notice')).toHaveAttribute('data-type', 'warning');
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('沒有儲存任何變更');
  expect(writes.at(-1)!.body).toEqual({ version: 2, startDate: '2026-12-01', endDate: '2026-12-31' });
  await expect(page.getByTestId('fi-gl-period-version-22')).toHaveText('2');
  await capture(page, 'unchanged-light-zh-Hant', '6.21');

  await page.getByTestId('fi-gl-period-edit-22').click();
  await page.getByTestId('fi-gl-period-form-end').fill('2026-12-30');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('已儲存期間日期：2026-12-01 – 2026-12-30（第 3 版）');
  await expect(page.getByTestId('fi-gl-period-end-22')).toHaveText('2026-12-30');
  await expect(page.getByTestId('fi-gl-period-version-22')).toHaveText('3');

  await page.getByTestId('fi-gl-period-close-21').click();
  await expect(page.getByTestId('erp-message-content')).toContainText('期間 2026-11-01 – 2026-11-30');
  await capture(page, 'close-confirm-light-zh-Hant', '6.21');
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('已關閉（第 2 版）');
  await expect(page.getByTestId('fi-gl-period-state-21')).toHaveText('已關閉');
  expect(saved.find(row => row.id === 21)!.status).toBe('Closed');
  await expect(page.getByTestId('fi-gl-period-reopen-21')).toBeVisible();
  await expect(page.getByTestId('fi-gl-period-close-21')).toHaveCount(0);

  await page.getByTestId('fi-gl-period-reopen-20').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('fi-gl-period-state-20')).toHaveText('開啟');
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('已重開（第 4 版）');

  await page.getByTestId('fi-gl-period-delete-100').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('已刪除期間 2027-01-01 – 2027-01-31');
  await expect(page.getByTestId('fi-gl-period-row-100')).toHaveCount(0);
  expect(saved.some(row => row.id === 100)).toBe(false);
  expect(writes.map(write => `${write.method} ${write.path}`)).toEqual(['POST /api/v1/gl/periods', 'PUT /api/v1/gl/periods/22',
    'PUT /api/v1/gl/periods/22', 'POST /api/v1/gl/periods/21/close', 'POST /api/v1/gl/periods/20/reopen',
    'DELETE /api/v1/gl/periods/100?version=1']);
  await chooseTheme(page, 'dark'); await capture(page, 'deleted-dark-zh-Hant', '6.21');
  await chooseLanguage(page, 'en');
  await page.getByTestId('fi-gl-period-close-22').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('is now Closed (version 4)');
  await page.getByTestId('fi-gl-period-edit-22').click();
  await expect(page.getByTestId('fi-gl-period-form-state')).toContainText('the period stays Closed');
  await capture(page, 'edit-dark-en', '6.21');
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
  await chooseLanguage(page, 'zh-Hans');
  await page.getByTestId('fi-gl-period-create').click();
  await expect(page.getByTestId('fi-gl-period-form-state')).toContainText('新期间一律为“开启”');
  await capture(page, 'create-dark-zh-Hans', '6.21');
});

test('FI-GL-UI-013/016/017/018 overlap, stale version, Posted protection and missing period explain what happened and change nothing', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422|404)/);
  const saved = seed(); const writes: Write[] = [];
  await mockStore(page, { '1': saved }, ['2026-11-15'], writes);
  await page.goto('/gl/periods');
  await expectRowsMatch(page, saved, [22, 21, 20]);
  await chooseTheme(page, 'dark');
  await page.getByTestId('fi-gl-period-create').click();
  await page.getByTestId('fi-gl-period-form-start').fill('2026-12-31');
  await page.getByTestId('fi-gl-period-form-end').fill('2027-01-31');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  // The last day of December belongs to period 22: inclusive overlap keeps the dialog and the entered dates.
  await expect(page.getByTestId('fi-gl-period-save-error')).toContainText('本公司另有期間涵蓋其中至少一天');
  await expect(page.getByTestId('fi-gl-period-form-start')).toHaveValue('2026-12-31');
  expect(saved).toHaveLength(3);
  await capture(page, 'overlap-dark-zh-Hant', '6.21');
  await page.getByTestId('fi-gl-period-form-start').fill('2027-01-01');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('fi-gl-period-notice')).toContainText('2027-01-01 – 2027-01-31');

  await page.getByTestId('fi-gl-period-edit-21').click();
  await page.getByTestId('fi-gl-period-form-start').fill('2026-11-16');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('fi-gl-period-save-error')).toContainText('此期間內有已過帳傳票');
  expect(saved.find(row => row.id === 21)).toMatchObject({ startDate: '2026-11-01', version: 1 });
  await capture(page, 'excludes-posted-dark-zh-Hant', '6.21');
  await page.getByRole('dialog').getByRole('button', { name: '取消' }).click();
  await page.getByRole('dialog').last().getByRole('button', { name: '離開' }).click();

  await page.getByTestId('fi-gl-period-delete-21').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('fi-gl-period-action-error')).toContainText('此期間含有已過帳傳票，不可刪除');
  await expect(page.getByTestId('fi-gl-period-action-target')).toHaveText('期間 2026-11-01 – 2026-11-30：');
  await expect(page.getByTestId('fi-gl-period-row-21')).toBeVisible();
  await expect(page.getByTestId('fi-gl-period-notice')).toHaveCount(0);
  await capture(page, 'contains-posted-dark-zh-Hant', '6.21');

  // Another user changes period 22 while this screen still holds version 2.
  saved.find(row => row.id === 22)!.version = 5;
  await page.getByTestId('fi-gl-period-edit-22').click();
  await page.getByTestId('fi-gl-period-form-end').fill('2026-12-30');
  await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
  await expect(page.getByTestId('fi-gl-period-action-error')).toContainText('重新載入');
  await expect(page.getByTestId('fi-gl-period-action-target')).toHaveText('期間 2026-12-01 – 2026-12-31：');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByTestId('fi-gl-period-version-22')).toHaveText('5');
  await expect(page.getByTestId('fi-gl-period-end-22')).toHaveText('2026-12-31');
  await capture(page, 'stale-dark-zh-Hant', '6.21');
  await chooseTheme(page, 'light'); await capture(page, 'stale-light-zh-Hant', '6.21');

  // Removed elsewhere: 404 explains and the re-read list no longer shows it.
  saved.splice(saved.findIndex(row => row.id === 20), 1);
  await page.getByTestId('fi-gl-period-reopen-20').click();
  await page.getByTestId('erp-message-ok').click();
  await expect(page.getByTestId('fi-gl-period-action-error')).toContainText('目前公司找不到此會計期間');
  await expect(page.getByTestId('fi-gl-period-row-20')).toHaveCount(0);
});

test.describe('FI-GL-UI-015 close-only role', () => {
  test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
    companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.PERIOD.VIEW', dataScope: 'Company' }, { code: 'GL.PERIOD.CLOSE', dataScope: 'Company' }] } });
  test('sees only Close on Open rows and no create, date edit, reopen or delete', async ({ page }) => {
    const saved = seed(); await mockStore(page, { '1': saved });
    await page.goto('/gl/periods');
    await expectRowsMatch(page, saved, [22, 21, 20]);
    await expect(page.getByTestId('fi-gl-period-close-21')).toBeVisible();
    await expect(page.getByTestId('fi-gl-period-create')).toHaveCount(0);
    await expect(page.locator('[data-testid^="fi-gl-period-edit-"], [data-testid^="fi-gl-period-reopen-"], [data-testid^="fi-gl-period-delete-"]')).toHaveCount(0);
    await capture(page, 'close-only-light-zh-Hant', '6.21');
  });
});
