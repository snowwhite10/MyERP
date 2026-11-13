import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

type Line = { sequence: number; accountId: number; accountCode: string; accountName: string; accountVersion: number;
  mappingVersion: number; debit: string; credit: string };
type Journal = { id: number; periodId: number; businessDate: string; baseCurrency: string; status: 'Posted'; totalDebit: string;
  totalCredit: string; sourceModule: string; sourceType: string; sourceId: number; sourceVersion: number; createdAt: string;
  createdBy: number; lines: Line[] };

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.22-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** Server JSON with decimal amounts as raw JSON numbers (every saved digit), like System.Text.Json writes decimal. */
const raw = (value: unknown) => JSON.stringify(value).replace(/"(totalDebit|totalCredit|debit|credit)":"([^"]+)"/g, '"$1":$2');
const json = (body: unknown, status = 200) => ({ status, contentType: 'application/json', body: raw(body) });
const header = ({ lines: _lines, ...rest }: Journal) => rest;

const periods = [
  { id: 20, startDate: '2026-10-01', endDate: '2026-10-31', status: 'Closed', version: 3, createdAt: '2026-09-20T09:15:00', createdBy: 1, updatedAt: null, updatedBy: null },
  { id: 21, startDate: '2026-11-01', endDate: '2026-11-30', status: 'Open', version: 1, createdAt: '2026-10-20T09:15:00', createdBy: 1, updatedAt: null, updatedBy: null },
];

const line = (sequence: number, accountCode: string, accountName: string, debit: string, credit: string): Line =>
  ({ sequence, accountId: 100 + sequence, accountCode, accountName, accountVersion: sequence, mappingVersion: 2, debit, credit });

const seed = (): Journal[] => [
  { id: 31, periodId: 20, businessDate: '2026-10-31', baseCurrency: 'TWD', status: 'Posted', totalDebit: '52500.0000', totalCredit: '52500.0000',
    sourceModule: 'PUR', sourceType: 'SupplierInvoice', sourceId: 501, sourceVersion: 1, createdAt: '2026-10-31T17:40:00', createdBy: 3,
    lines: [line(1, '5100', '進貨', '50000.0000', '0.0000'), line(2, '1150', '進項稅額', '2500.0000', '0.0000'), line(3, '2100', '應付帳款', '0.0000', '52500.0000')] },
  { id: 32, periodId: 21, businessDate: '2026-11-03', baseCurrency: 'TWD', status: 'Posted', totalDebit: '999999999999999.9999', totalCredit: '999999999999999.9999',
    sourceModule: 'SD', sourceType: 'Invoice', sourceId: 9, sourceVersion: 2, createdAt: '2026-11-03T10:20:30', createdBy: 4,
    lines: [line(1, '1100', '應收帳款', '999999999999999.9999', '0.0000'), line(2, '4100', '銷貨收入（舊名稱）', '0.0000', '999999999999999.9999')] },
  { id: 33, periodId: 21, businessDate: '2026-11-12', baseCurrency: 'TWD', status: 'Posted', totalDebit: '1250000.5000', totalCredit: '1250000.5000',
    sourceModule: 'SD', sourceType: 'Invoice', sourceId: 10, sourceVersion: 1, createdAt: '2026-11-12T09:00:00', createdBy: 4,
    lines: [line(1, '1100', '應收帳款', '1250000.5000', '0.0000'), line(2, '4100', '銷貨收入', '0.0000', '1250000.5000')] },
];

/** Saved journals per company, filtered and sorted like GlReadService (inclusive dates, saved source, account code). */
async function mockStore(page: Page, companies: Record<string, Journal[]>) {
  const reads: URL[] = [];
  await page.route('**/api/v1/gl/periods**', route => route.fulfill({ json: { items: periods, totalCount: periods.length, page: 1, pageSize: 200 } }));
  await page.route('**/api/v1/gl/journals**', route => {
    const request = route.request(); const url = new URL(request.url());
    const rows = companies[request.headers()['x-company-id'] ?? '1'] ?? [];
    const match = /\/journals\/(\d+)$/.exec(url.pathname);
    if (match) {
      const found = rows.find(row => row.id === Number(match[1]));
      return found ? route.fulfill(json({ header: header(found), lines: found.lines }))
        : route.fulfill(json({ status: 404, errorCode: 'FI.GL_JOURNAL.NOT_FOUND' }, 404));
    }
    reads.push(url);
    const q = (name: string) => url.searchParams.get(name);
    const sort = q('sort') ?? '-businessDate'; const descending = sort.startsWith('-');
    const items = rows.filter(row => (!q('periodId') || row.periodId === Number(q('periodId')))
      && (!q('from') || row.businessDate >= q('from')!) && (!q('to') || row.businessDate <= q('to')!)
      && (!q('sourceModule') || row.sourceModule === q('sourceModule')) && (!q('sourceType') || row.sourceType === q('sourceType'))
      && (!q('sourceId') || row.sourceId === Number(q('sourceId')))
      && (!q('accountCode') || row.lines.some(item => item.accountCode === q('accountCode'))))
      .sort((a, b) => (descending ? -1 : 1) * (sort.endsWith('id') ? a.id - b.id : a.businessDate.localeCompare(b.businessDate) || a.id - b.id))
      .map(header);
    return route.fulfill(json({ items, totalCount: items.length, page: 1, pageSize: 20 }));
  });
  return reads;
}

/** Every row on screen equals the saved journal the API returned, in the order shown. */
async function expectRowsMatch(page: Page, saved: Journal[], ids: number[], amounts: Record<number, string>) {
  await expect(page.locator('[data-testid^="fi-gl-journal-row-"]')).toHaveCount(ids.length);
  for (const [index, id] of ids.entries()) {
    const row = saved.find(item => item.id === id)!;
    await expect(page.locator('[data-testid^="fi-gl-journal-row-"]').nth(index)).toHaveAttribute('data-testid', `fi-gl-journal-row-${id}`);
    await expect(page.getByTestId(`fi-gl-journal-date-${id}`)).toHaveText(row.businessDate);
    await expect(page.getByTestId(`fi-gl-journal-source-${id}`)).toHaveText(`${row.sourceModule} / ${row.sourceType} #${row.sourceId} v${row.sourceVersion}`);
    await expect(page.getByTestId(`fi-gl-journal-debit-${id}`)).toHaveText(`${amounts[id]} TWD`);
    await expect(page.getByTestId(`fi-gl-journal-credit-${id}`)).toHaveText(`${amounts[id]} TWD`);
  }
}

const AMOUNTS = { 31: '52,500', 32: '999,999,999,999,999.9999', 33: '1,250,000.5' };

test('FI-GL-JNL-UI-001/006 lists Posted journals and opens the saved snapshot in three languages and both themes', async ({ page }) => {
  const saved = seed();
  await mockStore(page, { '1': saved });
  await page.goto('/');
  await page.getByTestId('layout-menu-fi').click();
  await page.getByTestId('layout-menu-fi-gl-journals').click();
  await expect(page).toHaveURL(/\/gl\/journals/);
  await expectRowsMatch(page, saved, [33, 32, 31], AMOUNTS);
  await expect(page.getByTestId('fi-gl-journal-period-31')).toHaveText('2026-10-01 – 2026-10-31');
  // UX P3: dates and period ranges stay on one line instead of breaking at the hyphen.
  for (const id of ['fi-gl-journal-date-33', 'fi-gl-journal-period-33', 'fi-gl-journal-posted-33'])
    expect(await page.getByTestId(id).evaluate(cell => getComputedStyle(cell).whiteSpace)).toBe('nowrap');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await page.getByTestId('fi-gl-journal-open-32').click();
  await expect(page).toHaveURL(/journal=32/);
  await expect(page.getByTestId('fi-gl-journal-detail-status')).toHaveText('已過帳');
  await expect(page.getByTestId('fi-gl-journal-line-name-2')).toHaveText('銷貨收入（舊名稱）');
  await expect(page.getByTestId('fi-gl-journal-line-debit-1')).toHaveText('999,999,999,999,999.9999');
  await expect(page.getByTestId('fi-gl-journal-line-credit-1')).toHaveText('');
  await expect(page.getByTestId('fi-gl-journal-total-credit')).toHaveText('999,999,999,999,999.9999');
  await expect(page.getByTestId('fi-gl-journal-detail').locator('button')).toHaveCount(0);
  await capture(page, 'detail-dark-zh-Hant');
  // The theme menu sits under the modal overlay, so close, switch and reopen.
  await page.getByTestId('fi-gl-journal-detail-close').click();
  await chooseTheme(page, 'light');
  await page.getByTestId('fi-gl-journal-open-32').click();
  await expect(page.getByTestId('fi-gl-journal-line-debit-1')).toHaveText('999,999,999,999,999.9999');
  await capture(page, 'detail-light-zh-Hant');
  // The open journal is part of the URL, so a reload shows it again.
  await page.reload();
  await expect(page.getByTestId('fi-gl-journal-detail-source-id')).toHaveText('9');
  await page.getByTestId('fi-gl-journal-detail-close').click();
  await expect(page).not.toHaveURL(/journal=/);
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-gl-journal-debit-33')).toHaveText('1,250,000.5 TWD');
  await capture(page, 'normal-light-en');
  await chooseTheme(page, 'dark');
  await page.getByTestId('fi-gl-journal-open-31').click();
  await expect(page.getByTestId('fi-gl-journal-detail-status')).toHaveText('Posted');
  await expect(page.getByTestId('fi-gl-journal-total-debit')).toHaveText('52,500');
  await capture(page, 'detail-dark-en');
  await page.getByTestId('fi-gl-journal-detail-close').click();
  await capture(page, 'normal-dark-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-gl-journal-table')).toContainText('借方合计');
  await capture(page, 'normal-dark-zh-Hans');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-zh-Hans');
});

test('FI-GL-JNL-UI-002/008 filters by period, dates, source and account, keeps them in the URL and restores them', async ({ page }) => {
  const saved = seed(); const reads = await mockStore(page, { '1': saved });
  await page.goto('/gl/journals');
  await expectRowsMatch(page, saved, [33, 32, 31], AMOUNTS);
  await page.getByTestId('fi-gl-journal-period').selectOption('21');
  await page.getByTestId('fi-gl-journal-search').click();
  await expectRowsMatch(page, saved, [33, 32], AMOUNTS);
  await page.getByTestId('fi-gl-journal-from').fill('2026-11-03');
  await page.getByTestId('fi-gl-journal-to').fill('2026-11-03');
  await page.getByTestId('fi-gl-journal-source-module').fill(' SD ');
  await page.getByTestId('fi-gl-journal-source-id').fill('9');
  await page.getByTestId('fi-gl-journal-account').fill('4100');
  await page.getByTestId('fi-gl-journal-search').click();
  // A one-day inclusive range on the saved business date.
  await expectRowsMatch(page, saved, [32], AMOUNTS);
  const last = reads.at(-1)!.searchParams;
  expect([last.get('periodId'), last.get('sourceModule'), last.get('sourceId'), last.get('accountCode')]).toEqual(['21', 'SD', '9', '4100']);
  await expect(page).toHaveURL(/periodId=21/);
  await expect(page).toHaveURL(/accountCode=4100/);
  await page.reload();
  await expect(page.getByTestId('fi-gl-journal-period')).toHaveValue('21');
  await expect(page.getByTestId('fi-gl-journal-account')).toHaveValue('4100');
  await expectRowsMatch(page, saved, [32], AMOUNTS);
  await page.getByTestId('fi-gl-journal-reset').click();
  await expectRowsMatch(page, saved, [33, 32, 31], AMOUNTS);
  await page.getByTestId('fi-gl-journal-table').getByText('傳票', { exact: true }).click();
  await expect.poll(() => reads.at(-1)!.searchParams.get('sort')).toBe('id');
  await expectRowsMatch(page, saved, [31, 32, 33], AMOUNTS);
  await expect(page).not.toHaveURL(/periodId=/);
});

test('FI-GL-JNL-UI-004/007 distinguishes empty, filtered-empty, invalid input, server error and network loss', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/gl\/journals/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  let mode: 'ok' | 'fail' | 'offline' = 'ok'; let reads = 0;
  await page.route('**/api/v1/gl/periods**', route => route.fulfill({ json: { items: periods, totalCount: 2, page: 1, pageSize: 200 } }));
  await page.route('**/api/v1/gl/journals**', route => { reads++;
    if (mode === 'fail') return route.fulfill({ status: 500, json: { traceId: 'trace-gl-journal-500' } });
    if (mode === 'offline') return route.abort('failed');
    return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }); });
  await page.goto('/gl/journals');
  await expect(page.getByTestId('fi-gl-journal-table')).toContainText('本公司尚未有已過帳傳票');
  await capture(page, 'empty-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'empty-dark-zh-Hant');
  await page.getByTestId('fi-gl-journal-account').fill('9999');
  await page.getByTestId('fi-gl-journal-search').click();
  await expect(page.getByTestId('fi-gl-journal-table')).toContainText('沒有符合篩選條件的已過帳傳票');
  await capture(page, 'no-results-dark-zh-Hant');
  const before = reads;
  await page.getByTestId('fi-gl-journal-from').fill('2026-12-01');
  await page.getByTestId('fi-gl-journal-to').fill('2026-01-01');
  await page.getByTestId('fi-gl-journal-search').click();
  await expect(page.getByTestId('fi-gl-journal-validation')).toContainText('業務日期起不可晚於業務日期迄');
  await page.getByTestId('fi-gl-journal-reset').click();
  await page.getByTestId('fi-gl-journal-source-id').fill('0');
  await page.getByTestId('fi-gl-journal-search').click();
  await expect(page.getByTestId('fi-gl-journal-validation')).toContainText('來源 ID 必須是正整數');
  await capture(page, 'invalid-dark-zh-Hant');
  expect(reads).toBe(before + 1);
  await page.getByTestId('fi-gl-journal-source-id').fill('');
  mode = 'fail'; await page.getByTestId('fi-gl-journal-search').click();
  await expect(page.getByTestId('fi-gl-journal-error')).toContainText('trace-gl-journal-500');
  await capture(page, 'server-error-dark-zh-Hant');
  await chooseTheme(page, 'light'); await capture(page, 'server-error-light-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-gl-journal-retry').click();
  await expect(page.getByTestId('fi-gl-journal-error')).toContainText('無法連線到伺服器');
  await capture(page, 'network-error-light-zh-Hant');
  mode = 'ok'; await page.getByTestId('fi-gl-journal-retry').click();
  await expect(page.getByTestId('fi-gl-journal-error')).toHaveCount(0);
  await expect(page.getByTestId('fi-gl-journal-table')).toContainText('本公司尚未有已過帳傳票');
});

test('FI-GL-JNL-UI-007 a journal link of another company shows not found without retry', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of 404/);
  await mockStore(page, { '1': seed() });
  await page.goto('/gl/journals?journal=77');
  await expect(page.getByTestId('fi-gl-journal-detail-error')).toContainText('找不到此傳票');
  await expect(page.getByTestId('fi-gl-journal-detail-retry')).toHaveCount(0);
  await capture(page, 'not-found-light-zh-Hant');
});

test('FI-GL-JNL-UI-005 company switch re-reads that company\'s journals and drops filters and the open journal', async ({ page }) => {
  const taipei = seed();
  const hongKong: Journal[] = [{ ...seed()[2], id: 90, baseCurrency: 'HKD', sourceId: 70 }];
  await mockStore(page, { '1': taipei, '2': hongKong });
  await page.goto('/gl/journals?sourceModule=PUR&journal=31');
  await expectRowsMatch(page, taipei, [31], AMOUNTS);
  await expect(page.getByTestId('fi-gl-journal-detail-source-id')).toHaveText('501');
  await page.getByTestId('fi-gl-journal-detail-close').click();
  await page.goto('/gl/journals?sourceModule=PUR');
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.locator('[data-testid^="fi-gl-journal-row-"]')).toHaveCount(1);
  await expect(page.getByTestId('fi-gl-journal-debit-90')).toHaveText('1,250,000.5 HKD');
  await expect(page.getByTestId('fi-gl-journal-source-module')).toHaveValue('');
  await expect(page).not.toHaveURL(/sourceModule=PUR/);
});

test.describe('FI-GL-JNL-UI-005 permissions', () => {
  test.describe('no permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.PERIOD.VIEW', dataScope: 'Company' }] } });
    test('hides the menu and route without reading journals', async ({ page }) => {
      let reads = 0;
      await page.route('**/api/v1/gl/journals**', route => { reads++; return route.abort(); });
      await page.goto('/gl/journals');
      await expect(page).toHaveURL(/\/forbidden/);
      await page.getByTestId('layout-menu-fi').click();
      await expect(page.getByTestId('layout-menu-fi-gl-periods')).toBeVisible();
      await expect(page.getByTestId('layout-menu-fi-gl-journals')).toHaveCount(0);
      expect(reads).toBe(0);
      await capture(page, 'no-permission-light-zh-Hant');
    });
  });
  test.describe('journal view only', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.JOURNAL.VIEW', dataScope: 'Company' }] } });
    test('reads journals without the period list and shows period numbers', async ({ page }) => {
      const saved = seed(); let periodReads = 0;
      await mockStore(page, { '1': saved });
      await page.route('**/api/v1/gl/periods**', route => { periodReads++; return route.abort(); });
      await page.goto('/gl/journals');
      await expectRowsMatch(page, saved, [33, 32, 31], AMOUNTS);
      await expect(page.getByTestId('fi-gl-journal-period')).toHaveCount(0);
      await expect(page.getByTestId('fi-gl-journal-period-33')).toHaveText('#21');
      expect(periodReads).toBe(0);
      await capture(page, 'journal-only-light-zh-Hant');
    });
  });
});
