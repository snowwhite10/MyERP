import { defineConfig, devices } from '@playwright/test';

/**
 * MyERP E2E（MASTER_PROMPT 第 10、11 節；只在 VM 本機執行，不加入 CI）。
 * 執行：& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx playwright test
 *
 * webServer 先以 production 設定建置前端，再用 serve-dist.mjs 提供靜態檔案，
 * 讓測試對象與部署產物一致（例如 theme-dark.css 檔名不被 hash）。
 * 設定 E2E_BASE_URL 時改測已啟動的站台，不啟動 webServer。
 */
const port = Number(process.env['E2E_PORT'] ?? '4300');
const externalBaseUrl = process.env['E2E_BASE_URL'];
const baseURL = externalBaseUrl ?? `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL,
    // 預設語言由瀏覽器語言決定：zh-TW → 繁體（languages.ts detectLanguage）
    locale: 'zh-TW',
    timezoneId: 'Asia/Taipei',
    colorScheme: 'light',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: externalBaseUrl
    ? undefined
    : {
        command: `npm --prefix ../../src/frontend run build && node serve-dist.mjs --port ${port}`,
        url: baseURL,
        reuseExistingServer: !process.env['CI'],
        timeout: 240_000,
        stdout: 'ignore',
        stderr: 'pipe',
      },
});
