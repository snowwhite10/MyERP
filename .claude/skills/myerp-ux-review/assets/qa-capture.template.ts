// QA 截圖範本：複製到 tests/qa/<模組>/<介面>.qa.spec.ts 後修改。
// 執行：npx playwright test tests/qa/<模組> --project=qa
import { test, Page } from '@playwright/test';

const MODULE = 'MD';            // 改為模組代碼
const SCREEN = '4.4.1.8.3';     // 改為 ui-spec 介面編號
const OUT = `tests/qa/${MODULE}/screenshots`;

async function shot(page: Page, state: string, theme: 'light' | 'dark', lang = 'zh-Hant') {
  await page.screenshot({ path: `${OUT}/${SCREEN}-${state}-${theme}-${lang}.png`, fullPage: true });
}

async function setTheme(page: Page, theme: 'light' | 'dark') {
  // 依專案實作調整：例如切換 localStorage 或點主題按鈕
  await page.emulateMedia({ colorScheme: theme });
}

/** 模擬 API 錯誤，不修改後端 */
async function mockApi(page: Page, urlPart: string, status: number, body: unknown) {
  await page.route(`**${urlPart}**`, route =>
    route.fulfill({ status, contentType: 'application/problem+json', body: JSON.stringify(body) }));
}

for (const theme of ['light', 'dark'] as const) {
  test.describe(`${SCREEN} ${theme}`, () => {
    test.beforeEach(async ({ page }) => {
      await setTheme(page, theme);
      // TODO：登入（使用測試帳號與 Development 萬用碼）
    });

    test('正常', async ({ page }) => {
      await page.goto('/md/suppliers');
      await shot(page, 'normal', theme);
    });

    test('空列表', async ({ page }) => {
      await mockApi(page, '/api/v1/md/suppliers', 200, { items: [], total: 0 });
      await page.goto('/md/suppliers');
      await shot(page, 'empty', theme);
    });

    test('伺服器錯誤', async ({ page }) => {
      await mockApi(page, '/api/v1/md/suppliers', 500, { title: 'Internal Server Error', traceId: 'qa-trace-001' });
      await page.goto('/md/suppliers');
      await shot(page, 'error500', theme);
    });

    // TODO：按 SKILL.md 第 1 步補齊其他狀態（409、422、無權限、部分成功、全部略過…）
  });
}
