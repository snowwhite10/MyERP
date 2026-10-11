import { AUTH_STORAGE_KEY, expect, test } from '../support/fixtures';

test('本人修改密碼後替換工作階段 Token', async ({ page }) => {
  let body: Record<string, string> | undefined;
  await page.route('**/api/v1/sys/auth/change-password', async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({ json: {
      accessToken: 'new-access', accessTokenExpiresAt: '2099-12-31T00:00:00+08:00',
      refreshToken: 'new-refresh', refreshTokenExpiresAt: '2099-12-31T00:00:00+08:00', tokenType: 'Bearer',
    } });
  });
  await page.goto('/');
  await page.getByTestId('home-change-password').click();
  await page.getByTestId('password-current').fill('old-password');
  await page.getByTestId('password-new').fill('New-pass-2026!');
  await page.getByTestId('password-confirm').fill('New-pass-2026!');
  await page.getByTestId('password-submit').click();
  await expect(page).toHaveURL(/\/$/);
  expect(body).toMatchObject({ currentPassword: 'old-password', newPassword: 'New-pass-2026!', refreshToken: 'e2e-refresh' });
  expect(await page.evaluate((key) => JSON.parse(localStorage.getItem(key)!).refreshToken, AUTH_STORAGE_KEY)).toBe('new-refresh');
});

test('個人設定的語言與主題在重新整理後保留', async ({ page }) => {
  await page.goto('/');
  await page.getByTestId('home-personal-settings').click();
  await expect(page.getByTestId('personal-settings-page')).toBeVisible();
  await page.getByTestId('personal-language').click();
  await page.locator('nz-option-item').filter({ hasText: 'English' }).click();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await page.getByTestId('personal-theme').click();
  await page.locator('nz-option-item').filter({ hasText: 'Dark' }).click();
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await expect(page.locator('html')).toHaveClass(/erp-dark/);
});
