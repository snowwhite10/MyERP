import { chooseLanguage, expect, openHome, test } from '../support/fixtures';

test.describe('語言切換', () => {
  test('切換英文與簡體，<html lang> 與文字同步，重新整理後保留', async ({ page }) => {
    await openHome(page);

    await chooseLanguage(page, 'en');
    await expect(page.getByTestId('home-title')).toHaveText('Welcome to MyERP');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');

    await chooseLanguage(page, 'zh-Hans');
    await expect(page.getByTestId('home-title')).toHaveText('欢迎使用 MyERP');
    await expect(page.locator('html')).toHaveAttribute('lang', 'zh-Hans');

    await page.reload();
    await expect(page.getByTestId('home-title')).toHaveText('欢迎使用 MyERP');
    expect(await page.evaluate(() => localStorage.getItem('myerp.lang'))).toBe('zh-Hans');
  });

  test.describe('瀏覽器語言為英文', () => {
    test.use({ locale: 'en-US' });

    test('沒有本機偏好時依瀏覽器語言顯示英文', async ({ page }) => {
      await openHome(page);

      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
      await expect(page.getByTestId('home-title')).toHaveText('Welcome to MyERP');
    });
  });
});
