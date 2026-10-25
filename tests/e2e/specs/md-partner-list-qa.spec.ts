import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const out = '../../tests/qa/MD/screenshots';

for (const kind of [
  { path: 'customers', prefix: 'md-customer', interfaceNo: '8.1', noun: '客戶' },
  { path: 'suppliers', prefix: 'md-supplier', interfaceNo: '8.6', noun: '供應商' },
] as const) {
  for (const theme of ['light', 'dark'] as const) {
    test(`MD ${kind.interfaceNo} partner list QA ${theme}`, async ({ page, pageErrors }) => {
      pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
      pageErrors.allowApi5xx(new RegExp(`/api/v1/md/${kind.path}(?:\\?.*)?$`), 500);
      const row = { id: 1, code: 'ACME', name: 'Acme', categoryId: null, isActive: true, version: 1, reviewStatus: 'Approved' };
      let mode: 'normal' | 'empty' | 'error' = 'normal';
      let reads = 0;
      await page.route(`**/api/v1/md/${kind.path}**`, route => {
        if (route.request().method() !== 'GET') return route.fallback();
        reads++;
        if (mode === 'error') return route.fulfill({ status: 500, json: { traceId: 'trace-partner-500' } });
        const keyword = new URL(route.request().url()).searchParams.get('keyword');
        const items = mode === 'normal' && !keyword ? [row] : [];
        return route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
      });
      await page.route(`**/api/v1/md/${kind.path === 'customers' ? 'customer' : 'supplier'}-categories**`, route =>
        route.fulfill({ json: { items: [], page: 1, pageSize: 100, totalCount: 0 } }));
      await page.goto(`/md/${kind.path}`);
      if (theme === 'dark') await chooseTheme(page, 'dark');
      await expect(page.getByTestId(`${kind.prefix}-row-ACME`)).toBeVisible();
      await expect(page.getByTestId(`${kind.prefix}-search`)).toHaveAttribute('aria-label', /代碼|名稱/);
      await page.screenshot({ path: `${out}/${kind.interfaceNo}-normal-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'empty';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(`沒有${kind.noun}`);
      await page.screenshot({ path: `${out}/${kind.interfaceNo}-empty-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'normal';
      await page.getByTestId(`${kind.prefix}-search`).fill('missing');
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(`沒有符合搜尋的${kind.noun}`);
      await expect(page.getByTestId(`${kind.prefix}-row-ACME`)).toHaveCount(0);
      await page.screenshot({ path: `${out}/${kind.interfaceNo}-no-results-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'error';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toContainText('trace-partner-500');
      await expect(page.getByTestId(`${kind.prefix}-table`)).toHaveCount(0);
      await page.screenshot({ path: `${out}/${kind.interfaceNo}-error500-${theme}-zh-Hant.png`, fullPage: true });
      mode = 'normal';
      const beforeRetry = reads;
      await page.getByTestId(`${kind.prefix}-retry`).click();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toHaveCount(0);
      expect(reads).toBeGreaterThan(beforeRetry);
    });
  }

  for (const language of ['zh-Hans', 'en'] as const) {
    test(`MD ${kind.interfaceNo} partner list QA ${language}`, async ({ page }) => {
      await page.route(`**/api/v1/md/${kind.path}**`, route => route.fulfill({ json: {
        items: [{ id: 1, code: 'ACME', name: 'Acme', categoryId: null, isActive: true, version: 1, reviewStatus: 'Approved' }],
        page: 1, pageSize: 20, totalCount: 1,
      } }));
      await page.route(`**/api/v1/md/${kind.path === 'customers' ? 'customer' : 'supplier'}-categories**`, route =>
        route.fulfill({ json: { items: [], page: 1, pageSize: 100, totalCount: 0 } }));
      await page.goto(`/md/${kind.path}`);
      await chooseLanguage(page, language);
      await expect(page.getByTestId(`${kind.prefix}-row-ACME`)).toBeVisible();
      await page.screenshot({ path: `${out}/${kind.interfaceNo}-normal-light-${language}.png`, fullPage: true });
    });
  }
}
