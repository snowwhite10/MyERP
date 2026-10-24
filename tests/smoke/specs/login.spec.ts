import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';

type DevelopmentSettings = {
  SeedUsers: { Username: string; Password: string }[];
  Captcha: { MasterCode: string };
};

test('README 預設埠：首頁、真實驗證碼、登入、登出', async ({ page }) => {
  const settings = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../src/backend/MyERP.Api/appsettings.Development.json'), 'utf8')) as DevelopmentSettings;
  const admin = settings.SeedUsers.find((user) => user.Username === 'admin');
  if (!admin || !settings.Captcha.MasterCode) throw new Error('Development 種子帳號或驗證碼設定缺失');

  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });
  page.on('response', (response) => {
    if (response.url().includes('/api/') && response.status() >= 500) errors.push(`${response.status()} ${response.url()}`);
  });

  await page.goto('/');
  await expect(page).toHaveURL(/\/login$/);
  const captchaResponse = page.waitForResponse((response) => response.url().includes('/api/v1/sys/auth/captcha') && response.ok());
  const image = page.getByTestId('captcha-image');
  await expect(image).toBeVisible();
  await expect.poll(async () => image.evaluate((element: HTMLImageElement) => element.complete && element.naturalWidth > 0)).toBe(true);
  // Reload explicitly checks the image through the dev-server proxy, including a real API response.
  await page.getByTestId('captcha-refresh').click();
  await captchaResponse;
  await expect.poll(async () => image.evaluate((element: HTMLImageElement) => element.complete && element.naturalWidth > 0)).toBe(true);

  await page.getByTestId('login-username').fill(admin.Username);
  await page.getByTestId('login-password').fill(admin.Password);
  await page.getByTestId('captcha-input').fill(settings.Captcha.MasterCode);
  await page.getByTestId('login-submit').click();
  await expect(page.getByTestId('home-title')).toBeVisible();
  await expect(page.getByTestId('layout-username')).toHaveText(admin.Username);
  await page.getByTestId('layout-logout').click();
  await expect(page).toHaveURL(/\/login$/);
  await page.goto('/');
  await expect(page).toHaveURL(/\/login$/);
  expect(errors).toEqual([]);
});
