import { expect, Page, test } from '@playwright/test';

// SYS-AUTH-017（WU-SYS-013、ADR-023）：以種子帳號連真實後端登入。不使用共用 fixture（不寫假 Session、不模擬 API）。
// 只在設定 E2E_API_URL 時執行：& ./tools/scripts/Invoke-RealApiE2E.ps1（建置、套用 migration、載入種子、啟動 API）。
const apiUrl = process.env['E2E_API_URL'];
const MASTER_CODE = '123456';

test.describe('真實 API 登入（種子帳號）', () => {
  test.skip(!apiUrl, '需要 E2E_API_URL：請用 tools/scripts/Invoke-RealApiE2E.ps1 執行');

  let consoleErrors: string[];

  test.beforeEach(({ page }) => {
    consoleErrors = [];
    page.on('console', (message) => {
      if (message.type() === 'error') {
        consoleErrors.push(message.text());
      }
    });
    page.on('pageerror', (error) => consoleErrors.push(error.message));
  });

  test('admin 登入後可在兩間示範公司查看各自分店，並可登出', async ({ page }) => {
    await login(page, 'admin', 'az5911');
    await expect(page.getByTestId('layout-username')).toHaveText('admin');
    await expect(page.getByTestId('layout-company-select')).toContainText('TW01');

    await page.goto('/sys/branches');
    for (const code of ['TPE', 'TXG', 'KHH']) {
      await expect(page.getByTestId(`sys-organization-row-${code}`)).toBeVisible();
    }
    await expect(page.getByTestId('sys-organization-create')).toBeVisible();

    await page.getByTestId('layout-company-select').click();
    await page.locator('nz-option-item', { hasText: 'HK01' }).click();
    await expect(page.getByTestId('layout-company-select')).toContainText('HK01');
    await expect(page.getByTestId('sys-organization-row-HKG')).toBeVisible();
    await expect(page.getByTestId('sys-organization-row-KLN')).toBeVisible();
    await expect(page.getByTestId('sys-organization-row-TPE')).toHaveCount(0);

    await page.getByTestId('layout-logout').click();
    await expect(page).toHaveURL(/\/login$/);
    await page.goto('/');
    await expect(page).toHaveURL(/\/login$/);
    expect(consoleErrors).toEqual([]);
  });

  test('viewer 唯讀：可查看分店但沒有新增按鈕', async ({ page }) => {
    await login(page, 'viewer', 'Demo@2026');

    await page.goto('/sys/branches');
    await expect(page.getByTestId('sys-organization-row-TPE')).toBeVisible();
    await expect(page.getByTestId('sys-organization-create')).toHaveCount(0);
    expect(consoleErrors).toEqual([]);
  });

  test('不存在的帳號顯示帳號或密碼錯誤', async ({ page }) => {
    await page.goto('/login');
    await fillLogin(page, 'e2e-nobody', 'Wrong@2026');

    await expect(page.getByTestId('login-error')).toContainText('帳號或密碼錯誤');
    await expect(page).toHaveURL(/\/login$/);
    expect(consoleErrors.filter((m) => !/status of 401/.test(m))).toEqual([]);
  });
});

async function login(page: Page, username: string, password: string): Promise<void> {
  await page.goto('/');
  await expect(page).toHaveURL(/\/login$/);
  await fillLogin(page, username, password);
  await expect(page.getByTestId('home-title')).toBeVisible();
}

async function fillLogin(page: Page, username: string, password: string): Promise<void> {
  await expect(page.getByTestId('captcha-image')).toBeVisible();
  await page.getByTestId('login-username').fill(username);
  await page.getByTestId('login-password').fill(password);
  await page.getByTestId('captcha-input').fill(MASTER_CODE);
  await page.getByTestId('login-submit').click();
}
