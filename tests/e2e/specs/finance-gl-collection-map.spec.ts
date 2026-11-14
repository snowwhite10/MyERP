import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { expectReadableText } from '../support/contrast';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

type Field = 'unappliedCashAccountCode' | 'receivableAccountCode';
type Codes = Record<Field, string>;
type MdAccount = { name: string; isActive: boolean; isPostable: boolean };
type Saved = { version: number; codes: Codes | null; updatedAt: string | null; salesReceivable: string | null };
const FIELDS: Field[] = ['unappliedCashAccountCode', 'receivableAccountCode'];
const API = '**/api/v1/gl/sales-collection-account-map';
const PAGE = '/gl/sales-collection-account-map';

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.25-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** MD chart used by the mock: 2181/2182/1131/1132 postable, 1130 has children, 2189 inactive. */
const chart = (): Record<string, MdAccount> => ({
  '2181': { name: '暫收款', isActive: true, isPostable: true },
  '2182': { name: '預收貨款', isActive: true, isPostable: true },
  '1131': { name: '應收帳款', isActive: true, isPostable: true },
  '1132': { name: '應收票據', isActive: true, isPostable: true },
  '1130': { name: '應收款項', isActive: true, isPostable: false },
  '2189': { name: '舊暫收款', isActive: false, isPostable: false },
});

/**
 * Saved map per company (X-Company-Id), served like the API (ADR-123): GET never 404s and names the SD invoice map's
 * receivable with a warning when it differs; PUT version 0 creates, any other version must equal the saved one (409);
 * unchanged codes → changed=false; every invalid account and equal codes → one 422 with accountErrors.
 */
async function mockMap(page: Page, companies: Record<string, Saved>, md = chart(), writes: unknown[] = []) {
  const read = (saved: Saved) => {
    const account = (code: string) => md[code] ? { code, ...md[code] } : { code, name: null, isActive: false, isPostable: false };
    if (!saved.codes) {
      return { configured: false, version: 0, unappliedCashAccount: null, receivableAccount: null,
        salesInvoiceReceivableAccountCode: saved.salesReceivable, warnings: [], createdAt: null, createdBy: null, updatedAt: null, updatedBy: null };
    }
    const differs = saved.salesReceivable !== null && saved.salesReceivable.toLowerCase() !== saved.codes.receivableAccountCode.toLowerCase();
    return { configured: true, version: saved.version, unappliedCashAccount: account(saved.codes.unappliedCashAccountCode),
      receivableAccount: account(saved.codes.receivableAccountCode), salesInvoiceReceivableAccountCode: saved.salesReceivable,
      warnings: differs ? ['FI.COLLECTION_MAP.RECEIVABLE_DIFFERS_FROM_SALES_MAP'] : [],
      createdAt: '2026-11-10T09:15:00', createdBy: 1, updatedAt: saved.updatedAt, updatedBy: 2 };
  };
  await page.route(API, route => {
    const request = route.request();
    const saved = companies[request.headers()['x-company-id'] ?? '1'] ??= { version: 0, codes: null, updatedAt: null, salesReceivable: null };
    if (request.method() === 'GET') return route.fulfill({ json: read(saved) });
    const body = request.postDataJSON() as Codes & { version: number };
    writes.push(body);
    if (body.version !== saved.version) return route.fulfill({ status: 409, json: { status: 409, errorCode: 'FI.COLLECTION_MAP.VERSION_CONFLICT' } });
    if (saved.codes && FIELDS.every(field => saved.codes![field] === body[field])) return route.fulfill({ json: { map: read(saved), changed: false } });
    const reasons: Partial<Record<Field, string>> = {};
    for (const field of FIELDS) {
      const account = md[body[field]];
      if (!account) reasons[field] = 'MD.ACCOUNT.NOT_FOUND';
      else if (!account.isActive) reasons[field] = 'MD.ACCOUNT.INACTIVE';
      else if (!account.isPostable) reasons[field] = 'MD.ACCOUNT.NOT_POSTABLE';
    }
    if (body.unappliedCashAccountCode.toLowerCase() === body.receivableAccountCode.toLowerCase()) {
      reasons.receivableAccountCode ??= 'FI.COLLECTION_MAP.SAME_ACCOUNT';
    }
    if (Object.keys(reasons).length) {
      return route.fulfill({ status: 422, json: { status: 422, errorCode: 'FI.COLLECTION_MAP.ACCOUNT_INVALID', accountErrors: reasons } });
    }
    saved.codes = { unappliedCashAccountCode: body.unappliedCashAccountCode, receivableAccountCode: body.receivableAccountCode };
    saved.version++; saved.updatedAt = '2026-11-14T05:10:00';
    return route.fulfill({ json: { map: read(saved), changed: true } });
  });
}

const configured = (): Saved => ({ version: 2, updatedAt: '2026-11-13T18:05:44', salesReceivable: '1131',
  codes: { unappliedCashAccountCode: '2181', receivableAccountCode: '1131' } });

async function fill(page: Page, codes: Partial<Codes>) {
  for (const [field, value] of Object.entries(codes)) await page.getByTestId(`fi-collection-map-input-${field}`).fill(value);
}

test('FI-COLL-MAP-UI-001 shows the saved accounts in three languages and both themes', async ({ page }) => {
  await mockMap(page, { '1': configured() });
  await page.goto('/');
  await page.getByTestId('layout-menu-fi').click();
  await page.getByTestId('layout-menu-fi-collection-map').click();
  await expect(page.getByTestId('layout-menu-fi-collection-map')).toHaveAttribute('title', '收款科目設定');
  await expect(page).toHaveURL(/\/gl\/sales-collection-account-map/);
  await expect(page.getByTestId('fi-collection-map-code-unappliedCashAccountCode')).toHaveText('2181');
  await expect(page.getByTestId('fi-collection-map-name-receivableAccountCode')).toHaveText('應收帳款');
  await expect(page.getByTestId('fi-collection-map-version')).toContainText('版本 2');
  await expect(page.getByTestId('fi-collection-map-version')).toContainText('2026-11-13 18:05');
  await expect(page.getByTestId('fi-collection-map-receivable-differs')).toHaveCount(0);
  await expect(page.getByTestId('fi-collection-map-input-receivableAccountCode')).toHaveValue('1131');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-collection-map-version')).toContainText('Version 2');
  await expect(page.getByTestId('layout-menu-fi-collection-map')).toHaveAttribute('title', 'Collection accounts');
  await expect(page.getByTestId('fi-collection-map-form')).toContainText('Receivable control account');
  await capture(page, 'normal-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-collection-map-form')).toContainText('未核销款暂收科目');
  await capture(page, 'normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hans');
});

test('FI-COLL-MAP-UI-002/005/006/007 unconfigured company creates the map; same codes refused; saving again is a warning', async ({ page }) => {
  const companies: Record<string, Saved> = { '1': { version: 0, codes: null, updatedAt: null, salesReceivable: '1131' } };
  const writes: unknown[] = [];
  await mockMap(page, companies, chart(), writes);
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-collection-map-unconfigured')).toContainText('收款單及其核銷暫時不能過帳');
  await capture(page, 'unconfigured-light-zh-Hant');
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-field-error-unappliedCashAccountCode')).toHaveText('請輸入科目代碼。');
  await expect(page.getByTestId('fi-collection-map-input-unappliedCashAccountCode')).toBeFocused();
  await fill(page, { unappliedCashAccountCode: '2181', receivableAccountCode: ' 2181 ' });
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-field-error-receivableAccountCode')).toHaveText('應收控制科目不可與未核銷款暫收科目相同。');
  await expect(page.getByTestId('fi-collection-map-input-receivableAccountCode')).toBeFocused();
  expect(writes).toHaveLength(0);
  await capture(page, 'validation-light-zh-Hant');
  await fill(page, { unappliedCashAccountCode: ' 2181 ', receivableAccountCode: '1131' });
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-notice')).toHaveAttribute('data-type', 'success');
  await expect(page.getByTestId('fi-collection-map-notice')).toContainText('版本 1');
  // Screen and saved data agree: version 1 with trimmed codes.
  expect(companies['1']).toMatchObject({ version: 1, codes: { unappliedCashAccountCode: '2181', receivableAccountCode: '1131' } });
  await expect(page.getByTestId('fi-collection-map-code-unappliedCashAccountCode')).toHaveText('2181');
  await expect(page.getByTestId('fi-collection-map-unconfigured')).toHaveCount(0);
  await capture(page, 'created-light-zh-Hant');
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-notice')).toHaveAttribute('data-type', 'warning');
  await expect(page.getByTestId('fi-collection-map-notice')).toContainText('沒有變更');
  expect(companies['1'].version).toBe(1);
  expect(writes).toHaveLength(2);
  await chooseTheme(page, 'dark'); await capture(page, 'unchanged-dark-zh-Hant');
  await fill(page, { unappliedCashAccountCode: '2182' });
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-notice')).toContainText('已修改科目（版本 2）');
  await expect(page.getByTestId('fi-collection-map-name-unappliedCashAccountCode')).toHaveText('預收貨款');
  expect(companies['1'].codes!.unappliedCashAccountCode).toBe('2182');
});

test('FI-COLL-MAP-UI-016 an AR account other than the sales invoice map is saved with a yellow warning naming both codes', async ({ page }) => {
  const companies: Record<string, Saved> = { '1': configured() };
  await mockMap(page, companies);
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-collection-map-receivable-differs')).toHaveCount(0);
  await fill(page, { receivableAccountCode: '1132' });
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-notice')).toHaveAttribute('data-type', 'success');
  const warning = page.getByTestId('fi-collection-map-receivable-differs');
  await expect(warning).toContainText('應收控制科目 1132 與「銷售發票科目設定」的應收科目 1131 不同');
  await expect(warning.locator('.ant-alert-warning')).toBeVisible();
  expect(companies['1'].codes!.receivableAccountCode).toBe('1132');
  await capture(page, 'receivable-differs-light-zh-Hant');
  await chooseTheme(page, 'dark'); await chooseLanguage(page, 'en');
  await expect(warning).toContainText('differs from the receivable account 1131 in Sales invoice accounts');
  await capture(page, 'receivable-differs-dark-en');
});

test('FI-COLL-MAP-UI-004/008/009 unusable saved accounts, per-field refusals and a stale version change nothing', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422)/);
  const md = chart();
  const companies: Record<string, Saved> = { '1': configured() };
  await mockMap(page, companies, md);
  md['1131'].isActive = false;
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-collection-map-unusable')).toContainText('過帳會失敗');
  await expect(page.getByTestId('fi-collection-map-inactive-receivableAccountCode')).toHaveText('已停用');
  await capture(page, 'unusable-light-zh-Hant');
  await fill(page, { unappliedCashAccountCode: '9999', receivableAccountCode: '1130' });
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-save-error')).toContainText('沒有儲存任何內容');
  await expect(page.getByTestId('fi-collection-map-field-error-unappliedCashAccountCode')).toHaveText('主檔沒有此代碼的科目。');
  await expect(page.getByTestId('fi-collection-map-input-unappliedCashAccountCode')).toBeFocused();
  await expect(page.getByTestId('fi-collection-map-field-error-receivableAccountCode')).toContainText('最底層科目');
  expect(companies['1']).toMatchObject({ version: 2, codes: { unappliedCashAccountCode: '2181', receivableAccountCode: '1131' } });
  await expectReadableText(page.locator('.field-error'));
  for (const field of FIELDS) await expect(page.getByTestId(`fi-collection-map-input-${field}`)).toHaveClass(/ant-input-status-error/);
  await capture(page, 'account-invalid-light-zh-Hant');
  await chooseTheme(page, 'dark'); await expectReadableText(page.locator('.field-error'));
  await capture(page, 'account-invalid-dark-zh-Hant');
  // Another user saves version 3 meanwhile.
  companies['1'] = { version: 3, updatedAt: '2026-11-14T05:00:00', salesReceivable: '1131',
    codes: { unappliedCashAccountCode: '2182', receivableAccountCode: '1131' } };
  md['1131'].isActive = true;
  await fill(page, { unappliedCashAccountCode: '2181', receivableAccountCode: '1131' });
  await page.getByTestId('fi-collection-map-save').click();
  await expect(page.getByTestId('fi-collection-map-save-error')).toContainText('其他使用者已先修改這些科目');
  await expect(page.getByTestId('fi-collection-map-version')).toContainText('版本 3');
  await expect(page.getByTestId('fi-collection-map-input-unappliedCashAccountCode')).toHaveValue('2182');
  expect(companies['1'].codes!.unappliedCashAccountCode).toBe('2182');
  await capture(page, 'stale-dark-zh-Hant');
});

test('FI-COLL-MAP-UI-011 server error and network loss on read, then recovery', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/gl\/sales-collection-account-map/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  let mode: 'fail' | 'offline' | 'ok' = 'fail';
  await page.route(API, route => {
    if (mode === 'fail') return route.fulfill({ status: 500, json: { traceId: 'trace-collection-map-500' } });
    if (mode === 'offline') return route.abort('failed');
    return route.fulfill({ json: { configured: false, version: 0, unappliedCashAccount: null, receivableAccount: null,
      salesInvoiceReceivableAccountCode: null, warnings: [], createdAt: null, createdBy: null, updatedAt: null, updatedBy: null } });
  });
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-collection-map-error')).toContainText('trace-collection-map-500');
  await expect(page.getByTestId('fi-collection-map-form')).toHaveCount(0);
  await capture(page, 'server-error-light-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-collection-map-retry').click();
  await expect(page.getByTestId('fi-collection-map-error')).toContainText('無法連線到伺服器');
  await capture(page, 'network-error-light-zh-Hant');
  mode = 'ok'; await page.getByTestId('fi-collection-map-retry').click();
  await expect(page.getByTestId('fi-collection-map-error')).toHaveCount(0);
  await expect(page.getByTestId('fi-collection-map-unconfigured')).toBeVisible();
});

test('FI-COLL-MAP-UI-012 company switch reads that company\'s map', async ({ page }) => {
  await mockMap(page, { '1': configured(), '2': { version: 0, codes: null, updatedAt: null, salesReceivable: null } });
  await page.goto(PAGE);
  await expect(page.getByTestId('fi-collection-map-code-unappliedCashAccountCode')).toHaveText('2181');
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('fi-collection-map-unconfigured')).toBeVisible();
  await expect(page.getByTestId('fi-collection-map-input-unappliedCashAccountCode')).toHaveValue('');
});

test.describe('FI-COLL-MAP-UI-003 permissions', () => {
  test.describe('no permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.PERIOD.VIEW', dataScope: 'Company' }] } });
    test('hides the menu and route without reading the map', async ({ page }) => {
      let reads = 0;
      await page.route(API, route => { reads++; return route.abort(); });
      await page.goto(PAGE);
      await expect(page).toHaveURL(/\/forbidden/);
      await page.getByTestId('layout-menu-fi').click();
      await expect(page.getByTestId('layout-menu-fi-gl-periods')).toBeVisible();
      await expect(page.getByTestId('layout-menu-fi-collection-map')).toHaveCount(0);
      expect(reads).toBe(0);
    });
  });
  test.describe('view permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.COLLECTION_MAP.VIEW', dataScope: 'Company' }] } });
    test('reads the map without any input; an unconfigured map asks to contact finance', async ({ page }) => {
      const companies: Record<string, Saved> = { '1': configured() };
      await mockMap(page, companies);
      await page.goto(PAGE);
      await expect(page.getByTestId('fi-collection-map-code-receivableAccountCode')).toHaveText('1131');
      await expect(page.locator('erp-collection-map-page input')).toHaveCount(0);
      await expect(page.getByTestId('fi-collection-map-form')).toHaveCount(0);
      await capture(page, 'view-only-light-zh-Hant');
      companies['1'] = { version: 0, codes: null, updatedAt: null, salesReceivable: '1131' };
      await page.reload();
      await expect(page.getByTestId('fi-collection-map-unconfigured')).toContainText('請聯絡有編輯權限的財務人員');
      await capture(page, 'view-only-unconfigured-light-zh-Hant');
    });
  });
});
