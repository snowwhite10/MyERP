import { defineConfig, devices } from '@playwright/test';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '../..');

export default defineConfig({
  testDir: './specs',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  retries: 0,
  use: {
    baseURL: 'http://localhost:4200',
    locale: 'zh-TW',
    timezoneId: 'Asia/Taipei',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project src/backend/MyERP.Api --launch-profile https',
      cwd: root,
      url: 'https://localhost:7180/health/ready',
      ignoreHTTPSErrors: true,
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: 'powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Invoke-Frontend.ps1 npm start',
      cwd: root,
      url: 'http://localhost:4200',
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
});
