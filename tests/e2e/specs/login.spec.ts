import { AUTH_STORAGE_KEY, expect, test } from '../support/fixtures';
import { LOGIN_LIMIT_PER_MINUTE, MASTER_CODE, mockAuthApi, VALID_USER } from '../support/mock-auth-api';

// REQ_F4118.24.1–4：登入、錯誤訊息、returnUrl、登出（API 以 page.route 模擬，ADR-013）
test.describe('登入與登出', () => {
  test.use({ signedIn: false });

  test('未登入開啟首頁導向登入頁，以萬用碼登入後進入首頁並可登出', async ({ page }) => {
    const api = await mockAuthApi(page);

    await page.goto('/');
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByTestId('login-title')).toHaveText('登入');
    await expect(page.getByTestId('captcha-image')).toBeVisible();

    await page.getByTestId('login-username').fill(VALID_USER.username);
    await page.getByTestId('login-password').fill(VALID_USER.password);
    await page.getByTestId('captcha-input').fill(MASTER_CODE);
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByTestId('layout-username')).toHaveText('admin');
    expect(api.loginBodies).toEqual([
      { username: 'admin', password: VALID_USER.password, captchaId: 'captcha-1', captchaCode: MASTER_CODE },
    ]);

    // 重新整理後仍保持登入
    await page.reload();
    await expect(page.getByTestId('home-title')).toBeVisible();

    await page.getByTestId('layout-logout').click();
    await expect(page).toHaveURL(/\/login$/);
    expect(api.logoutBodies).toEqual([{ refreshToken: 'mock-refresh-1' }]);
    expect(await page.evaluate((key) => localStorage.getItem(key), AUTH_STORAGE_KEY)).toBeNull();

    // 登出後不能再進入首頁
    await page.goto('/');
    await expect(page).toHaveURL(/\/login$/);
  });

  test('密碼錯誤顯示訊息、清空密碼並更換驗證碼', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of 40[01]/);
    const api = await mockAuthApi(page);

    await page.goto('/login');
    await expect(page.getByTestId('captcha-image')).toBeVisible();
    await page.getByTestId('login-username').fill('admin');
    await page.getByTestId('login-password').fill('wrong-password');
    await page.getByTestId('captcha-input').fill(MASTER_CODE);
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('login-error')).toContainText('帳號或密碼錯誤');
    await expect(page.getByTestId('login-password')).toHaveValue('');
    await expect(page.getByTestId('captcha-input')).toHaveValue('');
    await expect.poll(() => api.captchaRequests).toBe(2);
    await expect(page.getByTestId('captcha-image')).toBeVisible();

    // 錯誤驗證碼回 CAPTCHA_INVALID
    await page.getByTestId('login-password').fill(VALID_USER.password);
    await page.getByTestId('captcha-input').fill('9999');
    await page.getByTestId('login-submit').click();
    await expect.poll(() => api.loginBodies).toHaveLength(2);
    await expect(page.getByTestId('login-error')).toContainText('驗證碼錯誤');
    await expect(page).toHaveURL(/\/login$/);
  });

  test('登入嘗試超過每分鐘上限時顯示請稍後再試（三語）', async ({ page, pageErrors }) => {
    pageErrors.allow(/status of (401|429)/);
    const api = await mockAuthApi(page);

    await page.goto('/login');
    await page.getByTestId('login-username').fill('admin');
    for (let i = 0; i < LOGIN_LIMIT_PER_MINUTE; i++) {
      await page.getByTestId('login-password').fill('wrong-password');
      await page.getByTestId('captcha-input').fill(MASTER_CODE);
      await page.getByTestId('login-submit').click();
      await expect(page.getByTestId('login-error')).toContainText('帳號或密碼錯誤');
      await expect.poll(() => api.captchaRequests).toBe(i + 2);
    }

    await page.getByTestId('login-password').fill(VALID_USER.password);
    await page.getByTestId('captcha-input').fill(MASTER_CODE);
    await page.getByTestId('login-submit').click();
    await expect(page.getByTestId('login-error')).toContainText('操作過於頻繁');
    await expect(page).toHaveURL(/\/login$/);

    await page.getByTestId('login-language-en').click();
    await expect(page.getByTestId('login-error')).toContainText('Too many attempts');
    await page.getByTestId('login-language-zh-Hans').click();
    await expect(page.getByTestId('login-error')).toContainText('操作过于频繁');
  });

  test('欄位未填時顯示提示，不送出請求', async ({ page }) => {
    const api = await mockAuthApi(page);
    await page.goto('/login');
    await page.getByTestId('login-submit').click();

    await expect(page.getByRole('alert')).toHaveCount(3);
    expect(api.loginBodies).toEqual([]);
  });

  test('登入後回到原本要開的頁面', async ({ page }) => {
    await mockAuthApi(page);
    await page.goto('/forbidden');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fforbidden$/);

    await page.getByTestId('login-username').fill('ADMIN');
    await page.getByTestId('login-password').fill(VALID_USER.password);
    await page.getByTestId('captcha-input').fill(MASTER_CODE);
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('error-page-403')).toBeVisible();
    await expect(page).toHaveURL(/\/forbidden$/);
  });

  test('登入頁可切換三種語言', async ({ page }) => {
    await mockAuthApi(page);
    await page.goto('/login');

    await page.getByTestId('login-language-en').click();
    await expect(page.getByTestId('login-title')).toHaveText('Sign in');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');

    await page.getByTestId('login-language-zh-Hans').click();
    await expect(page.getByTestId('login-title')).toHaveText('登录');
  });
});
