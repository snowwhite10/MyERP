import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const output = '../../tests/qa/MD/screenshots';
const lists = [
  { path: 'warehouses', prefix: 'md-warehouse', interfaceNo: '8.17', noResults: '沒有符合條件的倉庫',
    row: { id: 1, code: 'MAIN', name: '主倉庫', address: '台北', contactName: 'Amy', phone: '02-1', isActive: true, version: 1 },
    child: 'md-warehouse-locations-MAIN' },
  { path: 'currencies', prefix: 'md-currency', interfaceNo: '8.79', noResults: '沒有符合條件的幣別',
    row: { id: 1, code: 'USD', name: '美元', symbol: '$', decimalPlaces: 2, isBase: false, isActive: true, version: 1 },
    child: 'md-currency-rates-USD' },
  { path: 'tax-codes', prefix: 'md-tax-code', interfaceNo: '8.83', noResults: '沒有符合條件的稅別',
    row: { id: 1, code: 'VAT5', name: '營業稅', taxType: 'Both', currentRatePercent: 5, isActive: true, version: 1 },
    child: 'md-tax-code-rates-VAT5' },
] as const;

for (const kind of lists) {
  for (const theme of ['light', 'dark'] as const) {
    test(`MD ${kind.interfaceNo} list QA ${theme}`, async ({ page, pageErrors }) => {
      pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
      pageErrors.allowApi5xx(new RegExp(`/api/v1/md/${kind.path}(?:\\?.*)?$`), 500);
      let mode: 'normal' | 'empty' | 'error' = 'normal';
      const keywords: (string | null)[] = [];
      await page.route(`**/api/v1/md/${kind.path}**`, route => {
        if (route.request().method() !== 'GET') return route.fallback();
        const keyword = new URL(route.request().url()).searchParams.get('keyword');
        keywords.push(keyword);
        if (mode === 'error') return route.fulfill({ status: 500, json: { traceId: 'trace-md-027b3' } });
        const items = mode === 'normal' && !keyword ? [kind.row] : [];
        return route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
      });
      await page.goto(`/md/${kind.path}`);
      if (theme === 'dark') await chooseTheme(page, 'dark');
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.row.code}`)).toBeVisible();
      await expect(page.getByTestId(kind.child)).toBeVisible();
      await expect(page.getByTestId(`${kind.prefix}-search`)).toHaveAttribute('aria-label', /代碼|名稱/);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-normal-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'empty';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(/沒有.*(倉庫|幣別|稅別)/);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-empty-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'normal';
      await page.getByTestId(`${kind.prefix}-search`).fill(' missing ');
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(kind.noResults);
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.row.code}`)).toHaveCount(0);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-no-results-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'error';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toContainText('trace-md-027b3');
      await expect(page.getByTestId(`${kind.prefix}-table`)).toHaveCount(0);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-error500-${theme}-zh-Hant.png`, fullPage: true });
      await page.getByTestId(`${kind.prefix}-search`).fill('unsubmitted');
      mode = 'normal';
      await page.getByTestId(`${kind.prefix}-retry`).click();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toHaveCount(0);
      expect(keywords.at(-1)).toBe('missing');
    });
  }

  for (const language of ['zh-Hans', 'en'] as const) {
    test(`MD ${kind.interfaceNo} list QA ${language}`, async ({ page }) => {
      await page.route(`**/api/v1/md/${kind.path}**`, route => route.fulfill({ json: {
        items: [kind.row], page: 1, pageSize: 20, totalCount: 1,
      } }));
      await page.goto(`/md/${kind.path}`);
      await chooseLanguage(page, language);
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.row.code}`)).toBeVisible();
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-normal-light-${language}.png`, fullPage: true });
    });
  }
}
