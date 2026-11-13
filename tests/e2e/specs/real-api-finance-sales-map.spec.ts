import { mkdir } from 'node:fs/promises';
import { expect, request, test, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql, utf16 } from '../support/real-api-sql';

// WU-FI-002a4c2 (FI-SALES-MAP-REAL-001–004): the SD invoice account map screen (6.23) against the running API and SQL Server.
// The Development seed creates 1131/4101/2141 for TW01 and HK01 (ADR-112); every screen value is compared with the saved row
// (read back with sqlcmd). Writes are undone in finally (the version keeps counting: a version is never reused, ADR-109).
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-finance-sales-map
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';

type Field = 'receivableAccountCode' | 'revenueAccountCode' | 'outputTaxAccountCode';
const FIELDS: Field[] = ['receivableAccountCode', 'revenueAccountCode', 'outputTaxAccountCode'];
type SavedMap = Record<Field, string> & { version: number };

function savedMap(company: number): SavedMap | undefined {
  return sql<SavedMap>(`SELECT receivable_account_code AS receivableAccountCode, revenue_account_code AS revenueAccountCode,
    output_tax_account_code AS outputTaxAccountCode, version FROM gl_sales_invoice_account_map WHERE company_id = ${company}`)[0];
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
  await page.screenshot({ path: `${path}/6.23-real-${name}.png`, fullPage: true, animations: 'disabled' });
}

test.describe('真實 API 銷售發票科目設定（SQL 讀回雙斷言）', () => {
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
    await expect(page.getByTestId('fi-sales-map-unconfigured')).toHaveCount(0);
    await expect(page.getByTestId('fi-sales-map-unusable')).toHaveCount(0);
    for (const field of FIELDS) {
      await expect(page.getByTestId(`fi-sales-map-code-${field}`)).toHaveText(saved[field]);
      const name = accountName(company, saved[field]);
      expect(name, `MD account ${saved[field]} of company ${company}: run --seed (ADR-112)`).toBeDefined();
      await expect(page.getByTestId(`fi-sales-map-name-${field}`)).toHaveText(name!);
    }
    await expect(page.getByTestId('fi-sales-map-version')).toContainText(`版本 ${saved.version}`);
  }

  /** Puts the codes back through the API (finance) when a test changed them. */
  async function restore(company: number, original: SavedMap): Promise<void> {
    const now = savedMap(company)!;
    if (FIELDS.every(field => now[field] === original[field])) return;
    const api = await request.newContext({ baseURL: apiUrl,
      extraHTTPHeaders: { Authorization: `Bearer ${tokens.finance!.accessToken}`, 'X-Company-Id': String(company) } });
    try {
      const response = await api.put(`${root}/gl/sales-invoice-account-map`, { data: {
        version: now.version, receivableAccountCode: original.receivableAccountCode,
        revenueAccountCode: original.revenueAccountCode, outputTaxAccountCode: original.outputTaxAccountCode } });
      expect(response.status(), await response.text()).toBe(200);
    } finally { await api.dispose(); }
    expect(savedMap(company)).toMatchObject({ receivableAccountCode: original.receivableAccountCode,
      revenueAccountCode: original.revenueAccountCode, outputTaxAccountCode: original.outputTaxAccountCode });
  }

  test.beforeEach(({ page }) => {
    consoleErrors = []; serverErrors = []; allowedConsole = [];
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    page.on('pageerror', error => consoleErrors.push(error.message));
    page.on('response', response => { if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`); });
  });

  test.afterEach(async ({ page }) => {
    expect(await page.locator('body').innerText()).not.toMatch(/\b(fi|common|layout)\.[a-z][A-Za-z]+\.[a-z][A-Za-z.]+\b/);
    expect(serverErrors).toEqual([]);
    expect(consoleErrors.filter(message => !allowedConsole.some(pattern => pattern.test(message)))).toEqual([]);
  });

  test('FI-SALES-MAP-REAL-001 finance opens the screen from the menu and sees the seeded map of TW01 and HK01', async ({ page }) => {
    const tw01 = companyId('TW01'); const hk01 = companyId('HK01');
    const tw = savedMap(tw01); const hk = savedMap(hk01);
    expect(tw, 'TW01 sales invoice map: run --seed (ADR-112)').toBeDefined();
    expect(hk, 'HK01 sales invoice map: run --seed (ADR-112)').toBeDefined();
    // The seed itself: the output tax account is the MD demo account 2141.
    expect(accountName(tw01, '2141')).toBe('銷項稅額');
    await login(page, 'finance');
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-sales-map').click();
    await expect(page).toHaveURL(/\/gl\/sales-invoice-account-map/);
    await expectSaved(page, tw01, tw!);
    for (const field of FIELDS) await expect(page.getByTestId(`fi-sales-map-input-${field}`)).toHaveValue(tw![field]);
    await capture(page, 'tw01-light-zh-Hant');

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01' }).click();
    await expect(page.getByTestId('layout-company-select')).toContainText('HK01');
    await expectSaved(page, hk01, hk!);
  });

  test('FI-SALES-MAP-REAL-002 a real change saves version +1; the same accounts save nothing', async ({ page }) => {
    const tw01 = companyId('TW01');
    const original = savedMap(tw01)!;
    // Another postable liability of the demo chart; whichever differs from the saved one.
    const otherTax = original.outputTaxAccountCode === '2171' ? '2141' : '2171';
    await login(page, 'finance');
    await page.goto('/gl/sales-invoice-account-map');
    await expectSaved(page, tw01, original);
    try {
      await page.getByTestId('fi-sales-map-input-outputTaxAccountCode').fill(` ${otherTax} `);
      await page.getByTestId('fi-sales-map-save').click();
      await expect(page.getByTestId('fi-sales-map-notice')).toHaveAttribute('data-type', 'success');
      await expect(page.getByTestId('fi-sales-map-notice')).toContainText(`已修改科目（版本 ${original.version + 1}）`);
      const changed = savedMap(tw01)!;
      expect(changed).toEqual({ ...original, outputTaxAccountCode: otherTax, version: original.version + 1 });
      await expectSaved(page, tw01, changed);
      await capture(page, 'updated-light-zh-Hant');

      // Same accounts again: a warning, and SQL Server keeps the row and its version.
      await page.getByTestId('fi-sales-map-save').click();
      await expect(page.getByTestId('fi-sales-map-notice')).toHaveAttribute('data-type', 'warning');
      await expect(page.getByTestId('fi-sales-map-notice')).toContainText(`沒有變更：這些已是保存中的科目（仍為版本 ${changed.version}）`);
      expect(savedMap(tw01)).toEqual(changed);
      await capture(page, 'unchanged-light-zh-Hant');
    } finally {
      await restore(tw01, original);
    }
  });

  test('FI-SALES-MAP-REAL-003 unusable accounts are refused field by field and nothing is saved', async ({ page }) => {
    const tw01 = companyId('TW01');
    const original = savedMap(tw01)!;
    // 9999 is not in MD; 2 is the liability root (has children, not postable).
    expect(accountName(tw01, '9999')).toBeUndefined();
    allowedConsole.push(/Failed to load resource: the server responded with a status of 422/);
    await login(page, 'finance');
    await page.goto('/gl/sales-invoice-account-map');
    await expectSaved(page, tw01, original);
    try {
      await page.getByTestId('fi-sales-map-input-receivableAccountCode').fill('9999');
      await page.getByTestId('fi-sales-map-input-outputTaxAccountCode').fill('2');
      await page.getByTestId('fi-sales-map-save').click();
      await expect(page.getByTestId('fi-sales-map-save-error')).toContainText('沒有保存任何資料');
      await expect(page.getByTestId('fi-sales-map-field-error-receivableAccountCode')).toHaveText('主檔沒有此代碼的科目。');
      await expect(page.getByTestId('fi-sales-map-input-receivableAccountCode')).toBeFocused();
      await expect(page.getByTestId('fi-sales-map-field-error-outputTaxAccountCode')).toHaveText('此科目有子科目，請選擇最底層科目。');
      await expect(page.getByTestId('fi-sales-map-field-error-revenueAccountCode')).toHaveCount(0);
      // The input stays for correction; the saved block and SQL Server are unchanged.
      await expect(page.getByTestId('fi-sales-map-input-receivableAccountCode')).toHaveValue('9999');
      expect(savedMap(tw01)).toEqual(original);
      await expectSaved(page, tw01, original);
      await capture(page, 'account-invalid-light-zh-Hant');
    } finally {
      await restore(tw01, original);
    }
  });

  test('FI-SALES-MAP-REAL-004 viewer reads the saved map without any input', async ({ page }) => {
    const tw01 = companyId('TW01');
    const saved = savedMap(tw01)!;
    await login(page, 'viewer');
    await page.goto('/gl/sales-invoice-account-map');
    await expectSaved(page, tw01, saved);
    await expect(page.getByTestId('fi-sales-map-form')).toHaveCount(0);
    await expect(page.locator('erp-sales-map-page input')).toHaveCount(0);
    await expect(page.getByTestId('fi-sales-map-save')).toHaveCount(0);
    await capture(page, 'viewer-light-zh-Hant');
  });
});
