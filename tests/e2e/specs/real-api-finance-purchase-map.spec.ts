import { mkdir } from 'node:fs/promises';
import { expect, request, test, type Page } from '@playwright/test';
import { realApiLogin, type RealApiTokens } from '../support/real-api';
import { companyId, sql, utf16 } from '../support/real-api-sql';

// WU-FI-003a2d (FI-PUR-MAP-REAL-001–004): the PUR invoice account map screen (6.24) against the running API and SQL Server.
// The Development seed creates 2101/5111/1151 for TW01 and HK01 (ADR-118); every screen value is compared with the saved row
// (read back with sqlcmd). Writes are undone in finally (the version keeps counting: a version is never reused, ADR-109).
// Run: & ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-finance-purchase-map
const apiUrl = process.env['E2E_API_URL'];
const root = '/api/v1';

type Field = 'payableAccountCode' | 'purchaseAccountCode' | 'inputTaxAccountCode';
const FIELDS: Field[] = ['payableAccountCode', 'purchaseAccountCode', 'inputTaxAccountCode'];
type SavedMap = Record<Field, string> & { version: number };

function savedMap(company: number): SavedMap | undefined {
  return sql<SavedMap>(`SELECT payable_account_code AS payableAccountCode, purchase_account_code AS purchaseAccountCode,
    input_tax_account_code AS inputTaxAccountCode, version FROM gl_purchase_invoice_account_map WHERE company_id = ${company}`)[0];
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
  await page.screenshot({ path: `${path}/6.24-real-${name}.png`, fullPage: true, animations: 'disabled' });
}

test.describe('真實 API 採購發票科目設定（SQL 讀回雙斷言）', () => {
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
    await expect(page.getByTestId('fi-purchase-map-unconfigured')).toHaveCount(0);
    await expect(page.getByTestId('fi-purchase-map-unusable')).toHaveCount(0);
    for (const field of FIELDS) {
      await expect(page.getByTestId(`fi-purchase-map-code-${field}`)).toHaveText(saved[field]);
      const name = accountName(company, saved[field]);
      expect(name, `MD account ${saved[field]} of company ${company}: run --seed (ADR-118)`).toBeDefined();
      await expect(page.getByTestId(`fi-purchase-map-name-${field}`)).toHaveText(name!);
    }
    await expect(page.getByTestId('fi-purchase-map-version')).toContainText(`版本 ${saved.version}`);
  }

  /** Puts the codes back through the API (finance) when a test changed them. */
  async function restore(company: number, original: SavedMap): Promise<void> {
    const now = savedMap(company)!;
    if (FIELDS.every(field => now[field] === original[field])) return;
    const api = await request.newContext({ baseURL: apiUrl,
      extraHTTPHeaders: { Authorization: `Bearer ${tokens.finance!.accessToken}`, 'X-Company-Id': String(company) } });
    try {
      const response = await api.put(`${root}/gl/purchase-invoice-account-map`, { data: {
        version: now.version, payableAccountCode: original.payableAccountCode,
        purchaseAccountCode: original.purchaseAccountCode, inputTaxAccountCode: original.inputTaxAccountCode } });
      expect(response.status(), await response.text()).toBe(200);
    } finally { await api.dispose(); }
    expect(savedMap(company)).toMatchObject({ payableAccountCode: original.payableAccountCode,
      purchaseAccountCode: original.purchaseAccountCode, inputTaxAccountCode: original.inputTaxAccountCode });
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

  test('FI-PUR-MAP-REAL-001 finance opens the screen from the menu and sees the seeded map of TW01 and HK01', async ({ page }) => {
    const tw01 = companyId('TW01'); const hk01 = companyId('HK01');
    const tw = savedMap(tw01); const hk = savedMap(hk01);
    expect(tw, 'TW01 purchase invoice map: run --seed (ADR-118)').toBeDefined();
    expect(hk, 'HK01 purchase invoice map: run --seed (ADR-118)').toBeDefined();
    // The seed itself: the input tax account is the MD demo account 1151.
    expect(accountName(tw01, '1151')).toBe('進項稅額');
    await login(page, 'finance');
    await page.getByTestId('layout-menu-fi').click();
    await page.getByTestId('layout-menu-fi-purchase-map').click();
    await expect(page).toHaveURL(/\/gl\/purchase-invoice-account-map/);
    await expectSaved(page, tw01, tw!);
    for (const field of FIELDS) await expect(page.getByTestId(`fi-purchase-map-input-${field}`)).toHaveValue(tw![field]);
    await capture(page, 'tw01-light-zh-Hant');

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01' }).click();
    await expect(page.getByTestId('layout-company-select')).toContainText('HK01');
    await expectSaved(page, hk01, hk!);
  });

  test('FI-PUR-MAP-REAL-002 a real change saves version +1; the same accounts save nothing', async ({ page }) => {
    const tw01 = companyId('TW01');
    const original = savedMap(tw01)!;
    // Another postable expense account of the demo chart; whichever differs from the saved one.
    const otherPurchase = original.purchaseAccountCode === '6102' ? '5111' : '6102';
    await login(page, 'finance');
    await page.goto('/gl/purchase-invoice-account-map');
    await expectSaved(page, tw01, original);
    try {
      await page.getByTestId('fi-purchase-map-input-purchaseAccountCode').fill(` ${otherPurchase} `);
      await page.getByTestId('fi-purchase-map-save').click();
      await expect(page.getByTestId('fi-purchase-map-notice')).toHaveAttribute('data-type', 'success');
      await expect(page.getByTestId('fi-purchase-map-notice')).toContainText(`已修改科目（版本 ${original.version + 1}）`);
      const changed = savedMap(tw01)!;
      expect(changed).toEqual({ ...original, purchaseAccountCode: otherPurchase, version: original.version + 1 });
      await expectSaved(page, tw01, changed);
      await capture(page, 'updated-light-zh-Hant');

      // Same accounts again: a warning, and SQL Server keeps the row and its version.
      await page.getByTestId('fi-purchase-map-save').click();
      await expect(page.getByTestId('fi-purchase-map-notice')).toHaveAttribute('data-type', 'warning');
      await expect(page.getByTestId('fi-purchase-map-notice')).toContainText(`沒有變更：這些已是保存中的科目（仍為版本 ${changed.version}）`);
      expect(savedMap(tw01)).toEqual(changed);
      await capture(page, 'unchanged-light-zh-Hant');
    } finally {
      await restore(tw01, original);
    }
  });

  test('FI-PUR-MAP-REAL-003 unusable accounts are refused field by field and nothing is saved', async ({ page }) => {
    const tw01 = companyId('TW01');
    const original = savedMap(tw01)!;
    // 9999 is not in MD; 1 is the asset root (has children, not postable).
    expect(accountName(tw01, '9999')).toBeUndefined();
    allowedConsole.push(/Failed to load resource: the server responded with a status of 422/);
    await login(page, 'finance');
    await page.goto('/gl/purchase-invoice-account-map');
    await expectSaved(page, tw01, original);
    try {
      await page.getByTestId('fi-purchase-map-input-payableAccountCode').fill('9999');
      await page.getByTestId('fi-purchase-map-input-inputTaxAccountCode').fill('1');
      await page.getByTestId('fi-purchase-map-save').click();
      await expect(page.getByTestId('fi-purchase-map-save-error')).toContainText('沒有儲存任何內容');
      await expect(page.getByTestId('fi-purchase-map-field-error-payableAccountCode')).toHaveText('主檔沒有此代碼的科目。');
      await expect(page.getByTestId('fi-purchase-map-input-payableAccountCode')).toBeFocused();
      await expect(page.getByTestId('fi-purchase-map-field-error-inputTaxAccountCode')).toHaveText('此科目有子科目，請選擇最底層科目。');
      await expect(page.getByTestId('fi-purchase-map-field-error-purchaseAccountCode')).toHaveCount(0);
      // The input stays for correction; the saved block and SQL Server are unchanged.
      await expect(page.getByTestId('fi-purchase-map-input-payableAccountCode')).toHaveValue('9999');
      expect(savedMap(tw01)).toEqual(original);
      await expectSaved(page, tw01, original);
      await capture(page, 'account-invalid-light-zh-Hant');
    } finally {
      await restore(tw01, original);
    }
  });

  test('FI-PUR-MAP-REAL-004 viewer reads the saved map without any input', async ({ page }) => {
    const tw01 = companyId('TW01');
    const saved = savedMap(tw01)!;
    await login(page, 'viewer');
    await page.goto('/gl/purchase-invoice-account-map');
    await expectSaved(page, tw01, saved);
    await expect(page.getByTestId('fi-purchase-map-form')).toHaveCount(0);
    await expect(page.locator('erp-purchase-map-page input')).toHaveCount(0);
    await expect(page.getByTestId('fi-purchase-map-save')).toHaveCount(0);
    await capture(page, 'viewer-light-zh-Hant');
  });
});
