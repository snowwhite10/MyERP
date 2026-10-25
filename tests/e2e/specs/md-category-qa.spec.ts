import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const out = '../../tests/qa/MD/screenshots';
const row = { id: 1, code: 'RETAIL', name: 'Retail', isActive: true, version: 1 };

for (const theme of ['light', 'dark'] as const) {
  test(`MD 8.30 category list QA ${theme}`, async ({ page, pageErrors }) => {
    pageErrors.allow(/Failed to load resource: the server responded with a status of 400/);
    pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
    pageErrors.allowApi5xx(/\/api\/v1\/md\/customer-categories(?:\?.*)?$/, 500);
    let mode: 'normal' | 'empty' | 'error' | 'server' = 'normal';
    let reads = 0;
    await page.route('**/api/v1/md/customer-categories**', route => {
      if (route.request().method() === 'POST') return route.fulfill({ status: 500, json: { traceId: 'trace-md-save-500' } });
      reads++;
      if (mode === 'error') return route.fulfill({ status: 400, json: { errorCode: 'COMMON.REQUEST.INVALID' } });
      if (mode === 'server') return route.fulfill({ status: 500, json: { traceId: 'trace-md-500' } });
      const keyword = new URL(route.request().url()).searchParams.get('keyword');
      const items = mode === 'normal' && !keyword ? [row] : [];
      return route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    });
    await page.goto('/md/customer-categories');
    if (theme === 'dark') await chooseTheme(page, 'dark');
    await expect(page.getByTestId('md-category-row-RETAIL')).toBeVisible();
    await expect(page.getByTestId('md-category-search')).toHaveAttribute('aria-label', /代碼|名稱/);
    await page.screenshot({ path: `${out}/8.30-normal-${theme}-zh-Hant.png`, fullPage: true });

    mode = 'empty';
    await page.getByTestId('md-category-search').fill('');
    await page.getByTestId('md-category-search').press('Enter');
    await expect(page.getByTestId('md-category-table')).toContainText('沒有分類');
    await page.screenshot({ path: `${out}/8.30-empty-${theme}-zh-Hant.png`, fullPage: true });

    mode = 'normal';
    await page.getByTestId('md-category-search').fill('missing');
    await page.getByTestId('md-category-search').press('Enter');
    await expect(page.getByTestId('md-category-table')).toContainText('沒有符合搜尋的分類');
    await expect(page.getByTestId('md-category-row-RETAIL')).toHaveCount(0);
    await page.screenshot({ path: `${out}/8.30-no-results-${theme}-zh-Hant.png`, fullPage: true });

    mode = 'error';
    await page.getByTestId('md-category-search').press('Enter');
    await expect(page.getByTestId('md-category-load-error')).toBeVisible();
    await page.screenshot({ path: `${out}/8.30-error400-${theme}-zh-Hant.png`, fullPage: true });
    mode = 'normal';
    const beforeRetry = reads;
    await page.getByTestId('md-category-retry').click();
    await expect(page.getByTestId('md-category-load-error')).toHaveCount(0);
    expect(reads).toBeGreaterThan(beforeRetry);

    mode = 'server';
    await page.getByTestId('md-category-search').press('Enter');
    await expect(page.getByTestId('md-category-load-error')).toContainText('trace-md-500');
    await page.screenshot({ path: `${out}/8.30-error500-${theme}-zh-Hant.png`, fullPage: true });
    mode = 'normal';
    await page.getByTestId('md-category-retry').click();

    await page.getByTestId('md-category-create').click();
    await page.locator('.ant-modal-footer .ant-btn-primary').click();
    await expect(page.locator('.category-form [role="alert"]')).toHaveCount(2);
    await page.screenshot({ path: `${out}/8.30-validation-${theme}-zh-Hant.png`, fullPage: true });
    await page.getByTestId('md-category-code').fill('NEW');
    await page.getByTestId('md-category-name').fill('New category');
    await page.locator('.ant-modal-footer .ant-btn-primary').click();
    await expect(page.getByTestId('md-category-save-error')).toContainText('trace-md-save-500');
    await expect(page.getByTestId('md-category-code')).toHaveValue('NEW');
    await expect(page.getByTestId('md-category-row-NEW')).toHaveCount(0);
    await page.screenshot({ path: `${out}/8.30-save500-${theme}-zh-Hant.png`, fullPage: true });
  });
}

for (const language of ['zh-Hans', 'en'] as const) {
  test(`MD 8.30 category list QA ${language}`, async ({ page }) => {
    await page.route('**/api/v1/md/customer-categories**', route => route.fulfill({
      json: { items: [row], page: 1, pageSize: 20, totalCount: 1 },
    }));
    await page.goto('/md/customer-categories');
    await chooseLanguage(page, language);
    await expect(page.getByTestId('md-category-row-RETAIL')).toBeVisible();
    await page.screenshot({ path: `${out}/8.30-normal-light-${language}.png`, fullPage: true });
  });
}

for (const kind of [
  { path: 'supplier-categories', interfaceNo: '8.33', title: '供應商分類' },
  { path: 'item-brands', interfaceNo: '8.39', title: '物料品牌' },
] as const) {
  for (const theme of ['light', 'dark'] as const) {
    test(`MD ${kind.interfaceNo} shared category QA ${theme}`, async ({ page }) => {
      let empty = false;
      await page.route(`**/api/v1/md/${kind.path}**`, route => route.fulfill({ json: {
        items: empty ? [] : [row], page: 1, pageSize: 20, totalCount: empty ? 0 : 1,
      } }));
      await page.goto(`/md/${kind.path}`);
      if (theme === 'dark') await chooseTheme(page, 'dark');
      await expect(page.getByTestId('md-category-title')).toHaveText(kind.title);
      await expect(page.getByTestId('md-category-row-RETAIL')).toBeVisible();
      await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
      await expect(page.locator('.ant-spin-blur')).toHaveCount(0);
      await page.waitForTimeout(350); // Let the nz-table loading fade finish before the screenshot.
      await page.screenshot({ path: `${out}/${kind.interfaceNo}-normal-${theme}-zh-Hant.png`, fullPage: true });
      empty = true;
      await page.getByTestId('md-category-search-button').click();
      await expect(page.getByTestId('md-category-row-RETAIL')).toHaveCount(0);
      await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
      await expect(page.locator('.ant-spin-blur')).toHaveCount(0);
      await page.waitForTimeout(350);
      await page.screenshot({ path: `${out}/${kind.interfaceNo}-empty-${theme}-zh-Hant.png`, fullPage: true });
    });
  }
}
