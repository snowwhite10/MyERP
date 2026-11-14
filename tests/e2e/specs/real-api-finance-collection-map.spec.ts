import { mkdir } from 'node:fs/promises';
import { expect, request, test, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql, utf16 } from '../support/real-api-sql';

// WU-FI-005a2f (FI-COLL-MAP-REAL-001–005): the sales collection account map screen (6.25) against the running API and SQL
// Server. The Development seed creates 2181/1131 for TW01 and HK01 (ADR-123) and the SD invoice map uses AR 1131; every
// screen value is compared with the saved row (read back with sqlcmd). Writes are undone in finally (the version keeps
// counting: a version is never reused, ADR-120).
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-finance-collection-map
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';

type Field = 'unappliedCashAccountCode' | 'receivableAccountCode';
const FIELDS: Field[] = ['unappliedCashAccountCode', 'receivableAccountCode'];
type SavedMap = Record<Field, string> & { version: number };

function savedMap(company: number): SavedMap | undefined {
  return sql<SavedMap>(`SELECT unapplied_cash_account_code AS unappliedCashAccountCode,
    receivable_account_code AS receivableAccountCode, version FROM gl_sales_collection_account_map WHERE company_id = ${company}`)[0];
}

function salesReceivable(company: number): string | undefined {
  return sql<{ code: string }>(`SELECT receivable_account_code AS code FROM gl_sales_invoice_account_map WHERE company_id = ${company}`)[0]?.code;
}

/** The MD name of an account code in the company, or undefined when MD has no live account with that code. */
function accountName(company: number, code: string): string | undefined {
  const [row] = sql<{ nameHex: string }>(`SELECT CONVERT(varchar(max), CONVERT(varbinary(max), name), 2) AS nameHex
    FROM md_account WHERE company_id = ${company} AND code = '${code}' AND is_deleted = 0`);
  return row ? utf16(row.nameHex) : undefined;
}

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.25-real-${name}.png`, fullPage: true, animations: 'disabled' });
}

test.describe('真實 API 收款科目設定（SQL 讀回雙斷言）', () => {
  test.describe.configure({ mode: 'serial' });
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');
  test.setTimeout(120_000);

  let consoleErrors: string[];
  let serverErrors: string[];
  let allowedConsole: RegExp[];
  const tokens: Partial<Record<'finance' | 'viewer', RealApiTokens>> = {};

  test.beforeAll(async () => {
    test.setTimeout(240_000); // realApiLogin may wait out the login rate-limit window
    tokens.finance = await realApiLogin(apiUrl!, 'finance', 'Demo@2026');
    tokens.viewer = await realApiLogin(apiUrl!, 'viewer', 'Demo@2026');
  });

  async function login(page: Page, username: 'finance' | 'viewer'): Promise<void> {
    await page.addInitScript(([key, value]) => localStorage.setItem(key, value),
      ['myerp.auth.session', JSON.stringify({ ...tokens[username], username })]);
    await page.goto('/');
    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page.getByTestId('layout-username')).toHaveText(username);
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01');
  }

  /** The saved accounts block equals the SQL row: codes, MD names and version. */
  async function expectSaved(page: Page, company: number, saved: SavedMap): Promise<void> {
    await expect(page.getByTestId('fi-collection-map-unconfigured')).toHaveCount(0);
    await expect(page.getByTestId('fi-collection-map-unusable')).toHaveCount(0);
    for (const field of FIELDS) {
      await expect(page.getByTestId(`fi-collection-map-code-${field}`)).toHaveText(saved[field]);
      const name = accountName(company, saved[field]);
      expect(name, `MD account ${saved[field]} of company ${company}: run --seed (ADR-123)`).toBeDefined();
      await expect(page.getByTestId(`fi-collection-map-name-${field}`)).toHaveText(name!);
    }
    await expect(page.getByTestId('fi-collection-map-version')).toContainText(`版本 ${saved.version}`);
  }

  /** Puts the codes back through the API (finance) when a test changed them. */
  async function restore(company: number, original: SavedMap): Promise<void> {
    const now = savedMap(company)!;
    if (FIELDS.every(field => now[field] === original[field])) return;
    const api = await request.newContext({ baseURL: apiUrl,
      extraHTTPHeaders: { Authorization: `Bearer ${tokens.finance!.accessToken}`, 'X-Company-Id': String(company) } });
    try {
      const response = await api.put(`${root}/gl/sales-collection-account-map`, { data: {
        version: now.version, unappliedCashAccountCode: original.unappliedCashAccountCode,
        receivableAccountCode: original.receivableAccountCode } });
      expect(response.status(), await response.text()).toBe(200);
    } finally { await api.dispose(); }
    expect(savedMap(company)).toMatchObject({ unappliedCashAccountCode: original.unappliedCashAccountCode,
      receivableAccountCode: original.receivableAccountCode });
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = []; allowedConsole = [];
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    expect(await page.locator('body').innerText()).not.toMatch(/\b(fi|common|layout|errors)\.[a-zA-Z][A-Za-z_]+\.[A-Za-z][A-Za-z_.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors.filter(message => !allowedConsole.some(pattern => pattern.test(message)))).toEqual([]);
  });

  test('FI-COLL-MAP-REAL-001 finance opens the screen from the menu and sees the seeded map of TW01 and HK01', async ({ page }) => {
    const tw01 = companyId('TW01'); const hk01 = companyId('HK01');
    const tw = savedMap(tw01); const hk = savedMap(hk01);
    expect(tw, 'TW01 collection map: run --seed (ADR-123)').toBeDefined();
    expect(hk, 'HK01 collection map: run --seed (ADR-123)').toBeDefined();
    expect(accountName(tw01, '2181')).toBe('暫收款');
    await login(page, 'finance');
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-collection-map').click();
    await expect(page).toHaveURL(/\/gl\/sales-collection-account-map/);
    await expectSaved(page, tw01, tw!);
    for (const field of FIELDS) await expect(page.getByTestId(`fi-collection-map-input-${field}`)).toHaveValue(tw![field]);
    // The warning appears exactly when SQL says the SD invoice map names another AR account.
    const differs = salesReceivable(tw01) !== undefined && salesReceivable(tw01) !== tw!.receivableAccountCode;
    await expect(page.getByTestId('fi-collection-map-receivable-differs')).toHaveCount(differs ? 1 : 0);
    await capture(page, 'tw01-light-zh-Hant');

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01' }).click();
    await expect(page.getByTestId('layout-company-select')).toContainText('HK01');
    await expectSaved(page, hk01, hk!);
  });

  test('FI-COLL-MAP-REAL-002 a real change saves version +1; the same accounts save nothing', async ({ page }) => {
    const tw01 = companyId('TW01');
    const original = savedMap(tw01)!;
    // Another postable liability account of the demo chart; whichever differs from the saved one.
    const otherCash = original.unappliedCashAccountCode === '2171' ? '2181' : '2171';
    await login(page, 'finance');
    await page.goto('/gl/sales-collection-account-map');
    await expectSaved(page, tw01, original);
    try {
      await page.getByTestId('fi-collection-map-input-unappliedCashAccountCode').fill(` ${otherCash} `);
      await page.getByTestId('fi-collection-map-save').click();
      await expect(page.getByTestId('fi-collection-map-notice')).toHaveAttribute('data-type', 'success');
      await expect(page.getByTestId('fi-collection-map-notice')).toContainText(`已修改科目（版本 ${original.version + 1}）`);
      const changed = savedMap(tw01)!;
      expect(changed).toEqual({ ...original, unappliedCashAccountCode: otherCash, version: original.version + 1 });
      await expectSaved(page, tw01, changed);
      await capture(page, 'updated-light-zh-Hant');

      await page.getByTestId('fi-collection-map-save').click();
      await expect(page.getByTestId('fi-collection-map-notice')).toHaveAttribute('data-type', 'warning');
      await expect(page.getByTestId('fi-collection-map-notice')).toContainText(`沒有變更：這些已是保存中的科目（仍為版本 ${changed.version}）`);
      expect(savedMap(tw01)).toEqual(changed);
    } finally {
      await restore(tw01, original);
    }
  });

  test('FI-COLL-MAP-REAL-003 unusable or identical accounts are refused and nothing is saved', async ({ page }) => {
    const tw01 = companyId('TW01');
    const original = savedMap(tw01)!;
    // 9999 is not in MD; 2 is the liability root (has children, not postable).
    expect(accountName(tw01, '9999')).toBeUndefined();
    allowedConsole.push(/Failed to load resource: the server responded with a status of 422/);
    let puts = 0;
    page.on('request', r => { if (r.method() === 'PUT' && r.url().includes('/gl/sales-collection-account-map')) puts++; });
    await login(page, 'finance');
    await page.goto('/gl/sales-collection-account-map');
    await expectSaved(page, tw01, original);
    try {
      // Same code on both fields is refused on screen without a request.
      await page.getByTestId('fi-collection-map-input-receivableAccountCode').fill(original.unappliedCashAccountCode);
      await page.getByTestId('fi-collection-map-save').click();
      await expect(page.getByTestId('fi-collection-map-field-error-receivableAccountCode')).toHaveText('應收控制科目不可與未核銷款暫收科目相同。');
      expect(puts).toBe(0);
      await page.getByTestId('fi-collection-map-input-unappliedCashAccountCode').fill('9999');
      await page.getByTestId('fi-collection-map-input-receivableAccountCode').fill('2');
      await page.getByTestId('fi-collection-map-save').click();
      await expect(page.getByTestId('fi-collection-map-save-error')).toContainText('沒有儲存任何內容');
      await expect(page.getByTestId('fi-collection-map-field-error-unappliedCashAccountCode')).toHaveText('主檔沒有此代碼的科目。');
      await expect(page.getByTestId('fi-collection-map-input-unappliedCashAccountCode')).toBeFocused();
      await expect(page.getByTestId('fi-collection-map-field-error-receivableAccountCode')).toHaveText('此科目有子科目，請選擇最底層科目。');
      expect(puts).toBe(1);
      await expect(page.getByTestId('fi-collection-map-input-unappliedCashAccountCode')).toHaveValue('9999');
      expect(savedMap(tw01)).toEqual(original);
      await expectSaved(page, tw01, original);
      await capture(page, 'account-invalid-light-zh-Hant');
    } finally {
      await restore(tw01, original);
    }
  });

  test('FI-COLL-MAP-REAL-004 an AR account other than the SD invoice map is saved with a warning naming both codes', async ({ page }) => {
    const tw01 = companyId('TW01');
    const original = savedMap(tw01)!;
    const sales = salesReceivable(tw01);
    expect(sales, 'TW01 sales invoice map: run --seed (ADR-110)').toBeDefined();
    const otherAr = sales === '1102' ? '1131' : '1102';
    await login(page, 'finance');
    await page.goto('/gl/sales-collection-account-map');
    try {
      await page.getByTestId('fi-collection-map-input-receivableAccountCode').fill(otherAr);
      await page.getByTestId('fi-collection-map-save').click();
      await expect(page.getByTestId('fi-collection-map-notice')).toHaveAttribute('data-type', 'success');
      expect(savedMap(tw01)).toMatchObject({ receivableAccountCode: otherAr, version: original.version + 1 });
      await expect(page.getByTestId('fi-collection-map-receivable-differs'))
        .toContainText(`應收控制科目 ${otherAr} 與「銷售發票科目設定」的應收科目 ${sales} 不同`);
      await expectSaved(page, tw01, savedMap(tw01)!);
      await capture(page, 'receivable-differs-light-zh-Hant');
    } finally {
      await restore(tw01, original);
    }
    await page.reload();
    await expect(page.getByTestId('fi-collection-map-code-receivableAccountCode')).toHaveText(original.receivableAccountCode);
  });

  test('FI-COLL-MAP-REAL-005 viewer reads the saved map without any input', async ({ page }) => {
    const tw01 = companyId('TW01');
    const saved = savedMap(tw01)!;
    await login(page, 'viewer');
    await page.goto('/gl/sales-collection-account-map');
    await expectSaved(page, tw01, saved);
    await expect(page.getByTestId('fi-collection-map-form')).toHaveCount(0);
    await expect(page.locator('erp-collection-map-page input')).toHaveCount(0);
    await expect(page.getByTestId('fi-collection-map-save')).toHaveCount(0);
    await capture(page, 'viewer-light-zh-Hant');
  });
});
