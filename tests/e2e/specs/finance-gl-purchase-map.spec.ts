import { mkdir } from 'node:fs/promises';
import type { Page } from '@playwright/test';
import { expectReadableText } from '../support/contrast';
import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

type Field = 'payableAccountCode' | 'purchaseAccountCode' | 'inputTaxAccountCode';
type Codes = Record<Field, string>;
type MdAccount = { name: string; isActive: boolean; isPostable: boolean };
type Saved = { version: number; codes: Codes | null; updatedAt: string | null };
const FIELDS: Field[] = ['payableAccountCode', 'purchaseAccountCode', 'inputTaxAccountCode'];

async function capture(page: Page, name: string): Promise<void> {
  const path = '../../tests/qa/FI/screenshots';
  await mkdir(path, { recursive: true });
  await page.screenshot({ path: `${path}/6.24-${name}.png`, fullPage: true, animations: 'disabled' });
}

/** MD chart used by the mock: 2101/5111/5112/1151 postable, 5100 has children, 1159 inactive. */
const chart = (): Record<string, MdAccount> => ({
  '2101': { name: '應付帳款', isActive: true, isPostable: true },
  '5111': { name: '進貨', isActive: true, isPostable: true },
  '5112': { name: '進貨費用', isActive: true, isPostable: true },
  '1151': { name: '進項稅額', isActive: true, isPostable: true },
  '5100': { name: '營業成本', isActive: true, isPostable: false },
  '1159': { name: '舊進項稅額', isActive: false, isPostable: false },
});

/**
 * Saved map per company (X-Company-Id), served like the API (ADR-118): GET never 404s; PUT version 0 creates, any other
 * version must equal the saved one (409); unchanged codes → changed=false; every invalid account → one 422 with accountErrors.
 */
async function mockMap(page: Page, companies: Record<string, Saved>, md = chart(), writes: unknown[] = []) {
  const read = (saved: Saved) => {
    const account = (code: string) => md[code] ? { code, ...md[code] } : { code, name: null, isActive: false, isPostable: false };
    return saved.codes
      ? { configured: true, version: saved.version, payableAccount: account(saved.codes.payableAccountCode),
        purchaseAccount: account(saved.codes.purchaseAccountCode), inputTaxAccount: account(saved.codes.inputTaxAccountCode),
        createdAt: '2026-11-10T09:15:00', createdBy: 1, updatedAt: saved.updatedAt, updatedBy: 2 }
      : { configured: false, version: 0, payableAccount: null, purchaseAccount: null, inputTaxAccount: null,
        createdAt: null, createdBy: null, updatedAt: null, updatedBy: null };
  };
  await page.route('**/api/v1/gl/purchase-invoice-account-map', route => {
    const request = route.request();
    const saved = companies[request.headers()['x-company-id'] ?? '1'] ??= { version: 0, codes: null, updatedAt: null };
    if (request.method() === 'GET') return route.fulfill({ json: read(saved) });
    const body = request.postDataJSON() as Codes & { version: number };
    writes.push(body);
    if (body.version !== saved.version) return route.fulfill({ status: 409, json: { status: 409, errorCode: 'FI.PURCHASE_MAP.VERSION_CONFLICT' } });
    if (saved.codes && FIELDS.every(field => saved.codes![field] === body[field])) return route.fulfill({ json: { map: read(saved), changed: false } });
    const reasons: Partial<Record<Field, string>> = {};
    for (const field of FIELDS) {
      const account = md[body[field]];
      if (!account) reasons[field] = 'MD.ACCOUNT.NOT_FOUND';
      else if (!account.isActive) reasons[field] = 'MD.ACCOUNT.INACTIVE';
      else if (!account.isPostable) reasons[field] = 'MD.ACCOUNT.NOT_POSTABLE';
    }
    if (Object.keys(reasons).length) {
      return route.fulfill({ status: 422, json: { status: 422, errorCode: 'FI.PURCHASE_MAP.ACCOUNT_INVALID', accountErrors: reasons } });
    }
    saved.codes = { payableAccountCode: body.payableAccountCode, purchaseAccountCode: body.purchaseAccountCode,
      inputTaxAccountCode: body.inputTaxAccountCode };
    saved.version++; saved.updatedAt = '2026-11-14T05:10:00';
    return route.fulfill({ json: { map: read(saved), changed: true } });
  });
}

const configured = (): Saved => ({ version: 2, updatedAt: '2026-11-13T18:05:44',
  codes: { payableAccountCode: '2101', purchaseAccountCode: '5111', inputTaxAccountCode: '1151' } });

async function fill(page: Page, codes: Partial<Codes>) {
  for (const [field, value] of Object.entries(codes)) await page.getByTestId(`fi-purchase-map-input-${field}`).fill(value);
}

test('FI-PUR-MAP-UI-001 shows the saved accounts in three languages and both themes', async ({ page }) => {
  await mockMap(page, { '1': configured() });
  await page.goto('/');
  await page.getByTestId('layout-menu-fi').click();
  await page.getByTestId('layout-menu-fi-purchase-map').click();
  await expect(page.getByTestId('layout-menu-fi-purchase-map')).toHaveAttribute('title', '採購發票科目設定');
  await expect(page).toHaveURL(/\/gl\/purchase-invoice-account-map/);
  await expect(page.getByTestId('fi-purchase-map-code-payableAccountCode')).toHaveText('2101');
  await expect(page.getByTestId('fi-purchase-map-name-purchaseAccountCode')).toHaveText('進貨');
  await expect(page.getByTestId('fi-purchase-map-version')).toContainText('版本 2');
  await expect(page.getByTestId('fi-purchase-map-version')).toContainText('2026-11-13 18:05');
  await expect(page.getByTestId('fi-purchase-map-input-inputTaxAccountCode')).toHaveValue('1151');
  await capture(page, 'normal-light-zh-Hant');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hant');
  await chooseLanguage(page, 'en');
  await expect(page.getByTestId('fi-purchase-map-version')).toContainText('Version 2');
  // Long menu labels are truncated in the sidebar; the full text stays available on hover.
  await expect(page.getByTestId('layout-menu-fi-purchase-map')).toHaveAttribute('title', 'Purchase invoice accounts');
  await expect(page.getByTestId('fi-purchase-map-form')).toContainText('Input tax account');
  await capture(page, 'normal-dark-en');
  await chooseTheme(page, 'light'); await capture(page, 'normal-light-en');
  await chooseLanguage(page, 'zh-Hans');
  await expect(page.getByTestId('fi-purchase-map-form')).toContainText('进项税科目');
  await capture(page, 'normal-light-zh-Hans');
  await chooseTheme(page, 'dark'); await capture(page, 'normal-dark-zh-Hans');
});

test('FI-PUR-MAP-UI-002/005/006 unconfigured company creates the map; saving the same accounts is a warning', async ({ page }) => {
  const companies: Record<string, Saved> = { '1': { version: 0, codes: null, updatedAt: null } };
  const writes: unknown[] = [];
  await mockMap(page, companies, chart(), writes);
  await page.goto('/gl/purchase-invoice-account-map');
  await expect(page.getByTestId('fi-purchase-map-unconfigured')).toContainText('採購發票暫時不能過帳');
  await capture(page, 'unconfigured-light-zh-Hant');
  await page.getByTestId('fi-purchase-map-save').click();
  await expect(page.getByTestId('fi-purchase-map-field-error-payableAccountCode')).toHaveText('請輸入科目代碼。');
  await expect(page.getByTestId('fi-purchase-map-input-payableAccountCode')).toBeFocused();
  expect(writes).toHaveLength(0);
  await capture(page, 'validation-light-zh-Hant');
  await fill(page, { payableAccountCode: ' 2101 ', purchaseAccountCode: '5111', inputTaxAccountCode: '1151' });
  await page.getByTestId('fi-purchase-map-save').click();
  await expect(page.getByTestId('fi-purchase-map-notice')).toHaveAttribute('data-type', 'success');
  await expect(page.getByTestId('fi-purchase-map-notice')).toContainText('版本 1');
  // Screen and saved data agree: version 1 with trimmed codes.
  expect(companies['1']).toMatchObject({ version: 1, codes: { payableAccountCode: '2101', purchaseAccountCode: '5111', inputTaxAccountCode: '1151' } });
  await expect(page.getByTestId('fi-purchase-map-code-payableAccountCode')).toHaveText('2101');
  await expect(page.getByTestId('fi-purchase-map-unconfigured')).toHaveCount(0);
  await capture(page, 'created-light-zh-Hant');
  await page.getByTestId('fi-purchase-map-save').click();
  await expect(page.getByTestId('fi-purchase-map-notice')).toHaveAttribute('data-type', 'warning');
  await expect(page.getByTestId('fi-purchase-map-notice')).toContainText('沒有變更');
  expect(companies['1'].version).toBe(1);
  expect(writes).toHaveLength(2);
  await chooseTheme(page, 'dark'); await capture(page, 'unchanged-dark-zh-Hant');
  await fill(page, { purchaseAccountCode: '5112' });
  await page.getByTestId('fi-purchase-map-save').click();
  await expect(page.getByTestId('fi-purchase-map-notice')).toContainText('已修改科目（版本 2）');
  await expect(page.getByTestId('fi-purchase-map-name-purchaseAccountCode')).toHaveText('進貨費用');
  expect(companies['1'].codes!.purchaseAccountCode).toBe('5112');
});

test('FI-PUR-MAP-UI-004/008/009 unusable saved accounts, per-field refusals and a stale version change nothing', async ({ page, pageErrors }) => {
  pageErrors.allow(/Failed to load resource: the server responded with a status of (409|422)/);
  const md = chart();
  const companies: Record<string, Saved> = { '1': configured() };
  await mockMap(page, companies, md);
  md['1151'].isActive = false;
  await page.goto('/gl/purchase-invoice-account-map');
  await expect(page.getByTestId('fi-purchase-map-unusable')).toContainText('過帳會失敗');
  await expect(page.getByTestId('fi-purchase-map-inactive-inputTaxAccountCode')).toHaveText('已停用');
  await capture(page, 'unusable-light-zh-Hant');
  await fill(page, { payableAccountCode: '9999', purchaseAccountCode: '5100', inputTaxAccountCode: '1159' });
  await page.getByTestId('fi-purchase-map-save').click();
  await expect(page.getByTestId('fi-purchase-map-save-error')).toContainText('沒有儲存任何內容');
  await expect(page.getByTestId('fi-purchase-map-field-error-payableAccountCode')).toHaveText('主檔沒有此代碼的科目。');
  await expect(page.getByTestId('fi-purchase-map-input-payableAccountCode')).toBeFocused();
  await expect(page.getByTestId('fi-purchase-map-field-error-purchaseAccountCode')).toContainText('最底層科目');
  await expect(page.getByTestId('fi-purchase-map-field-error-inputTaxAccountCode')).toContainText('已停用');
  expect(companies['1']).toMatchObject({ version: 2, codes: { payableAccountCode: '2101' } });
  await expectReadableText(page.locator('.field-error'));
  for (const field of FIELDS) await expect(page.getByTestId(`fi-purchase-map-input-${field}`)).toHaveClass(/ant-input-status-error/);
  await capture(page, 'account-invalid-light-zh-Hant');
  await chooseTheme(page, 'dark'); await expectReadableText(page.locator('.field-error'));
  await capture(page, 'account-invalid-dark-zh-Hant');
  // Another user saves version 3 meanwhile.
  companies['1'] = { version: 3, updatedAt: '2026-11-14T05:00:00',
    codes: { payableAccountCode: '2101', purchaseAccountCode: '5112', inputTaxAccountCode: '1151' } };
  md['1151'].isActive = true;
  await fill(page, { payableAccountCode: '2101', purchaseAccountCode: '5111', inputTaxAccountCode: '1151' });
  await page.getByTestId('fi-purchase-map-save').click();
  await expect(page.getByTestId('fi-purchase-map-save-error')).toContainText('其他使用者已先修改這些科目');
  await expect(page.getByTestId('fi-purchase-map-version')).toContainText('版本 3');
  await expect(page.getByTestId('fi-purchase-map-input-purchaseAccountCode')).toHaveValue('5112');
  expect(companies['1'].codes!.purchaseAccountCode).toBe('5112');
  await capture(page, 'stale-dark-zh-Hant');
});

test('FI-PUR-MAP-UI-011 server error and network loss on read, then recovery', async ({ page, pageErrors }) => {
  pageErrors.allowApi5xx(/\/api\/v1\/gl\/purchase-invoice-account-map/, 500);
  pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
  pageErrors.allow(/Failed to load resource: net::ERR_FAILED/);
  let mode: 'fail' | 'offline' | 'ok' = 'fail';
  await page.route('**/api/v1/gl/purchase-invoice-account-map', route => {
    if (mode === 'fail') return route.fulfill({ status: 500, json: { traceId: 'trace-purchase-map-500' } });
    if (mode === 'offline') return route.abort('failed');
    return route.fulfill({ json: { configured: false, version: 0, payableAccount: null, purchaseAccount: null, inputTaxAccount: null,
      createdAt: null, createdBy: null, updatedAt: null, updatedBy: null } });
  });
  await page.goto('/gl/purchase-invoice-account-map');
  await expect(page.getByTestId('fi-purchase-map-error')).toContainText('trace-purchase-map-500');
  await expect(page.getByTestId('fi-purchase-map-form')).toHaveCount(0);
  await capture(page, 'server-error-light-zh-Hant');
  mode = 'offline'; await page.getByTestId('fi-purchase-map-retry').click();
  await expect(page.getByTestId('fi-purchase-map-error')).toContainText('無法連線到伺服器');
  await capture(page, 'network-error-light-zh-Hant');
  mode = 'ok'; await page.getByTestId('fi-purchase-map-retry').click();
  await expect(page.getByTestId('fi-purchase-map-error')).toHaveCount(0);
  await expect(page.getByTestId('fi-purchase-map-unconfigured')).toBeVisible();
});

test('FI-PUR-MAP-UI-012 company switch reads that company\'s map', async ({ page }) => {
  await mockMap(page, { '1': configured(), '2': { version: 0, codes: null, updatedAt: null } });
  await page.goto('/gl/purchase-invoice-account-map');
  await expect(page.getByTestId('fi-purchase-map-code-payableAccountCode')).toHaveText('2101');
  await page.getByTestId('layout-company-select').click();
  await page.locator('nz-option-item', { hasText: 'HK01 Hong Kong' }).click();
  await expect(page.getByTestId('fi-purchase-map-unconfigured')).toBeVisible();
  await expect(page.getByTestId('fi-purchase-map-input-payableAccountCode')).toHaveValue('');
});

test.describe('FI-PUR-MAP-UI-003 permissions', () => {
  test.describe('no permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.PERIOD.VIEW', dataScope: 'Company' }] } });
    test('hides the menu and route without reading the map', async ({ page }) => {
      let reads = 0;
      await page.route('**/api/v1/gl/purchase-invoice-account-map', route => { reads++; return route.abort(); });
      await page.goto('/gl/purchase-invoice-account-map');
      await expect(page).toHaveURL(/\/forbidden/);
      await page.getByTestId('layout-menu-fi').click();
      await expect(page.getByTestId('layout-menu-fi-gl-periods')).toBeVisible();
      await expect(page.getByTestId('layout-menu-fi-purchase-map')).toHaveCount(0);
      expect(reads).toBe(0);
    });
  });
  test.describe('view permission', () => {
    test.use({ authorization: { companies: [{ id: 1, code: 'TW01', name: 'Taipei', isDefault: true }],
      companyId: 1, isFullAccess: false, permissions: [{ code: 'GL.PURCHASE_MAP.VIEW', dataScope: 'Company' }] } });
    test('reads the map without any input; an unconfigured map asks to contact finance', async ({ page }) => {
      const companies: Record<string, Saved> = { '1': configured() };
      await mockMap(page, companies);
      await page.goto('/gl/purchase-invoice-account-map');
      await expect(page.getByTestId('fi-purchase-map-code-inputTaxAccountCode')).toHaveText('1151');
      await expect(page.locator('erp-purchase-map-page input')).toHaveCount(0);
      await expect(page.getByTestId('fi-purchase-map-form')).toHaveCount(0);
      await capture(page, 'view-only-light-zh-Hant');
      companies['1'] = { version: 0, codes: null, updatedAt: null };
      await page.reload();
      await expect(page.getByTestId('fi-purchase-map-unconfigured')).toContainText('請聯絡有編輯權限的財務人員');
      await capture(page, 'view-only-unconfigured-light-zh-Hant');
    });
  });
});
