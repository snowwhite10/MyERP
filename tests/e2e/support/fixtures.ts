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

export const test = base.extend<{ pageErrors: PageErrors }>({
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
