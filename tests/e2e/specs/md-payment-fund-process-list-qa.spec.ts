import { chooseLanguage, chooseTheme, expect, test } from '../support/fixtures';

const output = '../../tests/qa/MD/screenshots';
const lists = [
  { path: 'payment-terms', prefix: 'md-payment-term', interfaceNo: '8.89', code: 'NET30',
    row: { id: 1, code: 'NET30', name: '月結三十天', dueType: 'NetDays', days: 30, closingDay: null,
      months: null, paymentDay: null, isActive: true, version: 1 } },
  { path: 'fund-accounts', prefix: 'md-fund', interfaceNo: '8.20', code: 'CASH',
    row: { id: 1, code: 'CASH', name: '現金帳戶', fundType: 'Cash', currencyId: 1, bankName: '',
      bankAccountNo: '', ledgerAccountId: null, isActive: true, version: 1 } },
  { path: 'income-expense-categories', prefix: 'md-fund', interfaceNo: '8.42', code: 'INCOME',
    row: { id: 1, code: 'INCOME', name: '營業收入', direction: 'Income', ledgerAccountId: null, isActive: true, version: 1 } },
  { path: 'process-categories', prefix: 'md-process', interfaceNo: '8.51', code: 'ASM',
    row: { id: 1, code: 'ASM', name: '組裝', sortOrder: 1, processCount: 0, isActive: true, version: 1 } },
  { path: 'processes', prefix: 'md-process', interfaceNo: '8.27', code: 'CUT',
    row: { id: 1, code: 'CUT', name: '裁切', categoryId: null, uomId: null, standardRate: 10,
      sortOrder: 1, isActive: true, version: 1 } },
  { path: 'item-attributes', prefix: 'md-process', interfaceNo: '8.16', code: 'COLOR',
    row: { id: 1, code: 'COLOR', name: '顏色', sortOrder: 1, itemCount: 0, isActive: true, version: 1 } },
] as const;

for (const kind of lists) {
  for (const theme of ['light', 'dark'] as const) {
    test(`MD ${kind.interfaceNo} list QA ${theme}`, async ({ page, pageErrors }) => {
      pageErrors.allow(/Failed to load resource: the server responded with a status of 500/);
      pageErrors.allowApi5xx(new RegExp(`/api/v1/md/${kind.path}(?:\\?.*)?$`), 500);
      let mode: 'normal' | 'empty' | 'error' = 'normal';
      const keywords: (string | null)[] = [];
      await page.route('**/api/v1/md/accounts**', route => route.fulfill({ json: [] }));
      await page.route('**/api/v1/md/currencies**', route => route.fulfill({ json: { items: [
        { id: 1, code: 'TWD', name: '新臺幣', isActive: true },
      ], page: 1, pageSize: 20, totalCount: 1 } }));
      await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
      await page.route('**/api/v1/md/process-categories**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
      await page.route(`**/api/v1/md/${kind.path}**`, route => {
        if (route.request().method() !== 'GET') return route.fallback();
        const keyword = new URL(route.request().url()).searchParams.get('keyword');
        keywords.push(keyword);
        if (mode === 'error') return route.fulfill({ status: 500, json: { traceId: 'trace-md-027b4' } });
        const items = mode === 'normal' && !keyword ? [kind.row] : [];
        return route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
      });
      await page.goto(`/md/${kind.path}`);
      if (theme === 'dark') await chooseTheme(page, 'dark');
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.code}`)).toBeVisible();
      await expect(page.getByTestId(`${kind.prefix}-search`)).toHaveAttribute('aria-label', /代碼|名稱/);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-normal-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'empty';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toBeVisible();
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.code}`)).toHaveCount(0);
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-empty-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'normal';
      await page.getByTestId(`${kind.prefix}-search`).fill('missing');
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-table`)).toContainText('沒有符合條件的資料');
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-no-results-${theme}-zh-Hant.png`, fullPage: true });

      mode = 'error';
      await page.getByTestId(`${kind.prefix}-search-button`).click();
      await expect(page.getByTestId(`${kind.prefix}-load-error`)).toContainText('trace-md-027b4');
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
      await page.route('**/api/v1/md/accounts**', route => route.fulfill({ json: [] }));
      await page.route('**/api/v1/md/currencies**', route => route.fulfill({ json: { items: [
        { id: 1, code: 'TWD', name: '新臺幣', isActive: true },
      ], page: 1, pageSize: 20, totalCount: 1 } }));
      await page.route('**/api/v1/md/uoms**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
      await page.route('**/api/v1/md/process-categories**', route => route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }));
      await page.route(`**/api/v1/md/${kind.path}**`, route => route.fulfill({ json: {
        items: [kind.row], page: 1, pageSize: 20, totalCount: 1,
      } }));
      await page.goto(`/md/${kind.path}`);
      await chooseLanguage(page, language);
      await expect(page.getByTestId(`${kind.prefix}-row-${kind.code}`)).toBeVisible();
      await page.screenshot({ path: `${output}/${kind.interfaceNo}-normal-light-${language}.png`, fullPage: true });
    });
  }
}
