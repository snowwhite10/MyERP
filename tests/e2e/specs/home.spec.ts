import { expect, expectCanvasPainted, openHome, test } from '../support/fixtures';

test.describe('首頁冒煙測試', () => {
  test('載入主框架與首頁，預設繁體中文', async ({ page }) => {
    await openHome(page);

    await expect(page).toHaveTitle('MyERP');
    await expect(page.locator('html')).toHaveAttribute('lang', 'zh-Hant');
    await expect(page.getByTestId('home-title')).toHaveText('歡迎使用 MyERP');
    await expect(page.getByTestId('layout-sider')).toBeVisible();
    await expect(page.getByTestId('layout-menu')).toBeVisible();
  });

  test('G2 圖表在真實瀏覽器畫出 canvas', async ({ page }) => {
    await openHome(page);

    for (const testId of ['home-chart-trend', 'home-chart-compare', 'home-chart-share']) {
      await expectCanvasPainted(page.getByTestId(testId));
    }
    // KPI 卡的迷你趨勢圖（有 trend 輸入者）
    await expectCanvasPainted(page.getByTestId('home-kpi-revenue'));
  });

  test('KPI 卡顯示數值', async ({ page }) => {
    await openHome(page);

    await expect(page.getByTestId('home-kpi-revenue')).toContainText('1,330,000');
    await expect(page.getByTestId('home-kpi-orders')).toContainText('186');
  });

  test('側欄可收合與展開', async ({ page }) => {
    await openHome(page);
    const sider = page.getByTestId('layout-sider');
    const expandedWidth = (await sider.boundingBox())?.width ?? 0;

    await page.getByTestId('layout-menu-toggle').click();
    await expect.poll(async () => (await sider.boundingBox())?.width ?? 0).toBeLessThan(expandedWidth);

    await page.getByTestId('layout-menu-toggle').click();
    await expect.poll(async () => (await sider.boundingBox())?.width ?? 0).toBe(expandedWidth);
  });
});
