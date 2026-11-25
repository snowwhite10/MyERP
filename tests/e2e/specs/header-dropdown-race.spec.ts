import { chooseLanguage, chooseTheme, expect, openHome, test } from '../support/fixtures';

test('SYS-HEADER-DROPDOWN-001 repeated language and theme choices close each menu', async ({ page }) => {
  test.setTimeout(90_000);
  await openHome(page);
  for (let index = 0; index < 8; index++) {
    const language = index % 3 === 0 ? 'en' : index % 3 === 1 ? 'zh-Hans' : 'zh-Hant';
    await chooseLanguage(page, language);
    await expect(page.locator('html')).toHaveAttribute('lang', language);
    await chooseTheme(page, index % 2 === 0 ? 'dark' : 'light');
    await expect(page.locator('html')).toHaveClass(index % 2 === 0 ? /erp-dark/ : /^(?!.*erp-dark)/);
  }
});

test('SYS-HEADER-DROPDOWN-002 selecting during enter animation closes the menu', async ({ page }) => {
  await openHome(page);
  await page.getByTestId('layout-language').click();
  const item = page.getByTestId('layout-language-en');
  const dropdown = page.locator('.ant-dropdown').filter({ has: item });
  await expect(dropdown).toHaveClass(/ant-slide-up-enter-active/);
  await item.click({ force: true });
  await expect(item).toBeHidden();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
});

test('SYS-HEADER-DROPDOWN-003 full page captures between language and theme choices keep menus closable', async ({ page }) => {
  test.setTimeout(90_000);
  await openHome(page);
  for (const theme of ['dark', 'light'] as const) {
    await page.evaluate(() => window.scrollTo(0, 0));
    await chooseTheme(page, theme);
    for (const language of ['zh-Hant', 'en', 'zh-Hans'] as const) {
      await page.evaluate(() => window.scrollTo(0, 0));
      await chooseLanguage(page, language);
      await expect(page.locator('html')).toHaveAttribute('lang', language);
      await page.screenshot({ fullPage: true, animations: 'disabled' });
    }
  }
});
