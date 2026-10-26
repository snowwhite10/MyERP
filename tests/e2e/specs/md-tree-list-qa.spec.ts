import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';
import type { Page } from '@playwright/test';

const out = '../../tests/qa/MD/screenshots';
async function capture(page: Page, name: string): Promise<void> {
  await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
  await expect(page.locator('.ant-spin-blur')).toHaveCount(0);
  await page.waitForFunction(() => [...document.querySelectorAll('.ant-spin-container')]
    .every(element => getComputedStyle(element).opacity === '1'));
  await page.screenshot({ path: `${out}/${name}.png`, fullPage: true });
}
const pages = [
  { route: 'item-categories', api: 'item-categories', prefix: 'md-item-category', number: '8.36',
    name: '原物料', child: '金屬', code: 'RAW', childCode: 'METAL',
    empty: '沒有分類', noResults: '沒有符合搜尋的分類',
    data: [{ id: 1, code: 'RAW', name: '原物料', parentId: null, isActive: true, version: 1 },
      { id: 2, code: 'METAL', name: '金屬', parentId: 1, isActive: true, version: 1 }] },
  { route: 'accounts', api: 'accounts', prefix: 'md-account', number: '8.87',
    name: '資產', child: '現金', code: '1', childCode: '1101',
    empty: '沒有會計科目', noResults: '沒有符合搜尋的會計科目',
    data: [{ id: 1, code: '1', name: '資產', parentId: null, accountType: 'Asset',
      balanceDirection: 'Debit', isActive: true, isPostable: false, version: 1 },
    { id: 2, code: '1101', name: '現金', parentId: 1, accountType: 'Asset',
      balanceDirection: 'Debit', isActive: true, isPostable: true, version: 1 }] },
] as const;

for (const kind of pages) {
  for (const theme of ['light', 'dark'] as const) {
    test(`MD ${kind.number} tree list QA ${theme}`, async ({ page, pageErrors }) => {
      pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
      pageErrors.allowApi5xx(new RegExp(`/api/v1/md/${kind.api}$`), 500);
      let mode: 'normal' | 'empty' | 'error' = 'normal';
      let reads = 0;
      await page.route(`**/api/v1/md/${kind.api}**`, route => {
        if (route.request().method() === 'POST') return route.fulfill({ status: 500,
          contentType: 'application/problem+json', json: { traceId: 'trace-md-tree-save-500' } });
        reads++;
        if (mode === 'error') return route.fulfill({ status: 500, contentType: 'application/problem+json',
          json: { traceId: 'trace-md-tree-500' } });
        return route.fulfill({ json: mode === 'empty' ? [] : kind.data });
      });
      await page.goto(`/md/${kind.route}`);
      if (theme === 'dark') await chooseTheme(page, 'dark');
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.childCode}`)).toHaveAttribute('aria-level', '2');
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.code}`)).toBeVisible();
      await expect(page.locator(`label[for="${kind.prefix}-search"]`)).toBeVisible();
      await capture(page, `${kind.number}-normal-${theme}-zh-Hant`);

      const beforeSearch = reads;
      await page.getByTestId(`${kind.prefix}-search`).fill(kind.child);
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.childCode}`)).toBeVisible();
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.code}`)).toBeVisible();
      expect(reads).toBe(beforeSearch);
      await page.getByTestId(`${kind.prefix}-search`).fill('missing');
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(kind.noResults);
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.code}`)).toHaveCount(0);
      expect(reads).toBe(beforeSearch);
      await capture(page, `${kind.number}-no-results-${theme}-zh-Hant`);

      await page.getByTestId(`${kind.prefix}-search`).fill('');
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page).toHaveURL(new RegExp(`/md/${kind.route}$`));
      mode = 'empty';
      await page.reload();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(kind.empty);
      await capture(page, `${kind.number}-empty-${theme}-zh-Hant`);

      mode = 'error';
      await page.reload();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toContainText('trace-md-tree-500');
      await expect(page.getByTestId(`${kind.prefix}-table`)).toHaveCount(0);
      await capture(page, `${kind.number}-error500-${theme}-zh-Hant`);
      mode = 'normal';
      await page.getByTestId(`${kind.prefix}-retry`).click();
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.childCode}`)).toBeVisible();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toHaveCount(0);

      await page.getByTestId(`${kind.prefix}-create`).click();
      await page.getByTestId(`${kind.prefix}-code`).fill(kind.api === 'accounts' ? '2' : 'NEW');
      await page.getByTestId(`${kind.prefix}-name`).fill('New entry');
      if (kind.api === 'accounts') await page.getByTestId('md-account-type').selectOption({ label: '負債' });
      await page.locator('.ant-modal-footer .ant-btn-primary').click();
      await expect(page.getByTestId(`${kind.prefix}-save-error`)).toContainText('trace-md-tree-save-500');
      await expect(page.getByTestId(`${kind.prefix}-code`)).toHaveValue(kind.api === 'accounts' ? '2' : 'NEW');
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.api === 'accounts' ? '2' : 'NEW'}`)).toHaveCount(0);
      await capture(page, `${kind.number}-save500-${theme}-zh-Hant`);
    });
  }

  for (const language of ['zh-Hans', 'en'] as const) {
    test(`MD ${kind.number} tree list QA ${language}`, async ({ page }) => {
      await page.route(`**/api/v1/md/${kind.api}**`, route => route.fulfill({ json: kind.data }));
      await page.goto(`/md/${kind.route}`);
      await chooseLanguage(page, language);
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.childCode}`)).toBeVisible();
      await capture(page, `${kind.number}-normal-light-${language}`);
    });
  }
}
