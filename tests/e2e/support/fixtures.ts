import { expect, Locator, Page, test as base } from '@playwright/test';

/**
 * 共用 fixture：
 * - pageErrors（自動）：收集瀏覽器 console error 與未捕捉例外，測試結束時必須為空。
 *   預期會出錯的測試可呼叫 pageErrors.allow(pattern) 放行特定訊息。
 */
export interface PageErrors {
  readonly messages: string[];
  allow(pattern: RegExp): void;
}

/** 與前端 AuthService 相同的 localStorage 鍵（PreferenceStorage 前綴 `myerp.`）。 */
export const AUTH_STORAGE_KEY = 'myerp.auth.session';

/** E2E 用的假 Session：只有前端檢查到期時間，靜態站台不會驗證 Token。 */
export const E2E_SESSION = {
  accessToken: 'e2e-access',
  accessTokenExpiresAt: '2099-12-31T00:00:00+08:00',
  refreshToken: 'e2e-refresh',
  refreshTokenExpiresAt: '2099-12-31T00:00:00+08:00',
  tokenType: 'Bearer',
  username: 'admin',
};

/** 假授權回應：admin 全權，兩間公司（台北為預設）。靜態站台沒有 API，由 page.route 提供（ADR-013）。 */
export const E2E_AUTHORIZATION = {
  companies: [
    { id: 1, code: 'TW01', name: 'Taipei', isDefault: true },
    { id: 2, code: 'HK01', name: 'Hong Kong', isDefault: false },
  ],
  companyId: 1,
  isFullAccess: true,
  permissions: [] as { code: string; dataScope: string }[],
};

export const test = base.extend<{ signedIn: boolean; session: void; authorization: typeof E2E_AUTHORIZATION; authorizationApi: string[]; pageErrors: PageErrors }>({
  /** 預設以已登入狀態開啟頁面；登入流程測試用 `test.use({ signedIn: false })`。 */
  signedIn: [true, { option: true }],

  // 每個分頁只在第一次載入前寫入 Session，登出後重新整理不會被重新寫入（ADR-013）
  session: [
    async ({ page, signedIn }, use) => {
      if (signedIn) {
        await page.addInitScript(
          ({ key, value }) => {
            if (!sessionStorage.getItem('e2e.seeded')) {
              localStorage.setItem(key, value);
              sessionStorage.setItem('e2e.seeded', '1');
            }
          },
          { key: AUTH_STORAGE_KEY, value: JSON.stringify(E2E_SESSION) },
        );
      }
      await use();
    },
    { auto: true },
  ],

  /** 測試可用 `test.use({ authorization: {...} })` 換成受限權限。 */
  authorization: [E2E_AUTHORIZATION, { option: true }],

  /** 模擬 GET /api/v1/sys/auth/authorization；回傳每次請求帶的 X-Company-Id（未帶為空字串）。 */
  authorizationApi: [
    async ({ page, authorization }, use) => {
      const companyHeaders: string[] = [];
      await page.route('**/api/v1/sys/notifications/unread-count', (route) => route.fulfill({ json: { count: 0 } }));
      // 客戶／供應商頁載入的付款條件與結算方式選項（ADR-047）預設為空；測試後註冊的 route 優先。
      await page.route(/\/api\/v1\/md\/(payment-terms|settlement-methods)\?/, (route) => route.request().method() === 'GET'
        ? route.fulfill({ json: { items: [], page: 1, pageSize: 100, totalCount: 0 } }) : route.fallback());
      await page.route('**/api/v1/sys/auth/authorization', async (route) => {
        const requested = route.request().headers()['x-company-id'] ?? '';
        companyHeaders.push(requested);
        const companyId = requested ? Number(requested) : authorization.companyId;
        await route.fulfill({ json: { ...authorization, companyId } });
      });
      await use(companyHeaders);
    },
    { auto: true },
  ],

  pageErrors: [
    async ({ page }, use) => {
      const messages: string[] = [];
      const allowed: RegExp[] = [];
      page.on('console', (message) => {
        if (message.type() === 'error') {
          messages.push(`console: ${message.text()}`);
        }
      });
      page.on('pageerror', (error) => messages.push(`pageerror: ${error.message}`));

      await use({ messages, allow: (pattern) => allowed.push(pattern) });

      const unexpected = messages.filter((m) => !allowed.some((pattern) => pattern.test(m)));
      expect(unexpected, '瀏覽器不應有 console error / 未捕捉例外').toEqual([]);
    },
    { auto: true },
  ],
});

export { expect };

/** 開啟首頁並等待框架與標題出現。 */
export async function openHome(page: Page): Promise<void> {
  await page.goto('/');
  await expect(page.getByTestId('home-title')).toBeVisible();
}

/** 由下拉選單選擇主題模式（light / dark / system）。 */
export async function chooseTheme(page: Page, mode: 'light' | 'dark' | 'system'): Promise<void> {
  await page.getByTestId('layout-theme').click();
  await pickDropdownItem(page, `layout-theme-${mode}`);
}

/** 由下拉選單選擇介面語言。 */
export async function chooseLanguage(page: Page, code: 'zh-Hant' | 'zh-Hans' | 'en'): Promise<void> {
  await page.getByTestId('layout-language').click();
  await pickDropdownItem(page, `layout-language-${code}`);
}

/**
 * 點選下拉選單項目並等待選單關閉。ng-zorro 下拉有開關動畫，
 * 不等關閉就再次開啟時，可能點到正在移除的舊選單項目。
 */
async function pickDropdownItem(page: Page, testId: string): Promise<void> {
  const item = page.getByTestId(testId);
  await item.click();
  await expect(item).toBeHidden();
}

/**
 * 圖表容器內的 canvas 已有實際繪製內容：尺寸大於 0，且至少有一個不透明像素。
 * 只檢查 DOM 有 canvas 不夠，G2 渲染失敗時仍會留下空白 canvas。
 */
export async function expectCanvasPainted(chart: Locator): Promise<void> {
  const canvas = chart.locator('canvas').first();
  await expect(canvas).toBeVisible();
  await expect
    .poll(
      () =>
        canvas.evaluate((element: HTMLCanvasElement) => {
          if (element.width === 0 || element.height === 0) {
            return 0;
          }
          const context = element.getContext('2d');
          if (!context) {
            return 0;
          }
          const pixels = context.getImageData(0, 0, element.width, element.height).data;
          let painted = 0;
          for (let i = 3; i < pixels.length; i += 4) {
            if (pixels[i] > 0) {
              painted++;
            }
          }
          return painted;
        }),
      { message: '圖表 canvas 應有繪製內容' },
    )
    .toBeGreaterThan(0);
}
