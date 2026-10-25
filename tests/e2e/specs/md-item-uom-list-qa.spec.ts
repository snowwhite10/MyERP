import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const output = '../../tests/qa/MD/screenshots';
const lists = [
  { path: 'items', prefix: 'md-item', interfaceNo: '8.11', noun: '物料',
    row: { id: 1, code: 'BOLT', name: '螺栓', specification: 'M8', baseUomId: 1,
      brandId: null, categoryId: null, trackingMode: 'Lot', isActive: true, version: 1 } },
  { path: 'uoms', prefix: 'md-uom', interfaceNo: '8.45', noun: '單位',
    row: { id: 1, code: 'BOLT', name: '螺栓', decimalPlaces: 2, isActive: true, version: 1 } },
] as const;

async function references(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/v1/md/uom-conversions**', route => route.fulfill({ json: [] }));
  await page.route('**/api/v1/md/item-brands**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route('**/api/v1/md/item-categories**', route => route.fulfill({ json: [] }));
}

for (const kind of lists) {
  for (const theme of ['light', 'dark'] as const) {
    test(`MD ${kind.interfaceNo} list QA ${theme}`, async ({ page, pageErrors }) => {
      pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
      pageErrors.allowApi5xx(new RegExp(`/api/v1/md/${kind.path}(?:\\?.*)?$`), 500);
      let mode: 'normal' | 'empty' | 'error' = 'normal';
      let reads = 0;
      const keywords: (string | null)[] = [];
      await references(page);
      await page.route(`**/api/v1/md/${kind.path}**`, route => {
        if (route.request().method() !== 'GET') return route.fallback();
        reads++;
        const url = new URL(route.request().url());
        if (url.searchParams.get('pageSize') === '200') {
          return route.fulfill({ json: { items: [], page: 1, pageSize: 200, totalCount: 0 } });
        }
        keywords.push(url.searchParams.get('keyword'));
        if (mode === 'error') return route.fulfill({ status: 500, json: { traceId: 'trace-md-list-500' } });
        const items = mode === 'normal' && !url.searchParams.get('keyword') ? [kind.row] : [];
        return route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
      });
      if (kind.path === 'items') {
        await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
      }
      await page.goto(`/md/${kind.path}`);
      if (theme === 'dark') await chooseTheme(page, 'dark');
      await expect(page.getByTestId(`${kind.prefix}-row-BOLT`)).toBeVisible();
      await expect(page.getByTestId(`${kind.prefix}-search`)).toHaveAttribute('aria-label', /代碼|名稱/);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-normal-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'empty';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(`沒有${kind.noun}`);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-empty-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'normal';
      await page.getByTestId(`${kind.prefix}-search`).fill('missing');
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText(`沒有符合搜尋的${kind.noun}`);
      await expect(page.getByTestId(`${kind.prefix}-row-BOLT`)).toHaveCount(0);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-no-results-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'error';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toContainText('trace-md-list-500');
      await expect(page.getByTestId(`${kind.prefix}-table`)).toHaveCount(0);
      if (kind.path === 'uoms') await expect(page.getByTestId('md-uom-conversion-table')).toBeVisible();
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-error500-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'normal';
      const beforeRetry = reads;
      await page.getByTestId(`${kind.prefix}-retry`).click();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toHaveCount(0);
      expect(reads).toBeGreaterThan(beforeRetry);
      expect(keywords.at(-1)).toBe('missing');
    });
  }

  for (const language of ['zh-Hans', 'en'] as const) {
    test(`MD ${kind.interfaceNo} list QA ${language}`, async ({ page }) => {
      await references(page);
      await page.route(`**/api/v1/md/${kind.path}**`, route => route.fulfill({ json: {
        items: [kind.row], page: 1, pageSize: 20, totalCount: 1,
      } }));
      if (kind.path === 'items') {
        await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
      }
      await page.goto(`/md/${kind.path}`);
      await chooseLanguage(page, language);
      await expect(page.getByTestId(`${kind.prefix}-row-BOLT`)).toBeVisible();
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-normal-light-${language}.png`, fullPage: true });
    });
  }
}
