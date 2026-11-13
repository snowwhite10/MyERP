import { execFileSync } from 'node:child_process';
import { mkdir } from 'node:fs/promises';
import { expect, request, test, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';

// WU-FI-004c4b (FI-GL-REAL-001–005): GL periods (6.20/6.21) and Posted journals (6.22) against the running API and SQL Server.
// Every screen value is checked against the row SQL Server saved (read back with sqlcmd), not only against the API response.
// Data comes from the Development demo seed (ADR-105). Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-finance-gl
const apiUrl = process.env['E2E_API_URL'];
const sqlServer = process.env['E2E_SQL_SERVER'] ?? 'localhost\\MSSQLSERVER2022';
const sqlDatabase = process.env['E2E_SQL_DATABASE'] ?? 'myerp';
const root = '/api/v1';

type SavedPeriod = { id: number; startDate: string; endDate: string; status: 'Open' | 'Closed'; version: number };
type SavedJournal = { id: number; periodId: number; businessDate: string; baseCurrency: string; status: string;
  totalDebit: string; totalCredit: string; sourceModule: string; sourceType: string; sourceId: number; sourceVersion: number;
  periodStart: string; periodEnd: string };
type SavedLine = { sequence: number; accountCode: string; accountNameHex: string; accountVersion: number; mappingVersion: number;
  debit: string; credit: string };

/**
 * Runs a read-only query with Windows authentication and returns its rows. The query must select only ASCII values
 * (sqlcmd writes the console code page), so names are selected as UTF-16 hex and decoded with {@link utf16}.
 */
function sql<T>(query: string): T[] {
  const output = execFileSync('sqlcmd', ['-S', sqlServer, '-d', sqlDatabase, '-E', '-C', '-b', '-y', '0',
    '-Q', `SET NOCOUNT ON; SELECT (${query} FOR JSON PATH, INCLUDE_NULL_VALUES) AS rows_json`], { encoding: 'utf8' });
  const text = output.replace(/\r?\n/g, '').trim();
  return text && text !== 'NULL' ? JSON.parse(text) as T[] : [];
}

const utf16 = (hex: string) => Buffer.from(hex, 'hex').toString('utf16le');

/** Screen format of a saved decimal: thousands separators, trailing zeros dropped, every other digit kept. */
function shown(value: string): string {
  const [whole, fraction = ''] = value.split('.');
  const digits = fraction.replace(/0+$/, '');
  const grouped = (whole === '' ? '0' : whole).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return digits ? `${grouped}.${digits}` : grouped;
}

const companyId = (code: string) => sql<{ id: number }>(`SELECT id FROM sys_company WHERE code = '${code}'`)[0]!.id;

function periods(company: number, where = '1 = 1'): SavedPeriod[] {
  return sql<SavedPeriod>(`SELECT id, CONVERT(char(10), start_date, 23) AS startDate, CONVERT(char(10), end_date, 23) AS endDate,
    status, version FROM gl_period WHERE company_id = ${company} AND ${where} ORDER BY start_date DESC`);
}

function demoJournal(company: number, sourceId: number): SavedJournal {
  const [journal] = sql<SavedJournal>(`SELECT j.id, j.period_id AS periodId, CONVERT(char(10), j.business_date, 23) AS businessDate,
    j.base_currency AS baseCurrency, j.status, CONVERT(varchar(40), j.total_debit) AS totalDebit,
    CONVERT(varchar(40), j.total_credit) AS totalCredit, k.source_module AS sourceModule, k.source_type AS sourceType,
    k.source_id AS sourceId, k.source_version AS sourceVersion, CONVERT(char(10), p.start_date, 23) AS periodStart,
    CONVERT(char(10), p.end_date, 23) AS periodEnd
    FROM gl_journal j JOIN gl_source_key k ON k.id = j.source_key_id JOIN gl_period p ON p.id = j.period_id
    WHERE j.company_id = ${company} AND k.source_module = 'FI' AND k.source_type = 'DemoSeed' AND k.source_id = ${sourceId}`);
  expect(journal, `demo journal ${sourceId} of company ${company}: run --seed (ADR-105)`).toBeDefined();
  return journal!;
}

function lines(journalId: number): SavedLine[] {
  return sql<SavedLine>(`SELECT sequence, account_code AS accountCode,
    CONVERT(varchar(max), CONVERT(varbinary(max), account_name), 2) AS accountNameHex, account_version AS accountVersion,
    mapping_version AS mappingVersion, CONVERT(varchar(40), debit) AS debit, CONVERT(varchar(40), credit) AS credit
    FROM gl_journal_line WHERE journal_id = ${journalId} ORDER BY sequence`);
}

async function capture(page: Page, name: string, screen: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/${screen}-real-${name}.png`, fullPage: true, animations: 'disabled' });
}

test.describe('真實 API 總帳期間與已過帳傳票（SQL 讀回雙斷言）', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(120_000);

  let consoleErrors: string[];
  let serverErrors: string[];
  let allowedConsole: RegExp[];
  // One login per user for the whole file keeps within the login rate limit; the UI login itself is real-api-login's job.
  const tokens: Partial<Record<'finance' | 'viewer', RealApiTokens>> = {};

  test.beforeAll(async () => {
    test.setTimeout(240_000); // realApiLogin may wait out the login rate-limit window
    tokens.finance = await realApiLogin(apiUrl!, 'finance', 'Demo@2026');
    tokens.viewer = await realApiLogin(apiUrl!, 'viewer', 'Demo@2026');
  });

  /** Opens the app signed in with the user's real tokens, stored the way AuthService stores them after the login page. */
  async function login(page: Page, username: 'finance' | 'viewer'): Promise<void> {
    await page.addInitScript(([key, value]) => localStorage.setItem(key, value),
      ['myerp.auth.session', JSON.stringify({ ...tokens[username], username })]);
    await page.goto('/');
    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page.getByTestId('layout-username')).toHaveText(username);
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01');
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = []; allowedConsole = [];
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    // Untranslated i18n keys (for example fi.glPeriod.title) must never reach the screen.
    expect(await page.locator('body').innerText()).not.toMatch(/\b(fi|common|layout)\.[a-z][A-Za-z]+\.[a-z][A-Za-z.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors.filter(message => !allowedConsole.some(pattern => pattern.test(message)))).toEqual([]);
  });

  test('FI-GL-REAL-001 finance sees every saved period of TW01 with its saved dates, state and version', async ({ page }) => {
    const tw01 = companyId('TW01');
    const saved = periods(tw01).slice(0, 20);
    expect(saved.length, 'TW01 demo periods (ADR-105)').toBeGreaterThanOrEqual(12);
    await login(page, 'finance');
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-gl-periods').click();
    await expect(page).toHaveURL(/\/gl\/periods/);
    await expect(page.locator('[data-testid^="fi-gl-period-row-"]')).toHaveCount(saved.length);
    for (const [index, row] of saved.entries()) {
      await expect(page.locator('[data-testid^="fi-gl-period-row-"]').nth(index)).toHaveAttribute('data-testid', `fi-gl-period-row-${row.id}`);
      await expect(page.getByTestId(`fi-gl-period-start-${row.id}`)).toHaveText(row.startDate);
      await expect(page.getByTestId(`fi-gl-period-end-${row.id}`)).toHaveText(row.endDate);
      await expect(page.getByTestId(`fi-gl-period-version-${row.id}`)).toHaveText(String(row.version));
      await expect(page.getByTestId(`fi-gl-period-state-${row.id}`)).toHaveText(row.status === 'Open' ? '開啟' : '已關閉');
    }
    // Closed rows offer Reopen, open rows offer Close: the buttons follow the saved state.
    const closed = saved.find(row => row.status === 'Closed')!; const open = saved.find(row => row.status === 'Open')!;
    await expect(page.getByTestId(`fi-gl-period-reopen-${closed.id}`)).toBeVisible();
    await expect(page.getByTestId(`fi-gl-period-close-${closed.id}`)).toHaveCount(0);
    await expect(page.getByTestId(`fi-gl-period-close-${open.id}`)).toBeVisible();
    await capture(page, 'list-light-zh-Hant', '6.20');

    // Status filter is applied by the server: the screen equals the saved Closed rows.
    await page.getByTestId('fi-gl-period-status').selectOption('Closed');
    await page.getByTestId('fi-gl-period-search').click();
    const savedClosed = periods(tw01, "status = 'Closed'").slice(0, 20);
    await expect(page.locator('[data-testid^="fi-gl-period-row-"]')).toHaveCount(savedClosed.length);
    for (const row of savedClosed) await expect(page.getByTestId(`fi-gl-period-state-${row.id}`)).toHaveText('已關閉');
  });

  test('FI-GL-REAL-002 finance creates, edits, closes, reopens and deletes a period; each step is saved in SQL Server', async ({ page }) => {
    const tw01 = companyId('TW01');
    // A far-future year no demo or user period uses; leftovers of an aborted run are removed in finally.
    const year = 2200 + Math.floor(Math.random() * 700);
    const start = `${year}-01-01`; const end = `${year}-01-31`;
    const inYear = `start_date >= '${year}-01-01' AND end_date <= '${year}-12-31'`;
    expect(periods(tw01, inYear)).toEqual([]);
    allowedConsole.push(/Failed to load resource: the server responded with a status of (409|422)/);
    await login(page, 'finance');
    await page.goto('/gl/periods');
    try {
      await page.getByTestId('fi-gl-period-create').click();
      await page.getByTestId('fi-gl-period-form-start').fill(start);
      await page.getByTestId('fi-gl-period-form-end').fill(end);
      await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
      await expect(page.getByTestId('fi-gl-period-notice')).toHaveAttribute('data-type', 'success');
      await expect(page.getByTestId('fi-gl-period-notice')).toContainText(`已新增期間 ${start} – ${end}，狀態為「開啟」（第 1 版）`);
      const [created] = periods(tw01, inYear);
      expect(created).toMatchObject({ startDate: start, endDate: end, status: 'Open', version: 1 });
      await expect(page.getByTestId(`fi-gl-period-version-${created!.id}`)).toHaveText('1');
      await capture(page, 'created-light-zh-Hant', '6.21');

      // Same dates: valid, nothing saved -> warning and the saved version stays 1.
      await page.getByTestId(`fi-gl-period-edit-${created!.id}`).click();
      await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
      await expect(page.getByTestId('fi-gl-period-notice')).toHaveAttribute('data-type', 'warning');
      await expect(page.getByTestId('fi-gl-period-notice')).toContainText('沒有儲存任何變更');
      expect(periods(tw01, inYear)[0]).toMatchObject({ endDate: end, version: 1 });

      await page.getByTestId(`fi-gl-period-edit-${created!.id}`).click();
      await page.getByTestId('fi-gl-period-form-end').fill(`${year}-01-30`);
      await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
      await expect(page.getByTestId('fi-gl-period-notice')).toContainText(`已儲存期間日期：${start} – ${year}-01-30（第 2 版）`);
      expect(periods(tw01, inYear)[0]).toMatchObject({ id: created!.id, endDate: `${year}-01-30`, status: 'Open', version: 2 });
      await expect(page.getByTestId(`fi-gl-period-end-${created!.id}`)).toHaveText(`${year}-01-30`);

      // Overlapping by one day is rejected by the server (409): the dialog keeps the input and nothing is saved.
      await page.getByTestId('fi-gl-period-create').click();
      await page.getByTestId('fi-gl-period-form-start').fill(`${year}-01-30`);
      await page.getByTestId('fi-gl-period-form-end').fill(`${year}-02-28`);
      await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
      await expect(page.getByTestId('fi-gl-period-save-error')).toContainText('本公司另有期間涵蓋其中至少一天');
      await expect(page.getByTestId('fi-gl-period-form-start')).toHaveValue(`${year}-01-30`);
      expect(periods(tw01, inYear)).toHaveLength(1);
      await capture(page, 'overlap-light-zh-Hant', '6.21');
      await page.getByRole('dialog').getByRole('button', { name: '取消' }).click();
      await page.getByRole('dialog').last().getByRole('button', { name: '離開' }).click();

      await page.getByTestId(`fi-gl-period-close-${created!.id}`).click();
      await page.getByTestId('erp-message-ok').click();
      await expect(page.getByTestId('fi-gl-period-notice')).toContainText('已關閉（第 3 版）');
      await expect(page.getByTestId(`fi-gl-period-state-${created!.id}`)).toHaveText('已關閉');
      expect(periods(tw01, inYear)[0]).toMatchObject({ status: 'Closed', version: 3 });

      await page.getByTestId(`fi-gl-period-reopen-${created!.id}`).click();
      await page.getByTestId('erp-message-ok').click();
      await expect(page.getByTestId('fi-gl-period-notice')).toContainText('已重開（第 4 版）');
      await expect(page.getByTestId(`fi-gl-period-state-${created!.id}`)).toHaveText('開啟');
      expect(periods(tw01, inYear)[0]).toMatchObject({ status: 'Open', version: 4 });

      await page.getByTestId(`fi-gl-period-delete-${created!.id}`).click();
      await page.getByTestId('erp-message-ok').click();
      await expect(page.getByTestId('fi-gl-period-notice')).toContainText(`已刪除期間 ${start} – ${year}-01-30`);
      await expect(page.getByTestId(`fi-gl-period-row-${created!.id}`)).toHaveCount(0);
      expect(periods(tw01, inYear)).toEqual([]);
      await capture(page, 'deleted-light-zh-Hant', '6.21');
    } finally {
      const left = periods(tw01, inYear);
      if (left.length) {
        const api = await request.newContext({ baseURL: apiUrl,
          extraHTTPHeaders: { Authorization: `Bearer ${tokens.finance!.accessToken}` } });
        try {
          for (const row of left) {
            const response = await api.delete(`${root}/gl/periods/${row.id}?version=${row.version}`, { headers: { 'X-Company-Id': String(tw01) } });
            expect(response.status(), await response.text()).toBe(204);
          }
        } finally { await api.dispose(); }
      }
    }
  });

  test('FI-GL-REAL-003 a period holding a Posted journal cannot be deleted or narrowed; SQL Server keeps it unchanged', async ({ page }) => {
    const tw01 = companyId('TW01');
    const journal = demoJournal(tw01, 2026111);
    const [before] = periods(tw01, `id = ${journal.periodId}`);
    allowedConsole.push(/Failed to load resource: the server responded with a status of 422/);
    await login(page, 'finance');
    await page.goto('/gl/periods');
    await page.getByTestId(`fi-gl-period-delete-${before!.id}`).click();
    await page.getByTestId('erp-message-ok').click();
    await expect(page.getByTestId('fi-gl-period-action-error')).toContainText('此期間含有已過帳傳票，不可刪除');
    await expect(page.getByTestId('fi-gl-period-action-target')).toHaveText(`期間 ${before!.startDate} – ${before!.endDate}：`);
    await expect(page.getByTestId(`fi-gl-period-row-${before!.id}`)).toBeVisible();
    await capture(page, 'contains-posted-light-zh-Hant', '6.21');

    // Moving the start past the Posted business date would leave the journal outside its period.
    const after = new Date(`${journal.businessDate}T00:00:00Z`); after.setUTCDate(after.getUTCDate() + 1);
    await page.getByTestId(`fi-gl-period-edit-${before!.id}`).click();
    await page.getByTestId('fi-gl-period-form-start').fill(after.toISOString().slice(0, 10));
    await page.getByRole('dialog').getByRole('button', { name: '儲存' }).click();
    await expect(page.getByTestId('fi-gl-period-save-error')).toContainText('此期間內有已過帳傳票');
    expect(periods(tw01, `id = ${journal.periodId}`)[0]).toEqual(before);
    expect(demoJournal(tw01, 2026111)).toEqual(journal);
  });

  test('FI-GL-REAL-004 Posted journals show the exact saved amounts, source key and line snapshots of TW01 and HK01', async ({ page }) => {
    const tw01 = companyId('TW01'); const hk01 = companyId('HK01');
    await login(page, 'finance');
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-gl-journals').click();
    await expect(page).toHaveURL(/\/gl\/journals/);
    const sales = demoJournal(tw01, 2026111);
    await expectJournal(page, sales);
    await capture(page, 'tw01-detail-light-zh-Hant', '6.22');
    await page.getByTestId('fi-gl-journal-detail-close').click();

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01' }).click();
    await expect(page.getByTestId('layout-company-select')).toContainText('HK01');
    const precision = demoJournal(hk01, 2026119);
    expect(precision.totalDebit).toBe('123456789012345.6700');
    await expectJournal(page, precision);
    await capture(page, 'hk01-precision-light-zh-Hant', '6.22');
    // The TW01 journal is not readable in HK01: the filter finds nothing.
    await page.getByTestId('fi-gl-journal-detail-close').click();
    await expect(page.locator(`[data-testid="fi-gl-journal-row-${sales.id}"]`)).toHaveCount(0);
  });

  test('FI-GL-REAL-005 viewer reads periods and journals but has no write button', async ({ page }) => {
    const tw01 = companyId('TW01');
    const saved = periods(tw01).slice(0, 20);
    await login(page, 'viewer');
    await page.goto('/gl/periods');
    await expect(page.locator('[data-testid^="fi-gl-period-row-"]')).toHaveCount(saved.length);
    await expect(page.getByTestId(`fi-gl-period-version-${saved[0]!.id}`)).toHaveText(String(saved[0]!.version));
    await expect(page.getByTestId('fi-gl-period-create')).toHaveCount(0);
    await expect(page.locator('[data-testid^="fi-gl-period-row-"] button')).toHaveCount(0);
    await capture(page, 'viewer-light-zh-Hant', '6.20');
    await page.goto('/gl/journals');
    const journal = demoJournal(tw01, 2026111);
    await expectJournal(page, journal);
    await expect(page.getByTestId('fi-gl-journal-detail').locator('button')).toHaveCount(0);
  });
});

/** Filters the journal list by the saved source key, then checks the row and the opened detail against SQL Server. */
async function expectJournal(page: Page, journal: SavedJournal): Promise<void> {
  await page.getByTestId('fi-gl-journal-source-module').fill(journal.sourceModule);
  await page.getByTestId('fi-gl-journal-source-type').fill(journal.sourceType);
  await page.getByTestId('fi-gl-journal-source-id').fill(String(journal.sourceId));
  await page.getByTestId('fi-gl-journal-search').click();
  await expect(page.locator('[data-testid^="fi-gl-journal-row-"]')).toHaveCount(1);
  const id = journal.id;
  await expect(page.getByTestId(`fi-gl-journal-row-${id}`)).toBeVisible();
  await expect(page.getByTestId(`fi-gl-journal-date-${id}`)).toHaveText(journal.businessDate);
  await expect(page.getByTestId(`fi-gl-journal-period-${id}`)).toHaveText(`${journal.periodStart} – ${journal.periodEnd}`);
  await expect(page.getByTestId(`fi-gl-journal-source-${id}`))
    .toHaveText(`${journal.sourceModule} / ${journal.sourceType} #${journal.sourceId} v${journal.sourceVersion}`);
  await expect(page.getByTestId(`fi-gl-journal-debit-${id}`)).toHaveText(`${shown(journal.totalDebit)} ${journal.baseCurrency}`);
  await expect(page.getByTestId(`fi-gl-journal-credit-${id}`)).toHaveText(`${shown(journal.totalCredit)} ${journal.baseCurrency}`);

  await page.getByTestId(`fi-gl-journal-open-${id}`).click();
  await expect(page).toHaveURL(new RegExp(`journal=${id}`));
  await expect(page.getByTestId('fi-gl-journal-detail-status')).toHaveText('已過帳');
  expect(journal.status).toBe('Posted');
  await expect(page.getByTestId('fi-gl-journal-detail-date')).toHaveText(journal.businessDate);
  await expect(page.getByTestId('fi-gl-journal-detail-period')).toHaveText(`${journal.periodStart} – ${journal.periodEnd}`);
  await expect(page.getByTestId('fi-gl-journal-detail-currency')).toHaveText(journal.baseCurrency);
  await expect(page.getByTestId('fi-gl-journal-detail-module')).toHaveText(journal.sourceModule);
  await expect(page.getByTestId('fi-gl-journal-detail-type')).toHaveText(journal.sourceType);
  await expect(page.getByTestId('fi-gl-journal-detail-source-id')).toHaveText(String(journal.sourceId));
  await expect(page.getByTestId('fi-gl-journal-detail-source-version')).toHaveText(String(journal.sourceVersion));
  const saved = lines(id);
  expect(saved.length).toBeGreaterThanOrEqual(2);
  await expect(page.locator('[data-testid^="fi-gl-journal-line-code-"]')).toHaveCount(saved.length);
  for (const line of saved) {
    const n = line.sequence;
    await expect(page.getByTestId(`fi-gl-journal-line-code-${n}`)).toHaveText(line.accountCode);
    await expect(page.getByTestId(`fi-gl-journal-line-name-${n}`)).toHaveText(utf16(line.accountNameHex));
    await expect(page.getByTestId(`fi-gl-journal-line-version-${n}`))
      .toHaveText(`科目 v${line.accountVersion} · 對照 v${line.mappingVersion}`);
    await expect(page.getByTestId(`fi-gl-journal-line-debit-${n}`)).toHaveText(/^0?\.?0*$/.test(line.debit) ? '' : shown(line.debit));
    await expect(page.getByTestId(`fi-gl-journal-line-credit-${n}`)).toHaveText(/^0?\.?0*$/.test(line.credit) ? '' : shown(line.credit));
  }
  await expect(page.getByTestId('fi-gl-journal-total-debit')).toHaveText(shown(journal.totalDebit));
  await expect(page.getByTestId('fi-gl-journal-total-credit')).toHaveText(shown(journal.totalCredit));
}
