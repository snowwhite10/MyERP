import { AUTH_STORAGE_KEY, expect, test } from '../support/fixtures';

test.use({ signedIn: false });

test('登入遇密碼到期時以帳密與 Captcha 修改並登入', async ({ page, pageErrors }) => {
  pageErrors.allow(/status of 403/);
  let captchaCount = 0;
  await page.route('**/api/v1/sys/auth/captcha', async (route) => {
    captchaCount++;
    await route.fulfill({ json: { captchaId: `c${captchaCount}`, image: 'data:image/svg+xml;base64,PHN2Zy8+', length: 5, expiresInSeconds: 120 } });
  });
  await page.route('**/api/v1/sys/auth/login', async (route) => {
    await route.fulfill({ status: 403, json: { errorCode: 'SYS.AUTH.PASSWORD_EXPIRED' } });
  });
  let changed: Record<string, string> | undefined;
  await page.route('**/api/v1/sys/auth/change-expired-password', async (route) => {
    changed = route.request().postDataJSON();
    await route.fulfill({ json: {
      accessToken: 'changed-access', accessTokenExpiresAt: '2099-12-31T00:00:00+08:00',
      refreshToken: 'changed-refresh', refreshTokenExpiresAt: '2099-12-31T00:00:00+08:00', tokenType: 'Bearer',
    } });
  });
  await page.goto('/login');
  await page.getByTestId('login-username').fill('admin');
  await page.getByTestId('login-password').fill('old-password');
  await page.getByTestId('captcha-input').fill('12345');
  await page.getByTestId('login-submit').click();
  await expect(page.getByTestId('login-change-expired')).toBeVisible();
  await page.getByTestId('login-change-expired').click();
  await expect(page).toHaveURL(/change-password\?expired=1/);
  await expect(page.getByTestId('password-username')).toHaveValue('admin');
  await page.getByTestId('password-current').fill('old-password');
  await page.getByTestId('password-new').fill('New-pass-2026!');
  await page.getByTestId('password-confirm').fill('New-pass-2026!');
  await page.getByTestId('captcha-input').fill('12345');
  await page.getByTestId('password-submit').click();
  await expect(page).toHaveURL(/\/$/);
  expect(changed).toMatchObject({ username: 'admin', currentPassword: 'old-password', newPassword: 'New-pass-2026!', captchaCode: '12345' });
  expect(await page.evaluate((key) => JSON.parse(localStorage.getItem(key)!).refreshToken, AUTH_STORAGE_KEY)).toBe('changed-refresh');
});
