import { expect, test } from '../support/fixtures';

test.describe('路由與錯誤頁', () => {
  test('不存在的路徑顯示 404 頁，可返回首頁', async ({ page }) => {
    await page.goto('/no-such-page');

    await expect(page.getByTestId('error-page-404')).toBeVisible();
    await page.getByTestId('error-page-home').click();
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByTestId('home-title')).toBeVisible();
  });

  test('/forbidden 顯示 403 頁', async ({ page }) => {
    await page.goto('/forbidden');

    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});

test.describe('E2E 靜態伺服器（serve-dist.mjs）', () => {
  test.skip(!!process.env['E2E_BASE_URL'], '只在使用內建 webServer 時檢查');

  test('缺少的靜態檔回 404，不以 index.html 掩蓋', async ({ request }) => {
    expect((await request.get('/missing-file.js')).status()).toBe(404);
    expect((await request.get('/i18n/en/missing.json')).status()).toBe(404);
    expect((await request.get('/api/v1/anything')).status()).toBe(404);
  });

  test('Angular 路由回 index.html', async ({ request }) => {
    const response = await request.get('/some/deep/route');
    expect(response.status()).toBe(200);
    expect(await response.text()).toContain('<erp-root>');
  });

  test('拒絕跳出根目錄的路徑', async ({ request }) => {
    // 根目錄 src/frontend/dist/myerp-frontend/browser 往上三層是 src/frontend，該處確有 package.json
    expect((await request.get('/..%2F..%2F..%2Fpackage.json')).status()).toBe(404);
    expect((await request.get('/%2e%2e/%2e%2e/%2e%2e/package.json')).status()).toBe(404);
    expect((await request.get('/..%5C..%5C..%5Cpackage.json')).status()).toBe(404);
  });

  test('i18n 語言檔可取得', async ({ request }) => {
    const response = await request.get('/i18n/zh-Hant/layout.json');
    expect(response.status()).toBe(200);
    expect(await response.json()).toHaveProperty('home.welcome', '歡迎使用 MyERP');
  });
});
