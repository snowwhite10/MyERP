import { Page } from '@playwright/test';
import { chooseTheme, expect, expectCanvasPainted, openHome, test } from '../support/fixtures';

const DARK_LINK = 'link#erp-theme-dark';

/** 暗色 ng-zorro 樣式表已實際載入（不只是插入了 <link>）：CSSOM 中有規則。 */
function darkStylesheetRuleCount(page: Page): Promise<number> {
  return page.evaluate(() => {
    const link = document.getElementById('erp-theme-dark') as HTMLLinkElement | null;
    const sheet = link?.sheet;
    if (!sheet) {
      return 0;
    }
    try {
      return sheet.cssRules.length;
    } catch {
      return 0;
    }
  });
}

function bodyBackground(page: Page): Promise<string> {
  return page.evaluate(() => getComputedStyle(document.body).backgroundColor);
}

test.describe('主題切換', () => {
  test('亮色為預設，不載入暗色樣式', async ({ page }) => {
    await openHome(page);

    await expect(page.locator('html')).not.toHaveClass(/erp-dark/);
    await expect(page.locator(DARK_LINK)).toHaveCount(0);
    expect(await bodyBackground(page)).toBe('rgb(245, 247, 250)');
  });

  test('切換暗色：載入 theme-dark.css、套用暗色變數，重新整理後保留', async ({ page }) => {
    await openHome(page);

    const cssResponse = page.waitForResponse((response) => response.url().endsWith('/theme-dark.css'));
    await chooseTheme(page, 'dark');
    expect((await cssResponse).status()).toBe(200);

    await expect(page.locator('html')).toHaveClass(/erp-dark/);
    await expect(page.locator(DARK_LINK)).toHaveAttribute('href', 'theme-dark.css');
    await expect.poll(() => darkStylesheetRuleCount(page)).toBeGreaterThan(100);
    await expect.poll(() => bodyBackground(page)).toBe('rgb(20, 20, 20)');
    expect(await page.evaluate(() => getComputedStyle(document.body).color)).toBe('rgb(230, 230, 230)');
    await expectCanvasPainted(page.getByTestId('home-chart-trend'));

    await page.reload();
    await expect(page.getByTestId('home-title')).toBeVisible();
    await expect(page.locator('html')).toHaveClass(/erp-dark/);
    await expect.poll(() => darkStylesheetRuleCount(page)).toBeGreaterThan(100);
    expect(await page.evaluate(() => localStorage.getItem('myerp.theme'))).toBe('dark');
  });

  test('切回亮色時移除暗色樣式', async ({ page }) => {
    await openHome(page);
    await chooseTheme(page, 'dark');
    await expect(page.locator(DARK_LINK)).toHaveCount(1);

    await chooseTheme(page, 'light');

    await expect(page.locator('html')).not.toHaveClass(/erp-dark/);
    await expect(page.locator(DARK_LINK)).toHaveCount(0);
    await expect.poll(() => bodyBackground(page)).toBe('rgb(245, 247, 250)');
  });

  test('跟隨系統：作業系統為暗色時套用暗色', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await openHome(page);

    await expect(page.locator('html')).toHaveClass(/erp-dark/);
    await expect(page.locator(DARK_LINK)).toHaveCount(1);

    await page.emulateMedia({ colorScheme: 'light' });
    await expect(page.locator('html')).not.toHaveClass(/erp-dark/);
  });
});
