import { E2E_AUTHORIZATION, expect, test } from '../support/fixtures';

test('MD 主檔入口可載入三語資源', async ({ page }) => {
  await page.goto('/md');
  await expect(page.getByRole('heading', { name: '主檔管理' })).toBeVisible();
  await page.getByTestId('layout-language').click();
  await page.getByTestId('layout-language-en').click();
  await expect(page.getByRole('heading', { name: 'Master Data' })).toBeVisible();
});

test.describe('MD 權限', () => {
  test.use({ authorization: { ...E2E_AUTHORIZATION, isFullAccess: false, permissions: [] } });

  test('沒有 MD.MASTER.VIEW 無法進入主檔入口', async ({ page }) => {
    await page.goto('/md');
    await expect(page.getByTestId('error-page-403')).toBeVisible();
  });
});
